using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Core;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using HexIDE.Addins;
using HexIDE.Forms.ViewModels;
using HexIDE.Menus;
using HexIDE.Localization;
using HexIDE.Automation;
using HexIDE.Utils;

namespace HexIDE.Integration.Tests.Views;

public class MenuRendererTests
{
    [AvaloniaFact]
    public async Task Installed_menu_follows_the_declaration_in_every_shipped_language()
    {
        await using var owned = new RealComposition();
        var vm = owned.Composition.Root;
        var localization = owned.Composition.LocalizationService;
        var view = new MainView { DataContext = vm };
        var menu = view.GetLogicalDescendants().OfType<Menu>().Single();
        var host = new Window { Content = view };
        try
        {
            host.Show();
            foreach (var language in LanguageManifest.Packs)
            {
                localization.Apply(language.Id);
                CheckDeclaration(menu, MainMenuDeclaration.Load(), vm, localization);
                menu.FlowDirection.Should().Be(localization.FlowDirection);
            }
            vm.RecentProjects.Add(new RecentProjectItemViewModel("Recent _project", new DelegateCommand(() => { })));
            vm.ClosedDocumentMenuItems.Add(new RecentProjectItemViewModel("Closed", new DelegateCommand(() => { })));
            ((MenuItem)((MenuItem)menu.Items[0]!).Items[^3]!).ItemsSource.Should().BeSameAs(vm.RecentProjects);
            ((MenuItem)((MenuItem)menu.Items[9]!).Items[1]!).ItemsSource.Should().BeSameAs(vm.ClosedDocumentMenuItems);
        }
        finally { host.Close(); localization.Apply("en"); view.DataContext = null; }
    }

    [AvaloniaFact]
    public async Task Installed_menu_keeps_named_and_automation_routes_and_releases_the_previous_context()
    {
        await using var owned = new RealComposition();
        var vm = owned.Composition.Root;
        owned.Composition.LocalizationService.Apply("en");
        var view = new MainView { DataContext = vm };
        var menu = view.GetLogicalDescendants().OfType<Menu>().Single();
        var host = new Window { Content = view };
        try
        {
            host.Show();
            var named = new Dictionary<string, string>
            {
                ["RemoveProjectMemberMenuItem"] = "Project/Remove",
                ["LanguageServersMenuItem"] = "Tools/Language Servers",
                ["ProtocolInspectorMenuItem"] = "Tools/Protocol Inspector",
                ["AddInManagerMenuItem"] = "Add-Ins/Add-In Manager",
                ["PanelLayoutsMenuItem"] = "Window/Panel Layouts",
                ["ToolsMenuItem"] = "Tools",
                ["AddInsMenuItem"] = "Add-Ins"
            };
            foreach (var (name, path) in named)
            {
                var result = MenuPath.Resolve(menu.Items, path);
                result.Error.Should().BeNull(path);
                result.Item.Should().BeSameAs(view.FindControl<MenuItem>(name), name);
            }
            var contribution = new AddinMenuItemViewModel("Contribution", () => { }, null);
            vm.AddinMenuService.ToolsItems.Add(contribution);
            var tools = view.FindControl<MenuItem>("ToolsMenuItem")!;
            var count = tools.Items.Count;
            MenuPath.Resolve(menu.Items, "Tools/Contribution").Command.Should().BeSameAs(contribution.Command);
            view.DataContext = null;
            vm.AddinMenuService.ToolsItems.Clear();
            tools.Items.Count.Should().Be(count, "the discarded renderer unsubscribed");
            view.FindControl<MenuItem>("ToolsMenuItem").Should().BeNull();
            view.DataContext = vm;
            view.FindControl<MenuItem>("ToolsMenuItem").Should().NotBeSameAs(tools);
            view.FindControl<Button>("AddProjectButton").Should().NotBeNull("markup names remain available");
        }
        finally { host.Close(); view.DataContext = null; }
    }

