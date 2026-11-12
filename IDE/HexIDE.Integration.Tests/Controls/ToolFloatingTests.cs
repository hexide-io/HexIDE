using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Headless;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Dock.Model.Core;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using HexIDE.Tools;

namespace HexIDE.Integration.Tests.Controls;

public class ToolFloatingTests
{
    [AvaloniaFact]
    public async Task Float_Immediate_shows_owned_host_and_can_dock_back()
    {
        await using var owned = new RealComposition();
        var vm = owned.Composition.Root;
        var factory = (MainViewViewModel.DockFactory)typeof(MainViewViewModel)
            .GetField("dockFactory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
        var locator = new Avalonia.Controls.Templates.FuncDataTemplate<ImmediateToolViewModel>((_, _) =>
            new ImmediateToolView());
        Application.Current!.DataTemplates.Add(locator);
        var window = new Window
        {
            Content = new DockControl { Layout = vm.Layout }, Width = 1000, Height = 700,
        };
        try
        {
            vm.OpenImmediateTool();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            factory.FloatDockable(vm.Immediate);
            Dispatcher.UIThread.RunJobs();

            var floating = ((IRootDock)vm.Layout).Windows.Should().ContainSingle().Subject;
            var host = floating.Host.Should().BeAssignableTo<HostWindow>().Subject;
            host.IsVisible.Should().BeTrue();
            host.Owner.Should().BeSameAs(window);
            host.Title.Should().Be(vm.Immediate.Title);
            host.GetVisualDescendants().OfType<ImmediateToolView>().Should().ContainSingle();
            vm.GetToolWindows().Single(t => t.Name == "Immediate").Visible.Should().BeTrue();
            host.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Name == "PART_MaximizeRestoreButton").IsVisible.Should().BeTrue();

            host.Close();
            Dispatcher.UIThread.RunJobs();
            factory.FindRoot(vm.Immediate).Should().BeSameAs(vm.Layout);
            window.GetVisualDescendants().OfType<ImmediateToolView>().Should().ContainSingle();
            vm.GetToolWindows().Single(t => t.Name == "Immediate").Visible.Should().BeTrue();
        }
        finally
        {
            foreach (var floating in ((IRootDock)vm.Layout).Windows?.ToArray() ?? [])
                (floating.Host as Window)?.Close();
            window.Close();
            Application.Current.DataTemplates.Remove(locator);
        }
    }
    [AvaloniaFact]
    public Task Floating_title_close_hides_tool_and_View_returns_it_docked() => WithWorkspace((vm, factory, window, _) =>
    {
        factory.FloatDockable(vm.Immediate);
        Dispatcher.UIThread.RunJobs();
        var host = FloatingHost(vm);
        var close = host.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_CloseButton");
        Action click = () =>
        {
            ((IInvokeProvider)new ButtonAutomationPeer(close)).Invoke();
            Dispatcher.UIThread.RunJobs();
        };
        click.Should().NotThrow();
        Dispatcher.UIThread.RunJobs();
        vm.GetToolWindows().Single(t => t.Name == "Immediate").Visible.Should().BeFalse();
        window.GetVisualDescendants().OfType<ImmediateToolView>().Should().BeEmpty();
        vm.OpenImmediateTool();
        Dispatcher.UIThread.RunJobs();
        factory.FindRoot(vm.Immediate).Should().BeSameAs(vm.Layout);
        window.GetVisualDescendants().OfType<ImmediateToolView>().Should().ContainSingle();
    });

    [AvaloniaFact]
    public Task Float_return_cycles_preserve_docked_proportion() => WithWorkspace((vm, factory, _, _) =>
    {
        var before = factory.CaptureManifest(vm.Layout).Tools.Single(t => t.Key == "immediate").Proportion;
        for (var cycle = 0; cycle < 4; cycle++)
        {
            factory.FloatDockable(vm.Immediate);
            Dispatcher.UIThread.RunJobs();
            FloatingHost(vm).Close();
            Dispatcher.UIThread.RunJobs();
            factory.CaptureManifest(vm.Layout).Tools.Single(t => t.Key == "immediate").Proportion.Should().Be(before);
        }
    });

    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public Task Float_opens_at_pane_size_with_usable_minimum_inside_owner_screen(double scaling) =>
        WithWorkspace((vm, factory, window, composition) =>
        {
            ((IDockable)vm.Immediate.Owner!).GetVisibleBounds(out _, out _, out var width, out var height);
            factory.FloatDockable(vm.Immediate);
            Dispatcher.UIThread.RunJobs();
            var host = FloatingHost(vm);
            host.Width.Should().BeGreaterThanOrEqualTo(Math.Max(320, width));
            host.Height.Should().BeGreaterThanOrEqualTo(Math.Max(200, height));
            var screen = window.Screens.ScreenFromWindow(window)!;
            screen.WorkingArea.Contains(new PixelRect(host.Position,
                PixelSize.FromSize(host.ClientSize, window.RenderScaling))).Should().BeTrue();
        }, scaling);

