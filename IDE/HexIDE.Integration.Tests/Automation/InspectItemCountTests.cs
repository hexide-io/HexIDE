using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// inspect_element's selectionItems is the selection, and read as the list's items it made a dropdown holding two look
/// like one holding the one selected: a working change to Startup Object read as a failed one (#1263). itemCount says
/// how many items there are, open or closed.
/// </summary>
public class InspectItemCountTests
{
    [AvaloniaFact]
    public void A_dropdown_reports_how_many_items_it_holds_closed_and_open()
    {
        var combo = new ComboBox { ItemsSource = new[] { "Sub Main", "Form1" }, SelectedIndex = 1 };
        var window = new Window { Width = 300, Height = 200, Content = combo };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var closed = UiAutomationDriver.Inspect(combo, "Window/ComboBox", window);
            closed.SelectionItems.Should().Equal("Form1");
            closed.ItemCount.Should().Be(2);

            combo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            var open = UiAutomationDriver.Inspect(combo, "Window/ComboBox", window);
            open.SelectionItems.Should().Equal("Form1");
            open.ItemCount.Should().Be(2);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_control_that_holds_no_items_reports_no_count()
    {
        var button = new Button { Content = "OK" };
        var window = new Window { Content = button };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            UiAutomationDriver.Inspect(button, "Window/Button", window).ItemCount.Should().BeNull();
        }
        finally { window.Close(); }
    }
    // A ListBox's and a TabControl's peers offer no selection provider, so selectionItems was [] on every one, which the
    // description's "what is selected" would read as nothing selected (#1263 review). They are read from the list.
    [AvaloniaFact]
    public void A_list_box_and_a_tab_control_report_what_is_selected_and_how_many_items()
    {
        var list = new ListBox { ItemsSource = new[] { "one", "two", "three" }, SelectedIndex = 1 };
        var tabs = new TabControl { Items = { new TabItem { Header = "General" }, new TabItem { Header = "Make" } }, SelectedIndex = 1 };
        var window = new Window { Width = 300, Height = 300, Content = new StackPanel { Children = { list, tabs } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var l = UiAutomationDriver.Inspect(list, "Window/List", window);
            l.SelectionItems.Should().Equal("two");
            l.ItemCount.Should().Be(3);
            var t = UiAutomationDriver.Inspect(tabs, "Window/Tab", window);
            t.SelectionItems.Should().Equal("Make");
            t.ItemCount.Should().Be(2);
        }
        finally { window.Close(); }
    }

    // A menu counts separators and hidden items a dump does not show, so its count could not be reconciled with the dump.
    [AvaloniaFact]
    public void A_menu_reports_no_count()
    {
        var file = new MenuItem { Header = "File", Items = { new MenuItem { Header = "New" }, new Separator(), new MenuItem { Header = "Exit" } } };
        var window = new Window { Content = new Menu { Items = { file } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            UiAutomationDriver.Inspect(file, "Window/Menu/File", window).ItemCount.Should().BeNull();
        }
        finally { window.Close(); }
    }

    /// <summary>A view model whose ToString is its type name, as ComponentInstanceViewModel's is.</summary>
    public sealed class Component(string name)
    {
        public string Name { get; } = name;
    }

    private static ComboBox TemplatedDropdown(int selected) => new()
    {
        ItemsSource = new[] { new Component("Form1"), new Component("Command1") },
        SelectedIndex = selected,
        ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Component>(
            (c, _) => new TextBlock { Text = c?.Name }),
    };

    [AvaloniaFact]
    public void ReadOnlyValue_TemplatedDropdown_MatchesTheDumpName()
    {
        var combo = TemplatedDropdown(0);
        var window = new Window { Width = 300, Height = 200, Content = combo };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var outcome = UiAutomationDriver.Interact(combo, "set_value", "Command1");
            outcome.Success.Should().BeTrue(outcome.Error);
            UiAutomationDriver.Inspect(combo, "Window/ComboBox", window).SelectionItems.Should().Equal("Command1");
            outcome.Detail.Should().Be("selected 'Command1' and closed the dropdown, as a click does");
        }
        finally { window.Close(); }
    }

    // A dropdown whose items are view models shown through a template: the peer names the selection, and gives its value,
    // by the item's ToString, its type name. Read as the dropdown shows it instead, closed and open (#1288).
    [AvaloniaFact]
    public void A_templated_dropdown_s_selection_and_value_are_the_text_it_shows()
    {
        var combo = TemplatedDropdown(1);
        var window = new Window { Width = 300, Height = 200, Content = combo };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var closed = UiAutomationDriver.Inspect(combo, "Window/ComboBox", window);
            combo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            var open = UiAutomationDriver.Inspect(combo, "Window/ComboBox", window);

            closed.SelectionItems.Should().Equal("Command1");
            closed.Value.Should().Be("Command1");
            open.SelectionItems.Should().Equal("Command1");
            open.Value.Should().Be("Command1");
        }
        finally { window.Close(); }
    }