    [AvaloniaFact]
    public async Task Installed_menu_preserves_profile_targets_and_the_first_shortcut()
    {
        await using var owned = new RealComposition();
        var vm = owned.Composition.Root;
        var view = new MainView { DataContext = vm };
        var menu = view.GetLogicalDescendants().OfType<Menu>().Single();
        ((MenuItem)((MenuItem)menu.Items[0]!).Items[0]!).InputGesture.Should()
            .Be(ApplicationCommands.NewProjectCommand.Gestures.FirstOrDefault());
        foreach (var (parent, index, path, expected) in new[]
        {
            (3, 10, "Profile.IsProjectReferencesVisible", vm.Profile.IsProjectReferencesVisible),
            (7, 5, "Profile.IsToolsReferencesVisible", vm.Profile.IsToolsReferencesVisible)
        })
        {
            var item = (MenuItem)((MenuItem)menu.Items[parent]!).Items[index]!;
            BindingPath(BindingOperations.GetBindingExpressionBase(item, Visual.IsVisibleProperty)
                as UntypedBindingExpressionBase).Should().Be(path);
            item.IsVisible.Should().Be(expected);
        }
        view.DataContext = null;
    }

    [AvaloniaTheory]
    [InlineData("BrandHomeMenuHeader", "Str.Menu.Help.BrandHome")]
    [InlineData("BrandSupportMenuHeader", "Str.Menu.Help.BrandSupport")]
    public async Task Declared_label_binding_follows_language_changes(string binding, string key)
    {
        await using var owned = new RealComposition();
        var vm = owned.Composition.Root;
        var localization = owned.Composition.LocalizationService;
        try
        {
            using var renderer = new MenuRenderer(vm, [new MenuDeclaration
            {
                Kind = MenuItemKind.Placeholder, Id = "test.label", LabelKey = key, LabelBinding = binding
            }]);
            var item = (MenuItem)renderer.Menu.Items[0]!;
            foreach (var language in new[] { "en", "ar", "en" })
            {
                localization.Apply(language);
                item.Header.Should().Be(vm.GetType().GetProperty(binding)!.GetValue(vm));
                BindingPath(BindingOperations.GetBindingExpressionBase(item, MenuItem.HeaderProperty)
                    as UntypedBindingExpressionBase).Should().Be(binding);
            }
        }
        finally { localization.Apply("en"); }
    }

    [AvaloniaTheory]
    [InlineData("HasBrandHome")]
    [InlineData("HasBrandSupport")]
    public async Task Declared_visibility_binding_reads_the_view_model(string binding)
    {
        await using var owned = new RealComposition();
        var vm = owned.Composition.Root;
        using var renderer = new MenuRenderer(vm, [new MenuDeclaration
        {
            Kind = MenuItemKind.Separator, Id = "test.visibility", VisibilityBinding = binding
        }]);
        var item = (Control)renderer.Menu.Items[0]!;
        item.IsVisible.Should().Be((bool)vm.GetType().GetProperty(binding)!.GetValue(vm)!);
        BindingPath(BindingOperations.GetBindingExpressionBase(item, Visual.IsVisibleProperty)
            as UntypedBindingExpressionBase).Should().Be(binding);
    }

