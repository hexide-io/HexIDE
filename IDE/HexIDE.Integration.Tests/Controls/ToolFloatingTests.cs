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
using HexIDE.Localization;
using HexIDE.Automation;

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
            Content = new DockControl { Name = "DockControl", Layout = vm.Layout }, Width = 1000, Height = 700,
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
    [InlineData("DockControl", "Host", "Dock host", "Dock-Host")]
    [InlineData("RootDockControl", "Root", "Root dock", "Stammdock")]
    [InlineData("PinnedDockControl", "PinnedHost", "Pinned dock host", "Host für angeheftete Fenster")]
    [InlineData("ToolPinnedControl", "PinnedToolTabs", "Pinned tool tabs", "Angeheftete Werkzeugregisterkarten")]
    [InlineData("DockCommandBarHost", "CommandBars", "Dock command bars", "Dock-Befehlsleisten")]
    [InlineData("DockSelectorOverlay", "DocumentsSelector", "Documents selector", "Dokumentauswahl")]
    public Task Dock_parts_have_localized_names_in_main_and_floating_windows(
        string className, string key, string english, string german) => WithWorkspace((vm, factory, window, _) =>
    {
        var localization = Substitute.For<ILocalizationService>();
        var resourceKey = "Str.Dock." + key;
        var resources = Application.Current!.Resources;
        var hadResource = resources.TryGetValue(resourceKey, out var original);
        try
        {
            factory.FloatDockable(vm.Immediate);
            Dispatcher.UIThread.RunJobs();
            foreach (var expected in new[] { german, english })
            {
                localization.GetString(resourceKey).Returns(expected);
                resources[resourceKey] = localization.GetString(resourceKey);
                Dispatcher.UIThread.RunJobs();
                foreach (var host in new Window[] { FloatingHost(vm), window })
                {
                    var parts = host.GetVisualDescendants().OfType<Control>()
                        .Where(c => c.GetType().Name == className).ToArray();
                    parts.Should().HaveCount(className == "ToolPinnedControl" ? 4 : 1);
                    foreach (var part in parts)
                        ControlAutomationPeer.CreatePeerForElement(part)!.GetName().Should().Be(expected);

                    var nodes = Flatten(UiAutomationDriver.Dump(host, "Window", 40, false))
                        .Where(n => n.ClassName == className).ToArray();
                    nodes.Should().HaveCount(parts.Length);
                    foreach (var node in nodes)
                    {
                        (node.Label ?? node.Name).Should().Be(expected);
                        var segment = className switch
                        {
                            "DockCommandBarHost" => "ToolBar[PART_CommandBarHost]",
                            "DockSelectorOverlay" => "List[PART_SelectorOverlay]",
                            "ToolPinnedControl" => $"Tab[#{Array.IndexOf(nodes, node)}]",
                            "DockControl" when host == window => "Pane[DockControl]",
                            _ => $"Pane[{english}]",
                        };
                        node.Path.Should().EndWith("/" + segment);
                        UiAutomationDriver.Resolve(host, node.Path).control.Should().NotBeNull();
                    }
                }
            }
        }
        finally
        {
            if (hadResource) resources[resourceKey] = original;
            else resources.Remove(resourceKey);
        }
    }, mainView: true);

    [AvaloniaFact]
    public Task Main_view_dock_host_follows_the_active_language() => WithWorkspace((_, _, window, _) =>
    {
        var dock = window.GetVisualDescendants().OfType<DockControl>().Single();
        window.Resources["Str.Dock.Host"] = "Dock-Host";
        Dispatcher.UIThread.RunJobs();
        ControlAutomationPeer.CreatePeerForElement(dock)!.GetName().Should().Be("Dock-Host");
        window.Resources["Str.Dock.Host"] = "Dock host";
        Dispatcher.UIThread.RunJobs();
        ControlAutomationPeer.CreatePeerForElement(dock)!.GetName().Should().Be("Dock host");
    }, mainView: true);

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task German_dock_paths_keep_the_English_segments(bool floating) => WithWorkspace((vm, factory, window, _) =>
    {
        window.Resources["Str.Dock.Root"] = "Stammdock";
        window.Resources["Str.Dock.PinnedHost"] = "Host für angeheftete Fenster";
        Window host = window;
        if (floating)
        {
            factory.FloatDockable(vm.Immediate);
            Dispatcher.UIThread.RunJobs();
            host = FloatingHost(vm);
        }
        host.Resources["Str.Dock.Host"] = "Dock-Host";
        host.Resources["Str.Dock.Root"] = "Stammdock";
        host.Resources["Str.Dock.PinnedHost"] = "Host für angeheftete Fenster";
        Dispatcher.UIThread.RunJobs();
        var dock = host.GetVisualDescendants().OfType<DockControl>().Single();
        var root = dock.GetVisualDescendants().OfType<RootDockControl>().Single();
        var pinned = root.GetVisualDescendants().OfType<PinnedDockControl>().Single();
        var dockPath = UiAutomationDriver.PathWithin(host, "Window", dock)!;
        dockPath.Should().EndWith(floating ? "/Pane[Dock host]" : "/Pane[DockControl]");
        var rootPath = dockPath + "/Pane[Root dock]";
        var resolved = UiAutomationDriver.Resolve(host, rootPath);
        resolved.error.Should().BeNull();
        resolved.control.Should().BeSameAs(root);
        var pinnedPath = rootPath + "/Pane[Pinned dock host]";
        UiAutomationDriver.Resolve(host, pinnedPath).control.Should().BeSameAs(pinned);
        ControlAutomationPeer.CreatePeerForElement(root)!.GetName().Should().Be("Stammdock");
        ControlAutomationPeer.CreatePeerForElement(pinned)!.GetName().Should().Be("Host für angeheftete Fenster");
    }, mainView: true);

    private static System.Collections.Generic.IEnumerable<UiNode> Flatten(UiNode node) =>
        node.Children.SelectMany(Flatten).Prepend(node);

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

    [AvaloniaFact]
    public Task Floating_tool_key_survives_German_and_lists_its_title() => WithWorkspace((vm, factory, window, composition) =>
    {
        var strings = composition.LocalizationService;
        var original = strings.ActiveLanguage;
        try
        {
            strings.Apply("de");
            factory.FloatDockable(vm.Immediate);
            Dispatcher.UIThread.RunJobs();
            var host = FloatingHost(vm);
            HexIDE.IDE.ForegroundWindow.Pick("immediate", window, [window, host]).Window.Should().BeSameAs(host);
            HexIDE.IDE.ForegroundWindow.Pick("Nope", window, [window, host]).Error.Should()
                .Contain("tool keys: immediate").And.Contain(host.Title!);
        }
        finally { strings.Apply(original); }
    });

    [AvaloniaFact]
    public Task Unrelated_floating_tool_is_not_blamed_for_a_German_path_failure() => WithWorkspace((vm, factory, window, composition) =>
    {
        factory.FloatDockable(vm.Immediate);
        Dispatcher.UIThread.RunJobs();
        var host = FloatingHost(vm);
        var app = Application.Current!;
        var field = typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(Avalonia.Controls.ApplicationLifetimes.IApplicationLifetime));
        var previous = field.GetValue(app);
        var language = composition.LocalizationService.ActiveLanguage;
        var lifetime = Substitute.For<Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime>();
        lifetime.MainWindow.Returns(window);
        lifetime.Windows.Returns(new[] { window, host });
        try
        {
            composition.LocalizationService.Apply("de");
            field.SetValue(app, lifetime);
            var result = HexIDE.IDE.ForegroundWindow.Resolve("Window/TitleBar[Locals]", "ide", window, window, window.Title!);
            result.Error.Should().NotContain("Tools are floating").And.Contain("Names follow the IDE language (de)")
                .And.Contain("Tool.locals").And.Contain("Tool.properties").And.Contain("Tool.project");
        }
        finally { field.SetValue(app, previous); composition.LocalizationService.Apply(language); }
    });

    private static HostWindow FloatingHost(MainViewViewModel vm) =>
        (HostWindow)((IRootDock)vm.Layout).Windows!.Single().Host!;

    private static async Task WithWorkspace(Action<MainViewViewModel, MainViewViewModel.DockFactory, Window, DISetup> check,
        double scaling = 1, bool mainView = false)
    {
        await using var owned = new RealComposition();
        var vm = owned.Composition.Root;
        var factory = (MainViewViewModel.DockFactory)typeof(MainViewViewModel)
            .GetField("dockFactory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
        var locator = new Avalonia.Controls.Templates.FuncDataTemplate<ImmediateToolViewModel>((_, _) => new ImmediateToolView());
        Application.Current!.DataTemplates.Add(locator);
        var window = new Window
        {
            Content = mainView ? new MainView { DataContext = vm } : new DockControl { Name = "DockControl", Layout = vm.Layout },
            Width = 1000, Height = 700,
        };
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
