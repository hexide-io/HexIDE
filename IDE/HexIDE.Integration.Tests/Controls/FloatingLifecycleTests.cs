using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using HexIDE.IDE;

namespace HexIDE.Integration.Tests.Controls;

public class FloatingLifecycleTests
{
    private static MainViewViewModel.DockFactory Factory(MainViewViewModel vm) =>
        (MainViewViewModel.DockFactory)typeof(MainViewViewModel)
            .GetField("dockFactory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;

    [AvaloniaTheory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task Fully_floating_region_keeps_home_proportions(bool grouped, int damage)
    {
        await using var owned = new RealComposition();
        var vm = owned.Composition.Root;
        var factory = Factory(vm);
        var keys = new[] { "immediate", "locals", "watches", "callStack" };
        var proportions = new[] { 0.10, 0.14, 0.22, 0.19 };
        var saved = LayoutManifest.Default with
        {
            Tools = LayoutManifest.Default.Tools.Select(t => Array.IndexOf(keys, t.Key) is var index && index >= 0
                ? t with { Open = true, Proportion = damage == 2 ? null : damage == 1 ? 0 : proportions[index] } : t).ToArray(),
            Floating = grouped
                ? [new FloatingLayoutState(["immediate", "locals"], "locals", 100, 120, 700, 220, WindowState.Normal),
                   new FloatingLayoutState(["watches"], "watches", 100, 120, 700, 220, WindowState.Normal),
                   new FloatingLayoutState(["callStack"], "callStack", 100, 120, 700, 220, WindowState.Normal)]
                : keys.Select(k => new FloatingLayoutState([k], k, 100, 120, 700, 220, WindowState.Normal)).ToArray(),
        };
        var layout = factory.ApplyLayout(saved);
        factory.InitLayout(layout);
        var view = new MainView();
        view.FindControl<DockControl>("DockControl")!.Layout = layout;
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var captured = factory.CaptureManifest(layout);
            if (damage == 0)
                captured.Tools.Where(t => keys.Contains(t.Key)).Select(t => t.Proportion)
                    .Should().Equal(proportions.Select(p => (double?)p));
            layout = factory.ApplyLayout(LayoutManifestJson.Deserialize(LayoutManifestJson.Serialize(captured))!);
            factory.InitLayout(layout);
            view.FindControl<DockControl>("DockControl")!.Layout = layout;
            Dispatcher.UIThread.RunJobs();
            captured = factory.CaptureManifest(layout);
            if (damage == 0)
                captured.Tools.Where(t => keys.Contains(t.Key)).Select(t => t.Proportion)
                    .Should().Equal(proportions.Select(p => (double?)p));
            var host = (Window)layout.Windows!.First().Host!;
            host.Close();
            Dispatcher.UIThread.RunJobs();
            var home = (IDock)vm.Immediate.Owner!;
            home.VisibleDockables.Should().Equal(grouped ? [vm.Immediate, vm.Locals] : new IDockable[] { vm.Immediate });
            home.Proportion.Should().BeGreaterThan(0);
            home.Proportion.Should().BeApproximately(damage == 0 ? 0.10 : 0.3, 0.00001);
            ((IDockable)home).GetVisibleBounds(out _, out _, out _, out var height);
            height.Should().BeGreaterThan(0);
            foreach (var remaining in layout.Windows?.ToArray() ?? []) ((Window)remaining.Host!).Close();
            Dispatcher.UIThread.RunJobs();
            home.Proportion.Should().BeGreaterThan(0);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Layout_switch_replaces_owned_floating_windows(int cycles)
    {
        await using var owned = new RealComposition();
        var vm = owned.Composition.Root;
        vm.ResetWindowLayout();
        var factory = Factory(vm);
        var view = new MainView { DataContext = vm };
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        try
        {
            window.Show();
            vm.OpenImmediateTool();
            factory.FloatDockable(vm.Immediate);
            Dispatcher.UIThread.RunJobs();
            var saved = factory.CaptureManifest(vm.Layout);
            vm.NamedLayouts.Delete("floating");
            vm.NamedLayouts.Save("floating", saved).Should().BeTrue();
            for (var cycle = 0; cycle <= cycles; cycle++)
            {
                vm.FullEditorLayout();
                Dispatcher.UIThread.RunJobs();
                window.OwnedWindows.Where(MainViewViewModel.DockFactory.IsToolHostWindow).Should().BeEmpty();
                vm.SwitchNamedLayout("floating");
                Dispatcher.UIThread.RunJobs();
                var host = window.OwnedWindows.Where(MainViewViewModel.DockFactory.IsToolHostWindow)
                    .Should().ContainSingle().Subject;
                host.IsVisible.Should().BeTrue();
                host.Should().BeSameAs(vm.Layout.Windows!.Single().Host);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Restored_group_returns_to_one_home_dock_with_saved_height(bool attachFirst, bool topology)
    {
        await using var owned = new RealComposition();
        var vm = owned.Composition.Root;
        vm.ResetWindowLayout();
        var factory = Factory(vm);
        var view = new MainView { DataContext = vm };
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        try
        {
            window.Show();
            vm.OpenImmediateTool();
            vm.OpenLocalsTool();
            vm.OpenWatchesTool();
            var home = (IDock)vm.Immediate.Owner!;
            factory.MoveDockable((IDock)vm.Locals.Owner!, home, vm.Locals, vm.Immediate);
            home.Proportion = 0.23;
            Dispatcher.UIThread.RunJobs();
            var proportion = home.Proportion;
            factory.FloatAllDockables(vm.Immediate);
            Dispatcher.UIThread.RunJobs();
            vm.NamedLayouts.Delete("floating-group");
            vm.NamedLayouts.Save("floating-group", factory.CaptureManifest(vm.Layout)).Should().BeTrue();
            await using var restoredOwned = new RealComposition();
            var restoredVm = restoredOwned.Composition.Root;
            var restoredFactory = Factory(restoredVm);
            var saved = LayoutManifestJson.Deserialize(LayoutManifestJson.Serialize(vm.NamedLayouts.Get("floating-group")!))!;
            if (!topology) saved = saved with { Topology = null };
            var restoredView = new MainView();
            var control = restoredView.FindControl<DockControl>("DockControl")!;
            var restoredWindow = new Window { Content = restoredView, Width = 1000, Height = 700 };
            try
            {
                if (attachFirst) restoredWindow.Show();
                var stateService = Substitute.For<IWindowStateService>();
                stateService.LoadLayoutManifest().Returns(saved);
                typeof(MainViewViewModel.DockFactory).GetField("windowStateService", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(restoredFactory, stateService);
                var layout = restoredFactory.CreateLayout();
                restoredFactory.InitLayout(layout);
                control.Layout = layout;
                if (!attachFirst) restoredWindow.Show();
                Dispatcher.UIThread.RunJobs();
                ((Window)layout.Windows!.Single().Host!).Close();
                Dispatcher.UIThread.RunJobs();
                vm = restoredVm;
                Dispatcher.UIThread.RunJobs();
                vm.Immediate.Owner.Should().BeSameAs(vm.Locals.Owner);
                var returned = (IDock)vm.Immediate.Owner!;
                returned.VisibleDockables.Should().Equal(vm.Immediate, vm.Locals);
                returned.Proportion.Should().BeApproximately(proportion, 0.00001);
                ((IDockable)returned).GetVisibleBounds(out _, out _, out _, out var height);
                height.Should().BeGreaterThan(0);
            }
            finally { restoredWindow.Close(); }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Owner_close_veto_preserves_hosts_and_confirmed_exit_captures_floating(bool confirm)
    {
        await using var owned = new RealComposition();
        var vm = owned.Composition.Root;
        vm.ResetWindowLayout();
        var factory = Factory(vm);
        var window = new Window { Content = new DockControl { Layout = vm.Layout }, Width = 1000, Height = 700 };
        window.Show();
        vm.OpenImmediateTool();
        factory.FloatDockable(vm.Immediate);
        Dispatcher.UIThread.RunJobs();
        var host = (Window)((IRootDock)vm.Layout).Windows!.Single().Host!;
        var title = host.Title;
        var floatingDock = vm.Immediate.Owner;
        var confirmed = false;
        string? saved = null;
        var state = Substitute.For<IWindowStateService>();
        state.When(s => s.SaveLayoutManifestJson(Arg.Any<string>()))
            .Do(call => saved = call.Arg<string>());
        typeof(MainViewViewModel).GetField("windowStateService", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, state);
        EventHandler<WindowClosingEventArgs> closing = (_, e) =>
        {
            if (!confirmed) e.Cancel = true;
            else vm.SaveLayout();
        };
        window.Closing += closing;
        try
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            window.IsVisible.Should().BeTrue();
            host.IsVisible.Should().BeTrue();
            host.Title.Should().Be(title);
            vm.Immediate.Owner.Should().BeSameAs(floatingDock);
            ((IDock)floatingDock!).VisibleDockables.Should().Contain(vm.Immediate);
            factory.FindRoot(vm.Immediate).Should().NotBeSameAs(vm.Layout);
            if (confirm)
            {
                confirmed = true;
                window.Close();
                LayoutManifestJson.Deserialize(saved!)!.Floating.Should().ContainSingle()
                    .Which.Tools.Should().Equal("immediate");
                host.IsVisible.Should().BeFalse();
            }
        }
        finally
        {
            window.Closing -= closing;
            host.Close();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Default_shutdown_saves_floating_before_closing_owner()
    {
        await using var owned = new RealComposition();
        var vm = owned.Composition.Root;
        vm.ResetWindowLayout();
        var factory = Factory(vm);
        var window = new Window { Content = new MainView { DataContext = vm }, Width = 1000, Height = 700 };
        var app = Avalonia.Application.Current!;
        var field = typeof(Avalonia.Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(Avalonia.Controls.ApplicationLifetimes.IApplicationLifetime));
        var previous = field.GetValue(app);
        var force = Static.ForceCloseWithoutPrompt;
        var lifetime = Substitute.For<Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime>();
        lifetime.MainWindow.Returns(window);
        using var context = new HexIDE.Desktop.Server.IdeContext(null!, null!, Substitute.For<HexIDE.Lsp.ILspClient>(),
            null!, Substitute.For<IProjectRunnerService>(), null!, null!, null!, null!, null!, vm,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        string? saved = null;
        var state = Substitute.For<IWindowStateService>();
        state.When(s => s.SaveLayoutManifestJson(Arg.Any<string>())).Do(call => saved = call.Arg<string>());
        typeof(MainViewViewModel).GetField("windowStateService", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, state);
        try
        {
            window.Show();
            vm.OpenImmediateTool();
            factory.FloatDockable(vm.Immediate);
            Dispatcher.UIThread.RunJobs();
            var host = (Window)vm.Layout.Windows!.Single().Host!;
            lifetime.Windows.Returns(new[] { window, host });
            field.SetValue(app, lifetime);
            window.Closing += (_, _) => vm.SaveLayout();
            HexIDE.Desktop.Server.IdeShutdown.Prepare(context, force: true).DialogsClosed.Should().Be(0);
            host.IsVisible.Should().BeTrue();
            HexIDE.Desktop.Server.IdeShutdown.CloseMainWindow();
            LayoutManifestJson.Deserialize(saved!)!.Floating.Should().ContainSingle().Which.Tools.Should().Equal("immediate");
            host.IsVisible.Should().BeFalse();
        }
        finally
        {
            field.SetValue(app, previous);
            Static.ForceCloseWithoutPrompt = force;
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Attachment_and_layout_switch_present_saved_windows(bool switchLayout)
    {
        await using var owned = new RealComposition();
        var vm = owned.Composition.Root;
        vm.ResetWindowLayout();
        var factory = Factory(vm);
        var saved = LayoutManifest.Default with
        {
            Floating = [new FloatingLayoutState(["immediate", "locals"], "locals", 100, 120, 700, 220, WindowState.Maximized)]
        };
        var layout = switchLayout ? (IRootDock)vm.Layout : factory.ApplyLayout(saved);
        factory.InitLayout(layout);
        var view = new MainView();
        var control = view.FindControl<DockControl>("DockControl")!;
        control.Layout = layout;
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            if (switchLayout)
            {
                layout = factory.ApplyLayout(saved);
                factory.InitLayout(layout);
                control.Layout = layout;
                Dispatcher.UIThread.RunJobs();
            }
            var host = (Window)layout.Windows.Should().ContainSingle().Subject.Host!;
            host.IsVisible.Should().BeTrue();
            host.Owner.Should().BeSameAs(window);
            ((IDock)vm.Immediate.Owner!).VisibleDockables.Should().Equal(vm.Immediate, vm.Locals);
            ((IDock)vm.Immediate.Owner!).ActiveDockable.Should().BeSameAs(vm.Locals);
            host.WindowState.Should().Be(WindowState.Maximized);
            host.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            host.Position.Should().Be(new Avalonia.PixelPoint(100, 120));
            host.Width.Should().Be(700);
            host.Height.Should().Be(220);
        }
        finally
        {
            foreach (var floating in layout.Windows?.ToArray() ?? []) (floating.Host as Window)?.Close();
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unpresentable_window_returns_its_tools_home(bool throwOnShow)
    {
        await using var owned = new RealComposition();
        var vm = owned.Composition.Root;
        vm.ResetWindowLayout();
        var factory = Factory(vm);
        if (!throwOnShow)
            factory.HostWindowLocator![nameof(IDockWindow)] = () => Substitute.For<IHostWindow>();
        var layout = factory.ApplyLayout(LayoutManifest.Default with
        {
            Floating = [new FloatingLayoutState(["immediate", "locals"], "locals", 100, 120, 700, 220, WindowState.Normal)]
        });
        factory.InitLayout(layout);
        if (throwOnShow)
            ((Window)layout.Windows!.Single().Host!).Opened += (_, _) => throw new InvalidOperationException("Cannot present host");
        var view = new MainView();
        view.FindControl<DockControl>("DockControl")!.Layout = layout;
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            factory.FindRoot(vm.Immediate).Should().BeSameAs(layout);
            factory.FindRoot(vm.Locals).Should().BeSameAs(layout);
            layout.Windows.Should().BeNullOrEmpty();
        }
        finally { window.Close(); }
    }
}
