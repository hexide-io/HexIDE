using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.Tools.ObjectBrowser;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// A list row whose item has no ToString of its own: its peer names it by the item's type, which the dump drops, so the
/// row had no name, select by value could not find it, and inspect_element named the selection "ListItem" (#1299).
/// </summary>
public class ListRowNameTests
{
    private static IEnumerable<UiNode> Flatten(UiNode node) => node.Children.SelectMany(Flatten).Prepend(node);

    private static (Window Window, ListBox List) Show<T>(IReadOnlyList<T> items, Func<T?, string> glyph, Func<T?, string> name)
    {
        var list = new ListBox
        {
            ItemsSource = items,
            // As the Object Browser's lists show a row: a glyph, then the name.
            ItemTemplate = new FuncDataTemplate<T>((item, _) => new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { new TextBlock { Text = glyph(item) }, new TextBlock { Text = name(item) } },
            }),
        };
        var window = new Window { Width = 300, Height = 300, Content = list };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, list);
    }

    // The Object Browser's own rows name themselves, so a caller selects a class or a member by its name.
    [AvaloniaFact]
    public void The_Object_Browser_s_rows_are_named_by_their_names()
    {
        var classes = new[] { new OBClassViewModel("Form1", OBClassKind.Form, "Project1"), new OBClassViewModel("Module1", OBClassKind.Module, "Project1") };
        var (window, list) = Show(classes, c => c?.KindGlyph ?? "", c => c?.Name ?? "");
        try
        {
            Flatten(UiAutomationDriver.Dump(window, "Window", 12, false)).Where(n => n.ControlType == "ListItem")
                .Select(n => n.Name).Should().Equal("Form1", "Module1");

            var selected = UiAutomationDriver.Interact(list, "select", "Module1");
            selected.Success.Should().BeTrue(selected.Error);
            UiAutomationDriver.Inspect(list, "Window/ListBox", window).SelectionItems.Should().Equal("Module1");
        }
        finally { window.Close(); }
    }

    public sealed class Plain(string name)
    {
        public string Name { get; } = name;
    }

    // An item with no ToString of its own: the row is named by the text it shows, as a person reads it, where it had none.
    [AvaloniaFact]
    public void A_row_whose_item_names_itself_by_its_type_is_named_by_the_text_it_shows()
    {
        var (window, list) = Show(new[] { new Plain("Alpha"), new Plain("Beta") }, _ => "*", p => p?.Name ?? "");
        try
        {
            Flatten(UiAutomationDriver.Dump(window, "Window", 12, false)).Where(n => n.ControlType == "ListItem")
                .Select(n => n.Name).Should().Equal("* Alpha", "* Beta");

            list.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            UiAutomationDriver.Inspect(list, "Window/ListBox", window).SelectionItems.Should().Equal("* Beta");
        }
        finally { window.Close(); }
    }
}
