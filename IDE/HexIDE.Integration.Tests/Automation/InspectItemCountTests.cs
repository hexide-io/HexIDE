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
            System.IO.File.WriteAllText("C:/Work/.hexide-sessions/err.txt",
                $"closed sel=[{string.Join("|", closed.SelectionItems)}] value={closed.Value}\n");
            combo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            var open = UiAutomationDriver.Inspect(combo, "Window/ComboBox", window);
            System.IO.File.AppendAllText("C:/Work/.hexide-sessions/err.txt",
                $"open sel=[{string.Join("|", open.SelectionItems)}] value={open.Value}\n");

            closed.SelectionItems.Should().Equal("Command1");
            closed.Value.Should().Be("Command1");
            open.SelectionItems.Should().Equal("Command1");
            open.Value.Should().Be("Command1");
        }
        finally { window.Close(); }
    }

    // A selection the peer names by its own text keeps that name, the one its row carries, though the template shows more.
    [AvaloniaFact]
    public void A_dropdown_of_text_keeps_its_items_names_whatever_its_template_shows()
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
}
