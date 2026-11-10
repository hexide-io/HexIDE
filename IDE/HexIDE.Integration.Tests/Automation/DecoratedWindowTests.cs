using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// A window whose content starts inside its surface (#3575). Under client-side decorations, which is native Wayland's on
/// GNOME, the title bar and shadow are drawn in the same surface as the window, so a window's own coordinates and the ones
/// its platform input is in differ by that inset. On every other platform here they are the same, which is how the tools
/// came to mix them.
/// </summary>
/// <remarks>
/// Avalonia sets the inset only for a real platform that asks for drawn decorations, so these put it there by reflection,
/// where the backend would. That property is internal: if a bump renames it, these fail rather than pass with no inset.
/// </remarks>
public class DecoratedWindowTests : WindowTestFixture
{
    private static readonly Thickness Inset = new(9, 39, 9, 9);
    private static readonly Color Top = Color.FromRgb(0, 200, 0);
    private static readonly Color Bottom = Color.FromRgb(0, 0, 200);

    private sealed record Scene(Window Window, Button Button, Border Pad);

    private static Scene Build(Size surface = default)
    {
        surface = surface == default ? new Size(418, 348) : surface;
        var button = new Button { Name = "Go", Content = "go", Width = 80, Height = 30 };
        var pad = new Border { Background = Brushes.White, Width = 100, Height = 60 };
        var band = new Border { Background = new SolidColorBrush(Top), Width = surface.Width, Height = 5 };
        var foot = new Border { Background = new SolidColorBrush(Bottom), Width = surface.Width, Height = 5 };
        var canvas = new Canvas { Background = Brushes.White };
        foreach (var (control, left, top) in new (Control, double, double)[]
                     { (band, 0, 0), (button, 10, 10), (pad, 10, 60), (foot, 0, surface.Height - Inset.Top - Inset.Bottom - 5) })
        {
            Canvas.SetLeft(control, left);
            Canvas.SetTop(control, top);
            canvas.Children.Add(control);
        }
        var window = new Window { Content = canvas, Width = surface.Width, Height = surface.Height };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var host = window.GetVisualParent()!;
        host.GetType().GetProperty("DecorationInset", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, Inset);
        // In this mode a window's ClientSize is what is left inside the inset, as Avalonia's own Show makes it.
        typeof(TopLevel).GetProperty(nameof(TopLevel.ClientSize))!.GetSetMethod(nonPublic: true)!.Invoke(window,
            [new Size(surface.Width - Inset.Left - Inset.Right, surface.Height - Inset.Top - Inset.Bottom)]);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        // Twice: the press is hit-tested against what was drawn, and the frame the first tick starts is the one the inset is
        // committed in.
        for (var tick = 0; tick < 2; tick++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
        window.Bounds.Position.Should().Be(new Point(Inset.Left, Inset.Top), "the scene needs a window set in from its surface");
        return new Scene(window, button, pad);
    }

    [AvaloniaFact]
    public async Task A_click_presses_the_control_it_names()
    {
        var scene = Build();
        var clicked = 0;
        scene.Button.Click += (_, _) => clicked++;

        var outcome = await UiAutomationDriver.FinishAsync(UiAutomationDriver.Interact(scene.Button, "click", null));

        outcome.Success.Should().BeTrue(outcome.Error);
        clicked.Should().Be(1, $"the reply said: {outcome.Detail}");
        outcome.Detail.Should().Contain("which is (50, 25) in the window", "the reply stays in the window's own coordinates");
    }

    [AvaloniaFact]
    public void A_hover_reaches_the_control_it_names()
    {
        var scene = Build();

        var outcome = UiAutomationDriver.Hover(scene.Pad, null, null, "Window/Pad", out _);

        outcome.Success.Should().BeTrue(outcome.Error);
        scene.Pad.IsPointerOver.Should().BeTrue($"the reply said: {outcome.Detail}");
    }

    [AvaloniaFact]
    public async Task A_right_click_presses_and_releases_at_the_point_it_names()
    {
        var scene = Build();
        var seen = new List<Point>();
        scene.Pad.PointerPressed += (_, e) => seen.Add(e.GetPosition(scene.Pad));
        scene.Pad.PointerReleased += (_, e) => seen.Add(e.GetPosition(scene.Pad));

        var outcome = await UiAutomationDriver.FinishAsync(UiAutomationDriver.Interact(scene.Pad, "click", "30,20 right"));

        outcome.Success.Should().BeTrue(outcome.Error);
        seen.Should().Equal(new Point(30, 20), new Point(30, 20));
    }

    // The press is platform input and the held moves are raised on the control, and the two have to agree: the designer
    // moves a control by the difference between them (#3575).
    [AvaloniaFact]
    public async Task A_drag_moves_by_the_distance_it_names()
    {
        var scene = Build();
        var seen = new List<Point>();
        var parent = (Visual)scene.Pad.Parent!;
        scene.Pad.PointerPressed += (_, e) => { seen.Add(e.GetPosition(parent)); e.Pointer.Capture(scene.Pad); };
        scene.Pad.PointerMoved += (_, e) => seen.Add(e.GetPosition(parent));
        scene.Pad.PointerReleased += (_, e) => seen.Add(e.GetPosition(parent));

        var outcome = await UiAutomationDriver.FinishAsync(UiAutomationDriver.Interact(scene.Pad, "click", "20,15 to 60,35"));

        outcome.Success.Should().BeTrue(outcome.Error);
        // 20,15 on a pad at 10,60 is 30,75, and the release is 40 across and 20 down from it.
        seen.Should().HaveCountGreaterThan(2);
        seen[^1].Should().Be(new Point(70, 95));
        seen.Should().OnlyContain(p => p.X >= 30 && p.X <= 70 && p.Y >= 75 && p.Y <= 95,
            $"every point lies between the press and the release, and the pad saw {string.Join(" ", seen)}");
    }

    [AvaloniaFact]
    public void A_wheel_turns_over_the_control_it_names()
    {
        var scene = Build();
        var turned = 0;
        scene.Pad.PointerWheelChanged += (_, _) => turned++;

        var outcome = UiAutomationDriver.Interact(scene.Pad, "wheel", "down");

        outcome.Success.Should().BeTrue(outcome.Error);
        turned.Should().Be(1, $"the reply said: {outcome.Detail}");
    }

    // The watch reads input as the platform carries it, in the surface's coordinates, and compares it with the point hovered, in
    // the window's.
    [AvaloniaFact]
    public void The_hover_watch_tells_the_hover_s_own_move_from_another_s()
    {
        var scene = Build();
        UiAutomationDriver.Hover(scene.Pad, null, null, null, out var landing).Success.Should().BeTrue();
        var point = landing!.Value.Receiver.TranslatePoint(landing.Value.Point, scene.Window)!.Value;

        using (var watch = new UiAutomationDriver.ForeignPointerWatch(scene.Window, point))
        {
            UiAutomationDriver.Hover(scene.Pad, null, null, null, out _);
            watch.Moved.Should().BeNull("the hover's own move is at the point it hovered");
        }

        using (var watch = new UiAutomationDriver.ForeignPointerWatch(scene.Window, point))
        {
            UiAutomationDriver.Hover(scene.Button, null, null, null, out _);
            watch.Moved.Should().Contain("(50, 25) in the window");
        }
    }

    [AvaloniaFact]
    public void BoundingRect_is_measured_from_the_window_and_not_its_title_bar()
    {
        var scene = Build();

        var rect = UiAutomationDriver.Inspect(scene.Button, "Window/Button", scene.Window).BoundingRect;

        rect.Should().Equal(10, 10, 80, 30);
    }

    [AvaloniaFact]
    public async Task A_snapshot_is_the_window_from_its_first_row_to_its_last()
    {
        var scene = Build();

        using var snapshot = await SnapshotComposerTests.Drawn(SnapshotComposer.CaptureAsync(scene.Window));

        snapshot!.Image.PixelSize.Should().Be(new PixelSize(400, 300));
        snapshot.WindowOffset.Should().Be(default(PixelPoint));
        ColourOfRow(snapshot.Image, 2).Should().Be(Top, "the window's first rows are at the top of the image");
        ColourOfRow(snapshot.Image, 297).Should().Be(Bottom, "the window's last rows are at the bottom of it");
    }

    [AvaloniaFact]
    public void The_type_names_that_identify_the_native_Wayland_backend_are_still_its_own()
    {
        var backend = Assembly.Load("Avalonia.Wayland");

        backend.GetType("Avalonia.Wayland.WindowImpl").Should().NotBeNull();
        backend.GetType("Avalonia.Wayland.PopupImpl").Should().NotBeNull();
    }

    [AvaloniaFact]
    public void A_control_in_a_popup_on_native_Wayland_has_no_boundingRect_and_the_reply_says_why()
    {
        var item = new MenuItem { Header = "Edit Watch..." };
        var menu = new ContextMenu { Items = { item } };
        var owner = new Border { ContextMenu = menu };
        var window = PopupBoundsTests.NativePopupWindow(1.0);
        window.Content = owner;
        window.Show();
        NativeWayland.ForTest = _ => true;
        try
        {
            menu.Open(owner);
            Dispatcher.UIThread.RunJobs();

            var detail = UiAutomationDriver.Inspect(item, UiAutomationDriver.PathWithin(window, "Window", item)!, window);

            detail.BoundingRect.Should().BeNull();
            detail.BoundingRectNote.Should().Contain("compositor");
        }
        finally
        {
            NativeWayland.ForTest = null;
            menu.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_snapshot_on_native_Wayland_leaves_a_popup_out_and_says_why_rather_than_guessing_its_place()
    {
        var item = new MenuItem { Header = "Edit Watch..." };
        var menu = new ContextMenu { Items = { item } };
        var owner = new Border { ContextMenu = menu, Background = Brushes.White };
        var window = PopupBoundsTests.NativePopupWindow(1.0);
        window.Content = owner;
        window.Width = 300;
        window.Height = 200;
        window.Show();
        NativeWayland.ForTest = _ => true;
        try
        {
            menu.Open(owner);
            Dispatcher.UIThread.RunJobs();

            using var snapshot = await SnapshotComposerTests.Drawn(SnapshotComposer.CaptureAsync(window));

            snapshot!.LeftOut.Should().ContainSingle().Which.Reason.Should().Contain("compositor");
            snapshot.Image.PixelSize.Should().Be(new PixelSize(300, 200));
        }
        finally
        {
            NativeWayland.ForTest = null;
            menu.Close();
        }
    }

    // The colour of a row's middle pixel, in BGRA byte order.
    private static Color ColourOfRow(Bitmap image, int row)
    {
        using var bitmap = SnapshotComposerTests.Readable(image);
        var stride = bitmap.PixelSize.Width * 4;
        var pixels = new byte[stride * bitmap.PixelSize.Height];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(default, bitmap.PixelSize), handle.AddrOfPinnedObject(), pixels.Length, stride); }
        finally { handle.Free(); }
        var at = row * stride + bitmap.PixelSize.Width / 2 * 4;
        return bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888
            ? Color.FromRgb(pixels[at], pixels[at + 1], pixels[at + 2])
            : Color.FromRgb(pixels[at + 2], pixels[at + 1], pixels[at]);
    }
}
