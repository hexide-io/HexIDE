using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Styling;
using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using HexIDE.Utils;

namespace HexIDE.Menus;

internal sealed class MenuRenderer : IDisposable
{
    private readonly List<Action> unsubscribe = [];
    internal Dictionary<string, MenuItem> NamedItems { get; } = new(StringComparer.Ordinal);
    internal Menu Menu { get; } = new() { Margin = new Thickness(0, 1, 0, 2) };

    internal static readonly IReadOnlyDictionary<string, Func<CompiledBinding>> VisibilityBindings =
        new Dictionary<string, Func<CompiledBinding>>(StringComparer.Ordinal)
        {
            ["{CompiledBinding HasRecentProjects}"] = () => Bind(vm => vm.HasRecentProjects),
            ["{CompiledBinding Profile.IsProjectReferencesVisible}"] = () => Bind(vm => vm.Profile.IsProjectReferencesVisible),
            ["{CompiledBinding Profile.IsToolsReferencesVisible}"] = () => Bind(vm => vm.Profile.IsToolsReferencesVisible),
            ["{CompiledBinding RunWith.HasLaunches, FallbackValue=False}"] = () => Bind(vm => vm.RunWith!.HasLaunches, false),
            ["{CompiledBinding RunWith.HasChoices, FallbackValue=False}"] = () => Bind(vm => vm.RunWith!.HasChoices, false),
            ["{x:Static vb:Static.IsBrowser}"] = () => new CompiledBinding { Source = Static.IsBrowser },
        };

    private static CompiledBinding Bind<T>(Expression<Func<MainViewViewModel, T>> path, object? fallback = null) =>
        CompiledBinding.Create(path, fallbackValue: fallback ?? AvaloniaProperty.UnsetValue);

    internal MenuRenderer(MainViewViewModel vm, IEnumerable<MenuDeclaration>? declaration = null)
    {
        var items = (declaration ?? MainMenuDeclaration.Load()).ToArray();
        MainMenuDeclaration.Validate(items, "main-menu renderer");
        RuntimeHelpers.RunClassConstructor(typeof(MenuUtils).TypeHandle);
        AutomationProperties.SetAutomationId(Menu, "MenuBar");
        Menu.DataContext = vm;
        Menu.Bind(Visual.FlowDirectionProperty, Menu.GetResourceObservable(LocalizationService.FlowDirectionKey));
        foreach (var item in items) Menu.Items.Add(Build(item, vm));
        var toolsIndex = Array.FindIndex(items, i => i.Id == "tools");
        if (toolsIndex < 0) return;
        var tools = (MenuItem)Menu.Items[toolsIndex]!;
        var count = tools.Items.Count;
        void RebuildTools()
        {
            while (tools.Items.Count > count) tools.Items.RemoveAt(tools.Items.Count - 1);
            if (vm.AddinMenuService.ToolsItems.Count > 0) tools.Items.Add(new Separator());
            foreach (var contribution in vm.AddinMenuService.ToolsItems)
                tools.Items.Add(MainView.BuildMenuItem(contribution));
        }
        Watch(vm.AddinMenuService.ToolsItems, RebuildTools);
        RebuildTools();
    }