    [AvaloniaFact]
    public Task Floating_title_follows_language_switch() => WithWorkspace((vm, factory, _, composition) =>
    {
        var strings = composition.LocalizationService;
        var original = strings.ActiveLanguage;
        try
        {
            strings.Apply("en");
            factory.FloatDockable(vm.Immediate);
            Dispatcher.UIThread.RunJobs();
            var host = FloatingHost(vm);
            var english = host.Title;
            strings.Apply("fr");
            Dispatcher.UIThread.RunJobs();
            vm.Immediate.Title.Should().NotBe(english);
            host.Title.Should().Be(vm.Immediate.Title);
        }
        finally { strings.Apply(original); }
    });

    [AvaloniaFact]
    public Task Floating_title_follows_active_tab() => WithWorkspace((vm, factory, _, _) =>
    {
        vm.OpenLocalsTool();
        factory.MoveDockable((IDock)vm.Locals.Owner!, (IDock)vm.Immediate.Owner!, vm.Locals, vm.Immediate);
        factory.FloatAllDockables(vm.Immediate);
        Dispatcher.UIThread.RunJobs();
        var host = FloatingHost(vm);
        var dock = (IDock)vm.Immediate.Owner!;
        dock.ActiveDockable = vm.Immediate;
        Dispatcher.UIThread.RunJobs();
        host.Title.Should().Be(vm.Immediate.Title);
        dock.ActiveDockable = vm.Locals;
        Dispatcher.UIThread.RunJobs();
        host.Title.Should().Be(vm.Locals.Title);
    });

    [AvaloniaFact]
    public Task Floating_tab_group_returns_together_to_its_old_slot() => WithWorkspace((vm, factory, _, _) =>
    {
        vm.OpenLocalsTool();
        factory.MoveDockable((IDock)vm.Locals.Owner!, (IDock)vm.Immediate.Owner!, vm.Locals, vm.Immediate);
        var oldDock = (IDock)vm.Immediate.Owner!;
        var oldParent = (IDock)oldDock.Owner!;
        var oldIndex = oldParent.VisibleDockables!.IndexOf(oldDock);
        factory.FloatDockable(oldDock);
        Dispatcher.UIThread.RunJobs();
        FloatingHost(vm).Close();
        Dispatcher.UIThread.RunJobs();
        vm.Immediate.Owner.Should().BeSameAs(vm.Locals.Owner);
        oldParent.VisibleDockables![oldIndex].Should().BeSameAs(vm.Immediate.Owner);
        ((IDock)vm.Immediate.Owner!).VisibleDockables.Should().Equal(vm.Immediate, vm.Locals);
    });

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Auto_ignores_floating_tools(bool runningForm) => WithWorkspace((vm, factory, window, _) =>
    {
        var form = new Window { Title = "Form1" };
        try
        {
            if (runningForm) form.Show();
            factory.FloatDockable(vm.Immediate);
            Dispatcher.UIThread.RunJobs();
            var host = FloatingHost(vm);
            HexIDE.IDE.ForegroundWindow.Pick("auto", window, [window, form, host]).Window.Should()
                .BeSameAs(runningForm ? form : window);
        }
        finally { form.Close(); }
    });

    [AvaloniaFact]
    public Task Floating_tool_is_named_and_listed_and_real_dialog_still_wins() => WithWorkspace((vm, factory, window, composition) =>
    {
        factory.FloatDockable(vm.Immediate);
        Dispatcher.UIThread.RunJobs();
        var host = FloatingHost(vm);
        HexIDE.IDE.ForegroundWindow.Pick(host.Title, window, [window, host]).Window.Should().BeSameAs(host);
        HexIDE.IDE.ForegroundWindow.Pick("Nope", window, [window, host]).Error.Should().Contain($"\"{host.Title}\"");
        var dialog = new Window { Title = host.Title };
        try
        {
            _ = dialog.ShowDialog(window);
            HexIDE.IDE.ForegroundWindow.Pick("auto", window, [window, dialog, host]).Window.Should().BeSameAs(dialog);
        }
        finally { dialog.Close(); }
    });

