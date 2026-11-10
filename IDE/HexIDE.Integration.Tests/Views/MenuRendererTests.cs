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
using HexIDE.Utils;

namespace HexIDE.Integration.Tests.Views;

public class MenuRendererTests
{
    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Rendered_menu_matches_the_real_axaml_recursively_and_after_updates(string language)
    {
        await using var owned = new RealComposition();
        var vm = owned.Composition.Root;
        var localization = owned.Composition.LocalizationService;
        Window? host = null;
        try
        {
            localization.Apply(language);
            vm.RunWith!.Starts.Add(new RunMenuItemViewModel("Launch once", new DelegateCommand(() => { })));
            vm.RunWith.Choices.Add(new RunMenuItemViewModel("Selected launch", new DelegateCommand(() => { }), true));
            var view = new MainView { DataContext = vm };
            var actual = view.GetLogicalDescendants().OfType<Menu>().Single();
            using var renderer = new MenuRenderer(vm);
            host = new Window { Content = new StackPanel { Children = { view, renderer.Menu } }, Width = 900, Height = 600 };
            host.Show();
            var axamlLayouts = view.FindControl<MenuItem>("PanelLayoutsMenuItem")!;
            var renderedLayouts = (MenuItem)((MenuItem)renderer.Menu.Items[9]!).Items[4]!;
            axamlLayouts.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, axamlLayouts));
            renderedLayouts.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, renderedLayouts));
            Compare(actual, renderer.Menu, MainMenuDeclaration.Load());
            renderer.Menu.FlowDirection.Should().Be(localization.FlowDirection);
            ((MenuItem)renderer.Menu.Items[0]!).Header.Should().Be(localization.GetString("Str.Menu.File"));

            vm.RecentProjects.Add(new RecentProjectItemViewModel("Recent _project", new DelegateCommand(() => { })));
            vm.ClosedDocumentMenuItems.Add(new RecentProjectItemViewModel("Closed", new DelegateCommand(() => { })));
            Compare(actual, renderer.Menu, MainMenuDeclaration.Load());
            vm.RecentProjects.Clear();
            Compare(actual, renderer.Menu, MainMenuDeclaration.Load());
            localization.Apply(language == "en" ? "ar" : "en");
            Compare(actual, renderer.Menu, MainMenuDeclaration.Load());
        }
        finally { host?.Close(); localization.Apply("en"); }
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

    private static void Compare(ItemsControl expected, ItemsControl actual, MenuDeclaration[]? declarations = null)
    {
        actual.Items.Count.Should().Be(expected.Items.Count);
        for (var index = 0; index < expected.Items.Count; index++)
        {
            var left = expected.Items[index].Should().BeAssignableTo<Control>().Subject;
            var right = actual.Items[index].Should().BeAssignableTo<Control>().Subject;
            var declaration = declarations?[index];
            var because = declaration?.Id ?? $"provider child {index}";
            right.GetType().Should().Be(left.GetType(), because);
            right.IsVisible.Should().Be(left.IsVisible, because);
            var expectedBinding = BindingOperations.GetBindingExpressionBase(left, Visual.IsVisibleProperty) as UntypedBindingExpressionBase;
            var actualBinding = BindingOperations.GetBindingExpressionBase(right, Visual.IsVisibleProperty) as UntypedBindingExpressionBase;
            BindingPath(actualBinding).Should().Be(BindingPath(expectedBinding), because);
            if (left is not MenuItem a || right is not MenuItem b) continue;
            b.Header.Should().NotBeNull(because);
            if (a.Header is TextBlock text) ((TextBlock)b.Header!).Text.Should().Be(text.Text, because);
            else b.Header.Should().Be(a.Header, because);
            (b.InputGesture?.ToString()).Should().Be(a.InputGesture?.ToString(), because);
            b.ToggleType.Should().Be(a.ToggleType, because);
            b.IsChecked.Should().Be(a.IsChecked, because);
            b.IsEnabled.Should().Be(a.IsEnabled, because);
            b.CommandParameter.Should().BeEquivalentTo(a.CommandParameter, because);
            if (declaration is not null)
                foreach (var property in new AvaloniaProperty[] { MenuItem.CommandProperty, MenuItem.IsCheckedProperty, ItemsControl.ItemsSourceProperty }
                    .Concat(declaration.LabelBinding is null ? [] : new AvaloniaProperty[] { MenuItem.HeaderProperty }))
                    BindingPath(BindingOperations.GetBindingExpressionBase(b, property) as UntypedBindingExpressionBase)
                        .Should().Be(BindingPath(BindingOperations.GetBindingExpressionBase(a, property) as UntypedBindingExpressionBase), because);
            if (declaration?.Command is { } command && MenuHostRegistry.Commands[command].ShellMember is { } member
                && typeof(MainViewViewModel).GetMember(member).Single() is MethodInfo)
            {
                b.Command.Should().NotBeNull(because);
                b.Command!.CanExecute(null).Should().Be(a.Command!.CanExecute(null), because);
            }
            else if (declarations is null && a.Command is not null)
                b.Command!.CanExecute(null).Should().Be(a.Command.CanExecute(null), because);
            else b.Command.Should().BeSameAs(a.Command, because);
            if (a.ItemsSource is not null)
            {
                b.ItemsSource.Should().BeSameAs(a.ItemsSource, because);
                // Realize the same provider templates, including the radio state of Run With.
                a.ApplyTemplate();
                b.ApplyTemplate();
                foreach (var data in a.ItemsSource)
                {
                    var ca = new MenuItem { DataContext = data, Theme = a.ItemContainerTheme };
                    var cb = new MenuItem { DataContext = data, Theme = b.ItemContainerTheme };
                    var host = new Window { Content = new StackPanel { Children = { ca, cb } } };
                    try
                    {
                        host.Show();
                        cb.Header.Should().Be(ca.Header, because);
                        cb.Header.Should().NotBeNull(because);
                        cb.Command.Should().BeSameAs(ca.Command, because);
                        cb.Command.Should().NotBeNull(because);
                        cb.ToggleType.Should().Be(ca.ToggleType, because);
                        cb.IsChecked.Should().Be(ca.IsChecked, because);
                    }
                    finally { host.Close(); }
                }
            }
            else Compare(a, b, declaration?.Children);
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
        var view = new MainView { DataContext = vm };
        using var renderer = new MenuRenderer(vm);
        owned.Composition.LocalizationService.Apply("en");
        var host = new Window { Content = new StackPanel { Children = { view, renderer.Menu } } };
        try
        {
            host.Show();
            var menus = renderer.Menu.Items.Cast<MenuItem>().ToArray();
            var refreshes = 0;
            vm.RunWith!.PropertyChanged += (_, _) => refreshes++;
            ((MenuItem)menus[6].Items[3]!).RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
            refreshes.Should().Be(0);
            menus[6].RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, menus[6]));
            refreshes.Should().BeGreaterThan(0);
            var window = menus[9];
            var layouts = (MenuItem)window.Items[4]!;
            var axamlLayouts = view.FindControl<MenuItem>("PanelLayoutsMenuItem")!;
            layouts.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, layouts));
            axamlLayouts.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, axamlLayouts));
            Compare(axamlLayouts, layouts);
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
