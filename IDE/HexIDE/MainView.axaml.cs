using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Labs.Input;
using HexIDE.Addins;
using HexIDE.Projects;
using HexIDE.Utils;

namespace HexIDE;

public partial class MainView : UserControl
{
    private readonly ObservableCollection<MenuItem> _projectItems = [];
    private readonly ObservableCollection<object> _toolsAddinItems = [];
    private Separator? _toolsAddinSeparator;

    public MainView()
    {
        InitializeComponent();
        // The presenter is created before Opening; keep its source live while the handler fills it.
        ((MenuFlyout)AddProjectButton.Flyout!).ItemsSource = _projectItems;
#if DEBUG
        // The automation server's section (#2451), left of Ln/Col. Added here rather than in the markup so that it compiles
        // out with the server, and a distributed build carries no hidden section.
        var automation = new HexIDE.Automation.AutomationStatusSection(HexIDE.Automation.AutomationServerStatus.Current);
        DockPanel.SetDock(automation, Avalonia.Controls.Dock.Right);
        StatusBarPanel.Children.Insert(2, automation);
#endif
    }

    private void ProjectFlyoutOpening(object? sender, EventArgs e)
    {
        if (sender is not MenuFlyout flyout || flyout.Target?.DataContext is not MainViewViewModel vm)
            return;

        _projectItems.Clear();
        foreach (var template in vm.Profile.AvailableProjectTypes)
        {
            var item = new MenuItem
            {
                Header = template.Name,
                Icon = new Image { Source = template.Icon, Width = 16, Height = 16 },
                Command = ApplicationCommands.AddProjectCommand,
                CommandParameter = template,
                IsEnabled = template.Supported,
            };
            if (template.NameKey is { } key)
                item.Bind(HeaderedSelectingItemsControl.HeaderProperty, this.GetResourceObservable(key));
            _projectItems.Add(item);
        }
        if (_projectItems.Count == 0)
        {
            var item = new MenuItem { IsEnabled = false };
            item.Bind(HeaderedSelectingItemsControl.HeaderProperty, this.GetResourceObservable("Str.NewProject.NoTemplates"));
            _projectItems.Add(item);
        }
    }

