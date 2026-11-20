using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Runtime.Debugging;

namespace HexIDE.Integration.Tests.Automation;

// #4627: the first xdg_popup.configure can arrive after the tool first observes an open tip.
public class PopupPlacementReadinessTests
{
    [AvaloniaTheory]
    [InlineData(true, 0, false)]
    [InlineData(true, 20, true)]
    [InlineData(false, 0, true)]
    public void Placement_reads_the_platform_position_only_on_native_Wayland(bool native, int x, bool expected)
    {
        var window = new Window { Position = new PixelPoint(x, 0) };
        try
        {
            NativeWayland.ForTest = _ => native;
            NativeWayland.Placed(window).Should().Be(expected);
        }
        finally
        {
            NativeWayland.ForTest = null;
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true, 1.0)]
    [InlineData(true, 1.5)]
    [InlineData(false, 1.0)]
    [InlineData(false, 1.5)]
    public async Task Hover_waits_for_placement_or_explains_unknown_bounds(bool configure, double scaling)
    {
        await using var owned = new RealComposition();
        using var context = new IdeContext(null!, null!, Substitute.For<ILspClient>(),
            null!, null!, null!, null!, null!, null!, Substitute.For<IDebugController>(), null!, null!, null!, null!,
            null!, owned.Composition.Capture, null!, null!, null!);
        var tools = new HexIdeTools(context);
        var button = new Button { Content = "Add", Width = 120, Height = 40 };
        ToolTip.SetTip(button, "Add Form");
        ToolTip.SetShowDelay(button, 0);
        var window = PopupBoundsTests.NativePopupWindow(scaling);
        window.Content = button;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        desktop.MainWindow.Returns(window);
        desktop.Windows.Returns(new[] { window });
        var lifetime = typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(IApplicationLifetime));
        var previous = lifetime.GetValue(Application.Current!);
        var placed = false;
        var scheduled = false;
        Rect? expected = null;
        IDisposable? configureTimer = null;
        NativeWayland.ForTest = _ => true;
        NativeWayland.PlacedForTest = popup =>
        {
            if (configure && !scheduled)
            {
                scheduled = true;
                configureTimer = DispatcherTimer.RunOnce(() =>
                {
                    placed = true;
                    var changed = (Action<PixelPoint>)typeof(IWindowBaseImpl)
                        .GetProperty("PositionChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                        .GetValue(popup.PlatformImpl)!;
                    changed(new PixelPoint(170, 110));
                    expected = UiAutomationDriver.OpenTipBounds(button, window);
                }, TimeSpan.FromMilliseconds(150));
            }
            return placed;
        };
        try
        {
            lifetime.SetValue(Application.Current!, desktop);
            var reply = await tools.HoverAsync("Window/Button", dwellMs: 1000, window: "ide",
                ct: TestContext.Current.CancellationToken);
            reply.Success.Should().BeTrue(reply.Error);
            reply.Detail.Should().Contain("tip: Add Form");
            if (configure)
            {
                placed.Should().BeTrue("hover must wait for the dispatcher's position notification");
                expected.Should().NotBeNull();
                reply.TipBounds.Should().Equal(expected!.Value.X, expected.Value.Y, expected.Value.Width, expected.Value.Height);
                reply.Detail.Should().NotContain("position is unknown");
            }
            else
            {
                reply.TipBounds.Should().BeNull();
                reply.Detail.Should().Contain("compositor has not placed").And.Contain("hover again");
            }
        }
        finally
        {
            configureTimer?.Dispose();
            lifetime.SetValue(Application.Current!, previous);
            NativeWayland.ForTest = null;
            NativeWayland.PlacedForTest = null;
            ToolTip.SetIsOpen(button, false);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, 1.0)]
    [InlineData(false, 1.5)]
    [InlineData(true, 1.0)]
    [InlineData(true, 1.5)]
    public async Task Synchronous_reads_omit_unplaced_popups_and_explain_how_to_retry(bool capture, double scaling)
    {
        var item = new MenuItem { Header = "Edit Watch..." };
        var menu = new ContextMenu { Items = { item } };
        var owner = new Border { ContextMenu = menu };
        var window = PopupBoundsTests.NativePopupWindow(scaling);
        window.Content = owner;
        window.Show();
        try
        {
            menu.Open(owner);
            Dispatcher.UIThread.RunJobs();
            TopLevel.GetTopLevel(item).Should().BeOfType<PopupRoot>();
            NativeWayland.ForTest = _ => true;
            NativeWayland.PlacedForTest = _ => false;
            if (capture)
            {
                using var snapshot = await SnapshotComposerTests.Drawn(SnapshotComposer.CaptureAsync(window));
                var omitted = snapshot!.LeftOut.Should().ContainSingle().Subject;
                omitted.Located.Should().BeFalse();
                omitted.Reason.Should().Contain("compositor has not placed").And.Contain("snapshot again");
                snapshot.WindowOffset.Should().Be(default(PixelPoint));
            }
            else
            {
                var path = UiAutomationDriver.PathWithin(window, "Window", item)!;
                var detail = UiAutomationDriver.Inspect(item, path, window);
                detail.BoundingRect.Should().BeNull();
                detail.BoundingRectNote.Should().Contain("compositor has not placed").And.Contain("Inspect it again");
            }
        }
        finally
        {
            NativeWayland.ForTest = null;
            NativeWayland.PlacedForTest = null;
            menu.Close();
            window.Close();
        }
    }
}