    private static void CheckDeclaration(ItemsControl parent, MenuDeclaration[] declarations,
        MainViewViewModel vm, ILocalizationService localization)
    {
        parent.Items.Count.Should().Be(declarations.Length);
        foreach (var (declaration, index) in declarations.Select((item, index) => (item, index)))
        {
            var control = (Control)parent.Items[index]!;
            var visibility = declaration.VisibilityBinding ?? (declaration.Visibility is { } value
                && value.StartsWith("{CompiledBinding ") ? value[17..].TrimEnd('}').Split(',')[0] : null);
            BindingPath(BindingOperations.GetBindingExpressionBase(control, Visual.IsVisibleProperty)
                as UntypedBindingExpressionBase).Should().Be(visibility, declaration.Id);
            if (declaration.Kind == MenuItemKind.Separator)
            {
                control.Should().BeOfType<Separator>(declaration.Id);
                continue;
            }
            var item = control.Should().BeOfType<MenuItem>().Subject;
            item.Header.Should().Be(declaration.LabelBinding is { } label
                ? vm.GetType().GetProperty(label)!.GetValue(vm) : localization.GetString(declaration.LabelKey!), declaration.Id);
            if (declaration.Command is { } command)
            {
                var registered = MenuHostRegistry.Commands[command];
                if (registered.RoutedCommand is { } commandInstance)
                {
                    item.Command.Should().BeSameAs(commandInstance, declaration.Id);
                    item.InputGesture.Should().Be(commandInstance is Avalonia.Labs.Input.RoutedCommand routed
                        ? routed.Gestures.FirstOrDefault() : null, declaration.Id);
                }
                else
                {
                    item.Command.Should().NotBeNull(declaration.Id);
                    BindingPath(BindingOperations.GetBindingExpressionBase(item, MenuItem.CommandProperty)
                        as UntypedBindingExpressionBase).Should().Be(registered.ShellMember + (typeof(MainViewViewModel).GetMember(registered.ShellMember!).Single() is MethodInfo ? "()" : ""), declaration.Id);
                }
            }
            if (declaration.LabelBinding is { } binding)
                BindingPath(BindingOperations.GetBindingExpressionBase(item, MenuItem.HeaderProperty)
                    as UntypedBindingExpressionBase).Should().Be(binding, declaration.Id);
            item.CommandParameter.Should().BeEquivalentTo(MenuRenderer.Arguments(declaration.Arguments));
            if (declaration.IconKey is not null) item.Icon.Should().NotBeNull(declaration.Id);
            if (declaration.Provider is { } provider && MenuHostRegistry.Providers[provider].StartsWith("{CompiledBinding "))
                BindingPath(BindingOperations.GetBindingExpressionBase(item, ItemsControl.ItemsSourceProperty)
                    as UntypedBindingExpressionBase).Should().Be(MenuHostRegistry.Providers[provider][17..^1], declaration.Id);
            if (declaration.Kind == MenuItemKind.Placeholder) item.IsEnabled.Should().BeFalse(declaration.Id);
            if (declaration.Kind == MenuItemKind.Toggle)
            {
                item.ToggleType.Should().Be(MenuItemToggleType.CheckBox);
                item.IsChecked.Should().Be((bool)vm.Settings.GetType().GetProperty(MenuHostRegistry.States[declaration.State!].SettingsProperty)!.GetValue(vm.Settings)!);
            }
            if (declaration.Provider is null || declaration.Provider == "add-ins")
                CheckDeclaration(item, declaration.Children ?? [], vm, localization);
        }
    }

    [AvaloniaFact]
    public async Task Toggle_bindings_write_settings_and_follow_settings_changes()
    {
        await using var owned = new RealComposition();
        var vm = owned.Composition.Root;
        var declarations = Walk(MainMenuDeclaration.Load()).Where(i => i.Kind == MenuItemKind.Toggle).ToArray();
        using var renderer = new MenuRenderer(vm, declarations);
        foreach (var (declaration, index) in declarations.Select((item, index) => (item, index)))
        {
            var item = (MenuItem)renderer.Menu.Items[index]!;
            var property = vm.Settings.GetType().GetProperty(MenuHostRegistry.States[declaration.State!].SettingsProperty)!;
            var previous = (bool)property.GetValue(vm.Settings)!;
            try
            {
                item.SetCurrentValue(MenuItem.IsCheckedProperty, !previous);
                property.GetValue(vm.Settings).Should().Be(!previous, declaration.Id);
                property.SetValue(vm.Settings, previous);
                item.IsChecked.Should().Be(previous, declaration.Id);
            }
            finally { property.SetValue(vm.Settings, previous); }
        }
    }