    private void OnPanelLayoutsOpened(object? sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, PanelLayoutsMenuItem) || DataContext is not MainViewViewModel vm) return;
        PanelLayoutsMenuItem.Items.Clear();
        MenuItem Item(string key, Action action)
        {
            var item = new MenuItem { Command = new DelegateCommand(action) };
            item.Bind(MenuItem.HeaderProperty, item.GetResourceObservable(key));
            return item;
        }
        PanelLayoutsMenuItem.Items.Add(Item("Str.Layout.Save", () => vm.SaveNamedLayoutAsync().ListenErrors()));
        PanelLayoutsMenuItem.Items.Add(Item("Str.Layout.FullEditor", vm.FullEditorLayout));
        foreach (var name in vm.NamedLayouts.Names)
        {
            var menu = new MenuItem { Header = new TextBlock { Text = name } };
            Avalonia.Automation.AutomationProperties.SetName(menu, name);
            menu.Items.Add(Item("Str.Layout.Switch", () => vm.SwitchNamedLayout(name)));
            menu.Items.Add(Item("Str.Layout.Rename", () => vm.RenameNamedLayoutAsync(name).ListenErrors()));
            menu.Items.Add(Item("Str.Layout.Delete", () => vm.DeleteNamedLayoutAsync(name).ListenErrors()));
            PanelLayoutsMenuItem.Items.Add(menu);
        }
    }

    private void AvaloniaOnWeb(object? sender, ExecutedRoutedEventArgs e)
    {
        TopLevel.GetTopLevel(this)!.Launcher.LaunchUriAsync(new Uri("https://avaloniaui.net"));
    }

    private void OnDockPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        (DataContext as MainViewViewModel)?.ScheduleLayoutSave();
    }

    // The Run menu's launches are asked whether they run the startup project as it opens, never on a project's load.
    private void OnRunMenuOpened(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender)) (DataContext as MainViewViewModel)?.RunWith?.Refresh();
    }

    public MainView WindowInitialized()
    {
        if (DataContext is MainViewViewModel vm)
        {
            vm.OnInitialized();

            // Catch mouse-driven dock changes (splitter resize, drag-dock) for eager layout persistence;
            // handledEventsToo so a splitter that marks the event handled still schedules a save.
            DockControl.AddHandler(InputElement.PointerReleasedEvent, OnDockPointerReleased,
                RoutingStrategies.Bubble, handledEventsToo: true);

            var service = vm.AddinMenuService;

            service.ToolsItems.CollectionChanged += (_, _) =>
                RebuildToolsSection(service.ToolsItems);

            service.AddInsItems.CollectionChanged += (_, _) =>
                RebuildAddInsMenu(service.AddInsItems);

            // Add-ins load before this subscription is set up, so seed from current state.
            RebuildToolsSection(service.ToolsItems);
            RebuildAddInsMenu(service.AddInsItems);

            var commands = vm.AddinCommandService;
            commands.CommandsChanged += () => RebuildCommandBindings(commands);
            RebuildCommandBindings(commands);
        }
        return this;
    }

    private readonly System.Collections.Generic.List<KeyBinding> _addinKeyBindings = [];

    private void RebuildCommandBindings(AddinCommandService service)
    {
        foreach (var kb in _addinKeyBindings)
            KeyBindings.Remove(kb);
        _addinKeyBindings.Clear();

        foreach (var cmd in service.Commands)
        {
            if (string.IsNullOrWhiteSpace(cmd.Gesture)) continue;

            KeyGesture gesture;
            try { gesture = KeyGesture.Parse(cmd.Gesture); }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Add-in command '{Id}' has an invalid key gesture '{Gesture}'", cmd.Id, cmd.Gesture);
                continue;
            }

            var kb = new KeyBinding
            {
                Gesture = gesture,
                Command = new DelegateCommand(cmd.OnInvoke, cmd.CanExecute),
            };
            _addinKeyBindings.Add(kb);
            KeyBindings.Add(kb);
        }
    }

    private void RebuildToolsSection(ObservableCollection<AddinMenuItemViewModel> items)
    {
        if (_toolsAddinSeparator is not null)
        {
            ToolsMenuItem.Items.Remove(_toolsAddinSeparator);
            _toolsAddinSeparator = null;
        }
        foreach (var item in _toolsAddinItems)
            ToolsMenuItem.Items.Remove(item);
        _toolsAddinItems.Clear();

        if (items.Count == 0)
            return;

        _toolsAddinSeparator = new Separator();
        ToolsMenuItem.Items.Add(_toolsAddinSeparator);
        foreach (var vm in items)
        {
            var mi = BuildMenuItem(vm);
            _toolsAddinItems.Add(mi);
            ToolsMenuItem.Items.Add(mi);
        }
    }

    private void RebuildAddInsMenu(ObservableCollection<AddinMenuItemViewModel> items)
    {
        AddInsMenuItem.Items.Clear();
        AddInsMenuItem.Items.Add(AddInManagerMenuItem);
        if (items.Count > 0)
            AddInsMenuItem.Items.Add(new Separator());
        foreach (var vm in items)
            AddInsMenuItem.Items.Add(BuildMenuItem(vm));
    }

    private static MenuItem BuildMenuItem(AddinMenuItemViewModel vm)
    {
        var mi = new MenuItem { Header = vm.Header };
        if (vm.Command is not null)
            mi.Command = vm.Command;
        foreach (var sub in vm.SubItems)
            mi.Items.Add(BuildMenuItem(sub));
        vm.SubItems.CollectionChanged += (_, _) => RebuildSubItems(mi, vm);
        return mi;
    }

    private static void RebuildSubItems(MenuItem mi, AddinMenuItemViewModel vm)
    {
        mi.Items.Clear();
        foreach (var sub in vm.SubItems)
            mi.Items.Add(BuildMenuItem(sub));
    }
}
