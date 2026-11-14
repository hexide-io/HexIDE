using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using HexIDE.Automation;
using HexIDE.Desktop.Server;

namespace HexIDE.Integration.Tests.Automation;

public class WindowChangeTests
{
    [AvaloniaTheory]
    [InlineData(1400, 900, 1282, 700)]
    [InlineData(845, 601, 845.3333333333334, 601.3333333333334)]
    [InlineData(1100, 620, 1100, 620)]
    [InlineData(1400, 900, 1281.3333333333333, 700, 1)]
    public async Task Layout_follows_the_platform_client_size(int width, int height, double actualWidth, double actualHeight, int x = 0)
    {
        var backing = new Window();
        backing.SetRenderScaling(1.5);
        var inner = backing.PlatformImpl!;
        var screen = (Screen)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(backing.Screens.All[0], null)!;
        typeof(Screen).GetProperty("WorkingArea")!.SetValue(screen, new PixelRect(0, 0, 1944, 1080));
        typeof(Screen).GetProperty("Bounds")!.SetValue(screen, screen.WorkingArea);
        typeof(Screen).GetProperty("Scaling")!.SetValue(screen, 1.5);
        var screens = PopupBoundsTests.ForwardingPlatform<IScreenImpl>.Wrap((method, args) => method.Name switch
        {
            "get_AllScreens" => new[] { screen },
            "get_ScreenCount" => 1,
            "ScreenFromPoint" or "ScreenFromWindow" or "ScreenFromRect" => screen,
            _ => null,
        });
        var largestRequest = new Size();
        Window? window = null;
        var resized = (Action<Size, WindowResizeReason>)null!;
        var platform = PopupBoundsTests.ForwardingPlatform<IWindowImpl>.Wrap((method, args) =>
        {
            if (method.Name == "get_RenderScaling" || method.Name == "get_DesktopScaling") return 1.5;
            if (method.Name == "get_FrameSize") return window is null ? null : window.ClientSize + new Size(14, 20);
            if (method.Name == "Resize")
            {
                var requested = (Size)args![0]!;
                largestRequest = new Size(Math.Max(largestRequest.Width, requested.Width), Math.Max(largestRequest.Height, requested.Height));
                var allowed = new Size(Math.Min(Math.Round(requested.Width * 1.5) / 1.5, (1944 - (window?.Position.X ?? 0)) / 1.5 - 14),
                    Math.Min(Math.Round(requested.Height * 1.5) / 1.5, 700));
                // Report the platform cap only after Avalonia has laid out at the explicit properties.
                Dispatcher.UIThread.Post(() =>
                {
                    method.Invoke(inner, new object[] { allowed, WindowResizeReason.Layout });
                    resized(allowed, WindowResizeReason.Layout);
                }, DispatcherPriority.Background);
                return null;
            }
            return method.Invoke(inner, args);
        });
        var factory = new Factory();
        var document = new Document { Title = "Code" };
        var documents = new DocumentDock { VisibleDockables = factory.CreateList<IDockable>(document), ActiveDockable = document };
        var diagnostics = new Tool { Title = "Diagnostics" };
        var bottom = new ToolDock
        {
            Proportion = 0.2, Alignment = Alignment.Bottom,
            VisibleDockables = factory.CreateList<IDockable>(diagnostics), ActiveDockable = diagnostics,
        };
        var immediate = new Tool { Title = "Immediate" };
        var locals = new Tool { Title = "Locals" };
        var stack = new ProportionalDock
        {
            Proportion = 0.4, Orientation = Dock.Model.Core.Orientation.Vertical,
            VisibleDockables = factory.CreateList<IDockable>(
                new ToolDock { VisibleDockables = factory.CreateList<IDockable>(immediate), ActiveDockable = immediate },
                new ProportionalDockSplitter(),
                new ToolDock { VisibleDockables = factory.CreateList<IDockable>(locals), ActiveDockable = locals },
                new ProportionalDockSplitter(), bottom),
        };
        var rows = new ProportionalDock
        {
            Orientation = Dock.Model.Core.Orientation.Vertical,
            VisibleDockables = factory.CreateList<IDockable>(documents, new ProportionalDockSplitter(), stack),
        };
        var layout = new RootDock { VisibleDockables = factory.CreateList<IDockable>(rows), ActiveDockable = rows };
        factory.InitLayout(layout);
        var root = new DockControl { Layout = layout };
        window = new Window(platform) { Width = 1282, Height = 700, Content = root };
        // Screens is a per-TopLevel wrapper over the platform service, not a member of IWindowImpl.
        typeof(TopLevel).GetField("_screens", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window,
            Activator.CreateInstance(typeof(Screens), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new object[] { screens }, null));
        var app = Application.Current!;
        var lifetime = typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(IApplicationLifetime));
        var previousLifetime = lifetime.GetValue(app);
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        desktop.MainWindow.Returns(window);
        desktop.Windows.Returns(new[] { window });
        lifetime.SetValue(app, desktop);
        window.Show();
        resized = (Action<Size, WindowResizeReason>)typeof(ITopLevelImpl)
            .GetProperty("Resized", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(inner)!;
        Dispatcher.UIThread.RunJobs();
        largestRequest = default;
        var largestLayout = new Size();
        root.LayoutUpdated += (_, _) => largestLayout = new Size(
            Math.Max(largestLayout.Width, root.Bounds.Width), Math.Max(largestLayout.Height, root.Bounds.Height));
        window.WindowState = WindowState.Maximized;
        try
        {
            var reply = await new HexIdeTools(null!).SetWindowStateAsync("Normal", x: x, y: 0, width: width, height: height,
                ct: TestContext.Current.CancellationToken);
            reply.Success.Should().BeTrue(reply.Error);
            reply.Window!.Width.Should().Be((int)actualWidth);
            reply.Window.Height.Should().Be((int)actualHeight);
            Dispatcher.UIThread.RunJobs();
            using var assertions = new AwesomeAssertions.Execution.AssertionScope();
            window.ClientSize.Should().Be(new Size(actualWidth, actualHeight));
            window.Width.Should().Be(window.ClientSize.Width);
            window.Height.Should().Be(window.ClientSize.Height);
            root.Bounds.Size.Should().Be(window.ClientSize);
            largestLayout.Width.Should().BeLessThanOrEqualTo(1282);
            largestLayout.Height.Should().BeLessThanOrEqualTo(700);
            largestRequest.Width.Should().BeLessThanOrEqualTo(1282, "layout must never receive the oversized width");
            largestRequest.Height.Should().BeLessThanOrEqualTo(700, "a later remeasure cannot repair cached dock proportions");
            var pane = root.GetVisualDescendants().OfType<ToolChromeControl>()
                .Single(control => ReferenceEquals(control.DataContext, bottom));
            var paneTop = pane.TranslatePoint(default, window)!.Value.Y;
            paneTop.Should().BeGreaterThanOrEqualTo(0);
            // Nested Dock panels round their edges independently at fractional scale; allow one device pixel.
            Math.Round(paneTop + pane.Bounds.Height, 1).Should().BeLessThanOrEqualTo(
                reply.Window.Client!.Height + Math.Round(1 / window.RenderScaling, 1));
            pane.Bounds.Height.Should().BeGreaterThan(0);
            var note = reply.Note;
            if (width <= 1282 && height <= 700)
                note.Should().BeNull();
            else
                note.Should().Contain("1400×900").And.Contain($"{actualWidth:0.#}×{actualHeight:0.#}")
                    .And.Contain("x/y/width/height").And.Contain("working area");

            // A later native/user resize must still own layout.
            resized(new Size(900, 500), WindowResizeReason.User);
            Dispatcher.UIThread.RunJobs();
            root.Bounds.Size.Should().Be(window.ClientSize);
        }
        finally { window.Close(); lifetime.SetValue(app, previousLifetime); }
    }

    [AvaloniaFact]
    public async Task Wait_reads_confirmation_after_a_later_dispatcher_post()
    {
        var confirmed = false;
        var wait = WindowChange.WaitAsync(() => confirmed, TestContext.Current.CancellationToken);
        wait.IsCompleted.Should().BeFalse();
        var later = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            confirmed = true;
        }, DispatcherPriority.Background);
        try
        {
            (await wait).Should().BeTrue();
            confirmed.Should().BeTrue();
        }
        finally { await later; }
    }

    [AvaloniaFact]
    public async Task Wait_reports_an_unapplied_request()
    {
        (await WindowChange.WaitAsync(() => false, TestContext.Current.CancellationToken, 25)).Should().BeFalse();
    }
}