    // A string item, or one with a ToString of its own, is named by that text, kept whatever the template shows: only text
    // the item gives without meaning to, its type or a record's members, is replaced by what the box shows.
    [AvaloniaFact]
    public void A_dropdown_of_text_keeps_its_items_own_text_whatever_its_template_shows()
    {
        var combo = new ComboBox
        {
            ItemsSource = new[] { "Sub Main", "Form1" },
            SelectedIndex = 1,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((s, _) => new TextBlock { Text = $"Startup: {s}" }),
        };
        var window = new Window { Width = 300, Height = 200, Content = combo };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var closed = UiAutomationDriver.Inspect(combo, "Window/ComboBox", window);
            closed.SelectionItems.Should().Equal("Form1");
        }
        finally { window.Close(); }
    }

    public sealed record Language(string Id, string DisplayName);

    public sealed class Generic<T>(string name)
    {
        public string Name { get; } = name;
    }

    // A record names itself by its members, and a generic view model by a type name with its arguments: neither is a type
    // name the old check knew, and both are what the box shows instead (#1288 review). A block the template hides is not
    // read.
    [AvaloniaFact]
    public void A_record_s_or_a_generic_s_own_text_is_read_as_the_box_shows_it()
    {
        var records = new ComboBox
        {
            ItemsSource = new[] { new Language("en", "English"), new Language("fr", "Français") },
            SelectedIndex = 1,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Language>((l, _) => new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = l?.DisplayName },
                    new Border { IsVisible = false, Child = new TextBlock { Text = l?.Id } },
                },
            }),
        };
        var generics = new ComboBox
        {
            ItemsSource = new[] { new Generic<string>("Form1"), new Generic<string>("Command1") },
            SelectedIndex = 1,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Generic<string>>((g, _) => new TextBlock { Text = g?.Name }),
        };
        var window = new Window { Width = 300, Height = 200, Content = new StackPanel { Children = { records, generics } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var record = UiAutomationDriver.Inspect(records, "Window/ComboBox", window);
            record.SelectionItems.Should().Equal("Français");
            record.Value.Should().Be("Français");
            UiAutomationDriver.Inspect(generics, "Window/ComboBox", window).SelectionItems.Should().Equal("Command1");
        }
        finally { window.Close(); }
    }

    private const string TwoButtons = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.CommandButton Command1
           End
           Begin VB.CommandButton Command2
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private sealed class NullSink : HexIDE.Runtime.Serialization.IDeserializeErrorSink { public void LogError(string _) { } }

    // The Properties window's object selector, as the IDE builds it: its template shows a control's name and class, so the
    // box's text is "Command1 CommandButton", which names no control. Its items name themselves, closed and open, in the
    // dump's rows, and for select by value (#1288 review).
    [AvaloniaFact]
    public void The_object_selector_names_its_controls_closed_open_and_for_select()
    {
        var project = new HexIDE.Runtime.ProjectElements.ProjectDefinition(HexIDE.Runtime.ProjectElements.VBProjectType.EXE, "P");
        var form = new HexIDE.Runtime.Serialization.FormDeserializer().Deserialize(project, TwoButtons, new NullSink())!;
        project.AddForm(form);
        var vm = new HexIDE.VisualDesigner.FormEditViewModel(null!, NSubstitute.Substitute.For<HexIDE.IDE.IEventBus>(),
            NSubstitute.Substitute.For<HexIDE.Projects.IProjectService>(), null!, null!, null!,
            NSubstitute.Substitute.For<HexIDE.Localization.ILocalizationService>()).Initialize(form);
        var view = new HexIDE.VisualDesigner.Views.PropertiesToolView();
        var window = new Window { Width = 400, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var combo = view.FindControl<ComboBox>("ObjectSelector")!;
            combo.ItemsSource = vm.AllComponents;
            combo.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();

            UiAutomationDriver.Inspect(combo, "Window/ObjectSelector", window).SelectionItems.Should().Equal("Command1");
            combo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            UiAutomationDriver.Inspect(combo, "Window/ObjectSelector", window).Value.Should().Be("Command1");
            UiAutomationDriver.Dump(combo.ContainerFromIndex(2)!, "Row", 0, false).Name.Should().Be("Command2");

            var selected = UiAutomationDriver.Interact(combo, "select", "Command2");
            selected.Success.Should().BeTrue(selected.Error);
            UiAutomationDriver.Inspect(combo, "Window/ObjectSelector", window).SelectionItems.Should().Equal("Command2");
        }
        finally { window.Close(); }
    }

    private const string AnOptionArray = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.OptionButton optSize
              Index           =   0
           End
           Begin VB.OptionButton optSize
              Index           =   1
           End
        End
        Attribute VB_Name = "Form1"
        """;

    // A control array's elements share a name, so the selector read "optSize" for each, and a select by the name(index) the
    // write tools take matched nothing (#1320). Each element names itself as those tools address it.
    [AvaloniaFact]
    public void The_object_selector_names_a_control_array_element_with_its_index()
    {
        var project = new HexIDE.Runtime.ProjectElements.ProjectDefinition(HexIDE.Runtime.ProjectElements.VBProjectType.EXE, "P");
        var form = new HexIDE.Runtime.Serialization.FormDeserializer().Deserialize(project, AnOptionArray, new NullSink())!;
        project.AddForm(form);
        var vm = new HexIDE.VisualDesigner.FormEditViewModel(null!, NSubstitute.Substitute.For<HexIDE.IDE.IEventBus>(),
            NSubstitute.Substitute.For<HexIDE.Projects.IProjectService>(), null!, null!, null!,
            NSubstitute.Substitute.For<HexIDE.Localization.ILocalizationService>()).Initialize(form);
        var view = new HexIDE.VisualDesigner.Views.PropertiesToolView();
        var window = new Window { Width = 400, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var combo = view.FindControl<ComboBox>("ObjectSelector")!;
            combo.ItemsSource = vm.AllComponents;
            combo.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();

            UiAutomationDriver.Inspect(combo, "Window/ObjectSelector", window).SelectionItems.Should().Equal("optSize(1)");
            combo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            UiAutomationDriver.Dump(combo.ContainerFromIndex(1)!, "Row", 0, false).Name.Should().Be("optSize(0)");

            var selected = UiAutomationDriver.Interact(combo, "select", "optSize(0)");
            selected.Success.Should().BeTrue(selected.Error);
            UiAutomationDriver.Inspect(combo, "Window/ObjectSelector", window).SelectionItems.Should().Equal("optSize(0)");
        }
        finally { window.Close(); }
    }
}