    private Control Build(MenuDeclaration declaration, MainViewViewModel vm)
    {
        Control control;
        if (declaration.Kind == MenuItemKind.Separator) control = new Separator();
        else
        {
            var item = new MenuItem();
            var name = declaration.Id switch
            {
                "project.removeformfallback" => "RemoveProjectMemberMenuItem",
                "tools" => "ToolsMenuItem",
                "tools.languageservers" => "LanguageServersMenuItem",
                "tools.protocolinspector" => "ProtocolInspectorMenuItem",
                "addins" => "AddInsMenuItem",
                "addins.manager" => "AddInManagerMenuItem",
                "window.layouts" => "PanelLayoutsMenuItem",
                _ => null
            };
            if (name is not null)
            {
                item.Name = name;
                NamedItems.Add(name, item);
            }
            if (declaration.Id == "run.start") item.Classes.Add("play");
            control = item;
            if (declaration.LabelBinding is { } label)
                item.Bind(MenuItem.HeaderProperty, Label(label));
            else item.Bind(MenuItem.HeaderProperty, item.GetResourceObservable(declaration.LabelKey!));
            if (declaration.IconKey is { } icon)
                item.Bind(MenuUtils.MenuIconProperty, item.GetResourceObservable(icon));
            if (declaration.Command is { } command)
            {
                var registration = MenuHostRegistry.Commands[command];
                if (registration.RoutedCommand is { } routed) item.Command = routed;
                else BindCommand(item, registration.ShellMember!);
                item.CommandParameter = Arguments(declaration.Arguments);
            }
            if (declaration.Kind == MenuItemKind.Placeholder) item.IsEnabled = false;
            if (declaration.State is { } state)
            {
                item.ToggleType = MenuItemToggleType.CheckBox;
                item.Bind(MenuItem.IsCheckedProperty, State(state));
            }
            foreach (var child in declaration.Children ?? []) item.Items.Add(Build(child, vm));
            if (declaration.Provider is { } provider) Populate(item, provider, vm);
            if (declaration.Id == "run")
                item.SubmenuOpened += (_, e) => { if (ReferenceEquals(e.Source, item)) vm.RunWith?.Refresh(); };
        }
        if (declaration.VisibilityBinding is { } hostVisibility)
            control.Bind(Visual.IsVisibleProperty, hostVisibility switch
            {
                nameof(MainViewViewModel.HasBrandHome) => Bind(vm => vm.HasBrandHome),
                nameof(MainViewViewModel.HasBrandSupport) => Bind(vm => vm.HasBrandSupport),
                _ => throw new InvalidOperationException("Unvalidated host visibility.")
            });
        else if (declaration.Visibility is { } visibility)
        {
            if (visibility == "{x:Static vb:Static.IsBrowser}") control.IsVisible = Static.IsBrowser;
            else control.Bind(Visual.IsVisibleProperty, VisibilityBindings[visibility]());
        }
        return control;
    }

    internal static object? Arguments(Dictionary<string, MenuArgument>? arguments) => arguments is null || arguments.Count == 0
        ? null : arguments.ToDictionary(pair => pair.Key, pair => pair.Value.Kind switch
        {
            MenuArgumentKind.String => (object)pair.Value.Value.GetString()!,
            MenuArgumentKind.Boolean => pair.Value.Value.GetBoolean(),
            MenuArgumentKind.Integer => pair.Value.Value.GetInt32(),
            _ => throw new InvalidOperationException("Unvalidated menu argument.")
        }, StringComparer.Ordinal);

    private void Populate(MenuItem item, string provider, MainViewViewModel vm)
    {
        switch (provider)
        {
            case "recent-projects":
                item.Bind(ItemsControl.ItemsSourceProperty, Bind(v => v.RecentProjects));
                item.ItemContainerTheme = RecentTheme();
                break;
            case "closed-documents":
                item.Bind(ItemsControl.ItemsSourceProperty, Bind(v => v.ClosedDocumentMenuItems));
                item.ItemContainerTheme = RecentTheme();
                break;
            case "run-starts":
            case "run-choices":
                item.Bind(ItemsControl.ItemsSourceProperty, provider == "run-starts"
                    ? Bind(v => v.RunWith!.Starts) : Bind(v => v.RunWith!.Choices));
                var theme = Theme();
                theme.Setters.Add(new Setter(MenuItem.HeaderProperty, CompiledBinding.Create((RunMenuItemViewModel v) => v.Header)));
                theme.Setters.Add(new Setter(MenuItem.CommandProperty, CompiledBinding.Create((RunMenuItemViewModel v) => v.Command)));
                if (provider == "run-choices")
                {
                    theme.Setters.Add(new Setter(MenuItem.ToggleTypeProperty, MenuItemToggleType.Radio));
                    theme.Setters.Add(new Setter(MenuItem.IsCheckedProperty,
                        CompiledBinding.Create((RunMenuItemViewModel v) => v.IsChecked, mode: BindingMode.OneWay)));
                }
                item.ItemContainerTheme = theme;
                break;
            case "panel-layouts":
                item.Bind(AutomationProperties.NameProperty, item.GetResourceObservable("Str.Layout.Layouts"));
                item.Items.Add(new Separator());
                item.SubmenuOpened += (_, e) =>
                {
                    if (ReferenceEquals(e.Source, item)) MainView.PopulatePanelLayouts(item, vm);
                };
                break;
            case "add-ins":
                var manager = item.Items.Single();
                void Rebuild()
                {
                    item.Items.Clear();
                    item.Items.Add(manager);
                    if (vm.AddinMenuService.AddInsItems.Count > 0) item.Items.Add(new Separator());
                    foreach (var contribution in vm.AddinMenuService.AddInsItems)
                        item.Items.Add(MainView.BuildMenuItem(contribution));
                }
                Watch(vm.AddinMenuService.AddInsItems, Rebuild);
                Rebuild();
                break;
        }
    }