    [AvaloniaFact]
    public async Task Providers_refresh_layouts_and_addins_and_unsubscribe_when_disposed()
    {
        await using var owned = new RealComposition();
        var vm = owned.Composition.Root;
        vm.RecentProjects.Add(new RecentProjectItemViewModel("Recent", new DelegateCommand(() => { })));
        vm.ClosedDocumentMenuItems.Add(new RecentProjectItemViewModel("Closed", new DelegateCommand(() => { })));
        vm.RunWith!.Starts.Add(new RunMenuItemViewModel("Launch", new DelegateCommand(() => { })));
        vm.RunWith.Choices.Add(new RunMenuItemViewModel("Choice", new DelegateCommand(() => { }), true));
        using var renderer = new MenuRenderer(vm);
        owned.Composition.LocalizationService.Apply("en");
        var host = new Window { Content = renderer.Menu };
        try
        {
            host.Show();
            var menus = renderer.Menu.Items.Cast<MenuItem>().ToArray();
            foreach (var (parent, index, radio, header) in new[] { (0, 20, false, "Recent"), (9, 1, false, "Closed"), (6, 3, false, "Launch"), (6, 4, true, "Choice") })
            {
                var provider = (MenuItem)menus[parent].Items[index]!;
                var data = provider.ItemsSource!.Cast<object>().Single(item =>
                    Equals(item.GetType().GetProperty("Header")!.GetValue(item), header));
                var child = new MenuItem { DataContext = data, Theme = provider.ItemContainerTheme };
                var popup = new Window { Content = child };
                try
                {
                    popup.Show();
                    child.Header.Should().Be(data.GetType().GetProperty("Header")!.GetValue(data));
                    child.Command.Should().BeSameAs(data.GetType().GetProperty("Command")!.GetValue(data));
                    child.Command.Should().NotBeNull();
                    if (radio)
                    {
                        child.ToggleType.Should().Be(MenuItemToggleType.Radio);
                        child.IsChecked.Should().BeTrue();
                    }
                }
                finally { popup.Close(); }
            }
            var refreshes = 0;
            vm.RunWith!.PropertyChanged += (_, _) => refreshes++;
            ((MenuItem)menus[6].Items[3]!).RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
            refreshes.Should().Be(0);
            menus[6].RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, menus[6]));
            refreshes.Should().BeGreaterThan(0);
            var window = menus[9];
            var layouts = (MenuItem)window.Items[4]!;
            layouts.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, layouts));
            ((MenuItem)layouts.Items[0]!).Header.Should().Be(owned.Composition.LocalizationService.GetString("Str.Layout.Save"));
            ((MenuItem)layouts.Items[1]!).Command.Should().NotBeNull();
            layouts.Items.Count.Should().BeGreaterThanOrEqualTo(2);

            var contribution = new AddinMenuItemViewModel("Contribution", () => { }, null);
            vm.AddinMenuService.AddInsItems.Add(contribution);
            vm.AddinMenuService.ToolsItems.Add(contribution);
            var addins = menus[8];
            addins.Items.Count.Should().Be(3);
            ((MenuItem)addins.Items[2]!).Header.Should().Be("Contribution");
            var tools = menus[7];
            ((MenuItem)tools.Items[^1]!).Header.Should().Be("Contribution");
            renderer.Dispose();
            vm.AddinMenuService.AddInsItems.Clear();
            vm.AddinMenuService.ToolsItems.Clear();
            addins.Items.Count.Should().Be(3);
        }
        finally { host.Close(); }

    }

    [AvaloniaFact]
    public async Task Gestures_follow_keymap_changes_and_placeholders_stay_disabled()
    {
        await using var owned = new RealComposition();
        var declarations = Walk(MainMenuDeclaration.Load()).Where(i => i.Kind is MenuItemKind.Placeholder
            || i.Command == "application.StepOverCommand").ToArray();
        using var renderer = new MenuRenderer(owned.Composition.Root, declarations);
        var service = new HexIDE.Keymaps.KeymapService();
        try
        {
            foreach (var pack in new[] { "VB6", "Default" })
            {
                service.Apply(pack);
                foreach (var (declaration, index) in declarations.Select((item, index) => (item, index)))
                {
                    var item = (MenuItem)renderer.Menu.Items[index]!;
                    if (declaration.Kind == MenuItemKind.Placeholder)
                    {
                        item.IsEnabled.Should().BeFalse();
                        item.Command.Should().BeNull();
                        item.Items.Should().BeEmpty();
                    }
                    else item.InputGesture.Should().Be(ApplicationCommands.StepOverCommand.Gestures.FirstOrDefault());
                }
            }
        }
        finally { service.Apply("Default"); }
    }

    [Theory]
    [InlineData((int)MenuArgumentKind.String, "\"text\"", "text")]
    [InlineData((int)MenuArgumentKind.Boolean, "true", true)]
    [InlineData((int)MenuArgumentKind.Integer, "42", 42)]
    public void Arguments_retain_the_declared_clr_type(int kind, string json, object expected)
    {
        using var value = JsonDocument.Parse(json);
        var arguments = new Dictionary<string, MenuArgument> { ["value"] = new((MenuArgumentKind)kind, value.RootElement.Clone()) };
        var result = MenuRenderer.Arguments(arguments).Should().BeOfType<Dictionary<string, object>>().Subject;
        result["value"].Should().Be(expected);
        result["value"].GetType().Should().Be(expected.GetType());
    }

    // Avalonia exposes the applied expression publicly, but its diagnostic path is protected.
    private static string? BindingPath(UntypedBindingExpressionBase? expression) => expression is null ? null
        : (string?)typeof(UntypedBindingExpressionBase).GetProperty("Description",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(expression);

    private static IEnumerable<MenuDeclaration> Walk(IEnumerable<MenuDeclaration> items) =>
        items.SelectMany(i => new[] { i }.Concat(Walk(i.Children ?? [])));
}