    [AvaloniaFact]
    public Task Forced_shutdown_preserves_floating_tool_and_closes_dialog() => WithWorkspace((vm, factory, window, _) =>
    {
        factory.FloatDockable(vm.Immediate);
        Dispatcher.UIThread.RunJobs();
        var host = FloatingHost(vm);
        var dialog = new Window { Title = "Find" };
        dialog.Show(window);
        var app = Application.Current!;
        var field = typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(Avalonia.Controls.ApplicationLifetimes.IApplicationLifetime));
        var previous = field.GetValue(app);
        var force = Static.ForceCloseWithoutPrompt;
        var lifetime = Substitute.For<Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime>();
        lifetime.MainWindow.Returns(window);
        lifetime.Windows.Returns(new[] { window, host, dialog });
        var runner = Substitute.For<HexIDE.IDE.IProjectRunnerService>();
        using var context = new HexIDE.Desktop.Server.IdeContext(null!, null!, Substitute.For<HexIDE.Lsp.ILspClient>(),
            null!, runner, null!, null!, null!, null!, null!, vm, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        try
        {
            field.SetValue(app, lifetime);
            var result = HexIDE.Desktop.Server.IdeShutdown.Prepare(context, force: true);
            host.IsVisible.Should().BeTrue();
            factory.FindRoot(vm.Immediate).Should().NotBeSameAs(vm.Layout);
            dialog.IsVisible.Should().BeFalse();
            result.DialogsClosed.Should().Be(1);
        }
        finally
        {
            field.SetValue(app, previous);
            Static.ForceCloseWithoutPrompt = force;
            dialog.Close();
        }
    });

    [AvaloniaFact]
    public Task Stale_ide_path_names_the_floating_window_to_dump() => WithWorkspace((vm, factory, window, _) =>
    {
        static System.Collections.Generic.IEnumerable<HexIDE.Automation.UiNode> Flatten(HexIDE.Automation.UiNode node) =>
            node.Children.SelectMany(Flatten).Prepend(node);
        var path = Flatten(HexIDE.Automation.UiAutomationDriver.Dump(window, "Window", 30, interactiveOnly: false))
            .First(n => n.DataContextType == nameof(ImmediateToolViewModel)).Path;
        HexIDE.IDE.ForegroundWindow.Resolve(path, "ide", window, window, window.Title!).Control.Should().NotBeNull();
        factory.FloatDockable(vm.Immediate);
        Dispatcher.UIThread.RunJobs();
        var host = FloatingHost(vm);
        var app = Application.Current!;
        var field = typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(Avalonia.Controls.ApplicationLifetimes.IApplicationLifetime));
        var previous = field.GetValue(app);
        var lifetime = Substitute.For<Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime>();
        lifetime.MainWindow.Returns(window);
        lifetime.Windows.Returns(new[] { window, host });
        try
        {
            field.SetValue(app, lifetime);
            var result = HexIDE.IDE.ForegroundWindow.Resolve(path, "ide", window, window, window.Title!);
            result.Control.Should().BeNull();
            result.Error.Should().Contain("floating").And.Contain($"window: \"{host.Title}\"").And.Contain("dump_visual_tree");
            var fresh = Flatten(HexIDE.Automation.UiAutomationDriver.Dump(host, "Window", 30, interactiveOnly: false))
                .First(n => n.DataContextType == nameof(ImmediateToolViewModel)).Path;
            HexIDE.IDE.ForegroundWindow.Resolve(fresh, host.Title, host, window, host.Title!).Control.Should().NotBeNull();
            HexIDE.IDE.ForegroundWindow.Resolve("", "ide", window, window, window.Title!).Error.Should().NotContain("floating");
            HexIDE.IDE.ForegroundWindow.Resolve("Immediate/x", "ide", window, window, window.Title!).Error.Should().NotContain("floating");
        }
        finally { field.SetValue(app, previous); }
    });

    private static HostWindow FloatingHost(MainViewViewModel vm) =>
        (HostWindow)((IRootDock)vm.Layout).Windows!.Single().Host!;

    private static async Task WithWorkspace(Action<MainViewViewModel, MainViewViewModel.DockFactory, Window, DISetup> check,
        double scaling = 1)
    {
        await using var owned = new RealComposition();
        var vm = owned.Composition.Root;
        var factory = (MainViewViewModel.DockFactory)typeof(MainViewViewModel)
            .GetField("dockFactory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
        var locator = new Avalonia.Controls.Templates.FuncDataTemplate<ImmediateToolViewModel>((_, _) => new ImmediateToolView());
        Application.Current!.DataTemplates.Add(locator);
        var window = new Window { Content = new DockControl { Layout = vm.Layout }, Width = 1000, Height = 700 };
        window.SetRenderScaling(scaling);
        try
        {
            vm.OpenImmediateTool();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            check(vm, factory, window, owned.Composition);
        }
        finally
        {
            foreach (var floating in ((IRootDock)vm.Layout).Windows?.ToArray() ?? [])
                (floating.Host as Window)?.Close();
            window.Close();
            Application.Current.DataTemplates.Remove(locator);
        }
    }

}