    private void Watch(INotifyCollectionChanged collection, Action rebuild)
    {
        NotifyCollectionChangedEventHandler handler = (_, _) => rebuild();
        collection.CollectionChanged += handler;
        unsubscribe.Add(() => collection.CollectionChanged -= handler);
    }

    private static ControlTheme Theme() => new(typeof(MenuItem))
    {
        BasedOn = Application.Current!.FindResource(typeof(MenuItem)) as ControlTheme
    };

    private static ControlTheme RecentTheme()
    {
        var theme = Theme();
        theme.Setters.Add(new Setter(MenuItem.HeaderProperty, CompiledBinding.Create((RecentProjectItemViewModel v) => v.Header)));
        theme.Setters.Add(new Setter(MenuItem.CommandProperty, CompiledBinding.Create((RecentProjectItemViewModel v) => v.Command)));
        return theme;
    }

    public void Dispose()
    {
        foreach (var action in unsubscribe) action();
        unsubscribe.Clear();
    }

    private static CompiledBinding Label(string name) => name switch
    {
        nameof(MainViewViewModel.BrandHomeMenuHeader) => Bind(vm => vm.BrandHomeMenuHeader),
        nameof(MainViewViewModel.BrandSupportMenuHeader) => Bind(vm => vm.BrandSupportMenuHeader),
        nameof(MainViewViewModel.RemoveProjectMenuHeader) => Bind(vm => vm.RemoveProjectMenuHeader),
        nameof(MainViewViewModel.SaveProjectMenuHeader) => Bind(vm => vm.SaveProjectMenuHeader),
        nameof(MainViewViewModel.SaveFormMenuHeader) => Bind(vm => vm.SaveFormMenuHeader),
        nameof(MainViewViewModel.SaveFormAsMenuHeader) => Bind(vm => vm.SaveFormAsMenuHeader),
        nameof(MainViewViewModel.MakeProjectMenuHeader) => Bind(vm => vm.MakeProjectMenuHeader),
        nameof(MainViewViewModel.MakeWithVb6MenuHeader) => Bind(vm => vm.MakeWithVb6MenuHeader),
        nameof(MainViewViewModel.UndoHeader) => Bind(vm => vm.UndoHeader),
        nameof(MainViewViewModel.RedoHeader) => Bind(vm => vm.RedoHeader),
        nameof(MainViewViewModel.RemoveFormMenuHeader) => Bind(vm => vm.RemoveFormMenuHeader),
        nameof(MainViewViewModel.ProjectPropertiesMenuHeader) => Bind(vm => vm.ProjectPropertiesMenuHeader),
        _ => throw new InvalidOperationException("Unvalidated menu label.")
    };

    private static CompiledBinding State(string name) => name switch
    {
        "toolbar.standard.visible" => CompiledBinding.Create((MainViewViewModel vm) => vm.Settings.IsStandardToolbarVisible, mode: BindingMode.TwoWay),
        "toolbar.edit.visible" => CompiledBinding.Create((MainViewViewModel vm) => vm.Settings.IsEditToolbarVisible, mode: BindingMode.TwoWay),
        "toolbar.debug.visible" => CompiledBinding.Create((MainViewViewModel vm) => vm.Settings.IsDebugToolbarVisible, mode: BindingMode.TwoWay),
        "toolbar.form-editor.visible" => CompiledBinding.Create((MainViewViewModel vm) => vm.Settings.IsFormEditorToolbarVisible, mode: BindingMode.TwoWay),
        "editor.minimap.visible" => CompiledBinding.Create((MainViewViewModel vm) => vm.Settings.IsMinimapVisible, mode: BindingMode.TwoWay),
        _ => throw new InvalidOperationException("Unvalidated menu state.")
    };

