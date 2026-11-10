using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Linq;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using TextEditor = AvaloniaEdit.TextEditor;
using Dock.Model.Core;
using HexIDE.Forms.Views;
using HexIDE.Forms.ViewModels;
using HexIDE.VisualDesigner;
using HexIDE.Tools;
using HexIDE.Tools.ObjectBrowser;
using HexIDE.VisualDesigner.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Labs.Input;
using HexIDE.Addins;
using HexIDE.Projects;
using HexIDE.Menus;
using HexIDE.Utils;

namespace HexIDE;

public partial class MainView : UserControl
{
    private readonly ObservableCollection<MenuItem> _projectItems = [];
    private MenuRenderer? _menuRenderer;

    public MainView()
    {
        // Register shortcut tracking before markup or the renderer assigns its first command.
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(MenuUtils).TypeHandle);
        InitializeComponent();
        var viewNames = this.GetLogicalDescendants().OfType<Control>().Prepend(this)
            .Where(control => control.Name is not null && ReferenceEquals(this.FindNameScope()!.Find(control.Name), control))
            .ToArray();
        DataContextChanged += (_, _) =>
        {
            _menuRenderer?.Dispose();
            _menuRenderer = DataContext is MainViewViewModel menuVm ? new MenuRenderer(menuVm) : null;
            var names = new NameScope();
            foreach (var control in viewNames) names.Register(control.Name!, control);
            if (_menuRenderer is { } renderer)
                foreach (var (name, item) in renderer.NamedItems) names.Register(name, item);
            names.Complete();
            NameScope.SetNameScope(this, names);
            MainMenuHost.Child = _menuRenderer?.Menu;
            ++_focusRequest;
            _pendingFocus = null;
            if (_focusOwner != null) _focusOwner.KeyboardFocusRequested -= FocusDockable;
            _focusOwner = DataContext as MainViewViewModel;
            if (_focusOwner != null) _focusOwner.KeyboardFocusRequested += FocusDockable;
        };
        LayoutUpdated += (_, _) => TryFocusDockable();
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

    private MainViewViewModel? _focusOwner;
    private IDockable? _pendingFocus;
    private int _focusRequest;

    private void FocusDockable(IDockable dockable)
    {
        var request = ++_focusRequest;
        _pendingFocus = dockable is CodeEditorViewModel or ImmediateToolViewModel or ProjectToolViewModel
            or PropertiesToolViewModel or ObjectBrowserToolViewModel ? dockable : null;
        // Existing panes need no layout; new content is picked up by LayoutUpdated when Dock creates it.
        Dispatcher.UIThread.Post(() =>
        {
            if (request == _focusRequest) TryFocusDockable();
        }, DispatcherPriority.Background);
    }

    private void TryFocusDockable()
    {
        if (_pendingFocus is not { } dockable) return;
        var view = this.GetVisualDescendants().OfType<UserControl>()
            .FirstOrDefault(c => ReferenceEquals(c.DataContext, dockable) && c.IsEffectivelyVisible
                && c is CodeEditorView or ImmediateToolView or ProjectToolView or PropertiesToolView or ObjectBrowserToolView);
        if (view == null) return;
        var children = view.GetVisualDescendants().OfType<Control>();
        Control? input = view switch
        {
            CodeEditorView code => code.ActiveTextArea,
            ImmediateToolView => children.OfType<TextEditor>().FirstOrDefault()?.TextArea,
            ProjectToolView => children.OfType<TreeViewItem>().FirstOrDefault(c => c.IsSelected && c.IsEffectivelyVisible)
                ?? children.OfType<TreeViewItem>().FirstOrDefault(c => c.IsEffectivelyVisible),
            PropertiesToolView => children.OfType<ListBoxItem>().FirstOrDefault(c => c.IsSelected && c.IsEffectivelyVisible)
                ?? children.OfType<ListBoxItem>().FirstOrDefault(c => c.IsEffectivelyVisible),
            ObjectBrowserToolView => children.OfType<TextBox>().FirstOrDefault(c => c.IsEffectivelyVisible),
            _ => null,
        };
        if (input?.Focus() == true) _pendingFocus = null;
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

    internal static void PopulatePanelLayouts(MenuItem menuItem, MainViewViewModel vm)
    {
        menuItem.Items.Clear();
        MenuItem Item(string key, Action action)
        {
            var item = new MenuItem { Command = new DelegateCommand(action) };
            item.Bind(MenuItem.HeaderProperty, item.GetResourceObservable(key));
            return item;
        }
        menuItem.Items.Add(Item("Str.Layout.Save", () => vm.SaveNamedLayoutAsync().ListenErrors()));
        menuItem.Items.Add(Item("Str.Layout.FullEditor", vm.FullEditorLayout));
        foreach (var name in vm.NamedLayouts.Names)
        {
            var menu = new MenuItem { Header = new TextBlock { Text = name } };
            Avalonia.Automation.AutomationProperties.SetName(menu, name);
            menu.Items.Add(Item("Str.Layout.Switch", () => vm.SwitchNamedLayout(name)));
            menu.Items.Add(Item("Str.Layout.Rename", () => vm.RenameNamedLayoutAsync(name).ListenErrors()));
            menu.Items.Add(Item("Str.Layout.Delete", () => vm.DeleteNamedLayoutAsync(name).ListenErrors()));
            menuItem.Items.Add(menu);
        }
    }

    private void ProductOnWeb(object? sender, ExecutedRoutedEventArgs e)
    {
        (DataContext as MainViewViewModel)?.OpenGithubRepo();
    }

    private void OnDockPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        (DataContext as MainViewViewModel)?.ScheduleLayoutSave();
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

    internal static MenuItem BuildMenuItem(AddinMenuItemViewModel vm)
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
