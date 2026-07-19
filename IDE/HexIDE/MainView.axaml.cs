using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Labs.Input;
using HexIDE.Addins;
using HexIDE.Utils;

namespace HexIDE;

public partial class MainView : UserControl
{
    private readonly ObservableCollection<object> _toolsAddinItems = [];
    private Separator? _toolsAddinSeparator;

    public MainView()
    {
        InitializeComponent();
    }

    private void AvaloniaOnWeb(object? sender, ExecutedRoutedEventArgs e)
    {
        TopLevel.GetTopLevel(this)!.Launcher.LaunchUriAsync(new Uri("https://avaloniaui.net"));
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