    private static CompiledBinding Method(string name, Action<MainViewViewModel> execute) =>
        new(new CompiledBindingPathBuilder().Command(name, (target, _) => execute((MainViewViewModel)target),
            (_, _) => true, []).Build());

    private static void BindCommand(MenuItem item, string member)
    {
        switch (member)
        {
            case nameof(MainViewViewModel.AddClassModuleCommand): item.Bind(MenuItem.CommandProperty, Bind(v => v.AddClassModuleCommand)); break;
            case nameof(MainViewViewModel.AddFileCommand): item.Bind(MenuItem.CommandProperty, Bind(v => v.AddFileCommand)); break;
            case nameof(MainViewViewModel.AddFormCommand): item.Bind(MenuItem.CommandProperty, Bind(v => v.AddFormCommand)); break;
            case nameof(MainViewViewModel.AddModuleCommand): item.Bind(MenuItem.CommandProperty, Bind(v => v.AddModuleCommand)); break;
            case nameof(MainViewViewModel.AddPropertyPageCommand): item.Bind(MenuItem.CommandProperty, Bind(v => v.AddPropertyPageCommand)); break;
            case nameof(MainViewViewModel.AddUserControlCommand): item.Bind(MenuItem.CommandProperty, Bind(v => v.AddUserControlCommand)); break;
            case nameof(MainViewViewModel.OpenAddInManager): item.Bind(MenuItem.CommandProperty, Method(member, vm => vm.OpenAddInManager())); break;
            case nameof(MainViewViewModel.OpenDiagnosticsCommand): item.Bind(MenuItem.CommandProperty, Bind(v => v.OpenDiagnosticsCommand)); break;
            case nameof(MainViewViewModel.OpenLogFolderCommand): item.Bind(MenuItem.CommandProperty, Bind(v => v.OpenLogFolderCommand)); break;
            case nameof(MainViewViewModel.OpenBrandHomeCommand): item.Bind(MenuItem.CommandProperty, Bind(v => v.OpenBrandHomeCommand)); break;
            case nameof(MainViewViewModel.OpenBrandSupportCommand): item.Bind(MenuItem.CommandProperty, Bind(v => v.OpenBrandSupportCommand)); break;
            case nameof(MainViewViewModel.OpenGithubRepo): item.Bind(MenuItem.CommandProperty, Method(member, vm => vm.OpenGithubRepo().ListenErrors())); break;
            case nameof(MainViewViewModel.OpenLanguageServersCommand): item.Bind(MenuItem.CommandProperty, Bind(v => v.OpenLanguageServersCommand)); break;
            case nameof(MainViewViewModel.OpenOutlineCommand): item.Bind(MenuItem.CommandProperty, Bind(v => v.OpenOutlineCommand)); break;
            case nameof(MainViewViewModel.OpenProtocolInspectorCommand): item.Bind(MenuItem.CommandProperty, Bind(v => v.OpenProtocolInspectorCommand)); break;
            case nameof(MainViewViewModel.OpenRecentEditsTool): item.Bind(MenuItem.CommandProperty, Method(member, vm => vm.OpenRecentEditsTool())); break;
            case nameof(MainViewViewModel.RecoverUnsavedWorkCommand): item.Bind(MenuItem.CommandProperty, Bind(v => v.RecoverUnsavedWorkCommand)); break;
            case nameof(MainViewViewModel.RemoveDocumentCommand): item.Bind(MenuItem.CommandProperty, Bind(v => v.RemoveDocumentCommand)); break;
            case nameof(MainViewViewModel.StartExternalDebugCommand): item.Bind(MenuItem.CommandProperty, Bind(v => v.StartExternalDebugCommand)); break;
            default: throw new InvalidOperationException("Unvalidated shell command.");
        }
    }
}
