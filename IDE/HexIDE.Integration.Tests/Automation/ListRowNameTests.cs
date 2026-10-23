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
/// row had no name, select by value could not find it, and inspect_element named the selection "ListItem" (#1299). The
/// Object Browser's rows were all of that shape.
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

    [AvaloniaFact]
    public void Dump_WhitespaceNames_EveryPathResolvesToItsReportedControl()
    {
        string[] names = ["    Form1", "Module1   ", "\tTabbed\t", "  Duplicate", "Duplicate",
            "    ", "Left[Bracket", "Right]Bracket", "A/B", "#1", "Inner  Spaces"];
        var rows = names.Select(name => new ListBoxItem { Content = name }).ToArray();
        var list = new ListBox { ItemsSource = rows };
        var window = new Window { Width = 400, Height = 700, Content = list };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var nodes = Flatten(UiAutomationDriver.Dump(window, "Window", 20, false)).ToList();
            var itemNodes = nodes.Where(n => n.ControlType == "ListItem").ToList();
            itemNodes.Should().HaveCount(rows.Length);
            // Check identity for every reported node, including the list's template children.
            foreach (var node in nodes)
            {
                var (control, error) = UiAutomationDriver.Resolve(window, node.Path);
                error.Should().BeNull(node.Path);
                control.Should().NotBeNull();
                UiAutomationDriver.PathWithin(window, "Window", control!).Should().Be(node.Path);
                if (node.ControlType == "ListItem")
                    control.Should().BeSameAs(rows[itemNodes.IndexOf(node)]);
            }
            itemNodes.Select(n => n.Name).Should().Equal(names);
            itemNodes[0].Path.Should().EndWith("ListItem[Form1]");
            itemNodes[1].Path.Should().EndWith("ListItem[Module1]");
            itemNodes[2].Path.Should().EndWith("ListItem[Tabbed]");
            foreach (var index in new[] { 3, 4, 5, 6, 7, 8, 9 })
                itemNodes[index].Path.Should().EndWith($"ListItem[#{index}]");
            itemNodes[10].Path.Should().EndWith("ListItem[Inner  Spaces]");
        }
        finally { window.Close(); }
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
            new OBMemberViewModel("Caption", OBMemberKind.Property, "Property Caption As String").ToString().Should().Be("Caption");
        }
        finally { window.Close(); }
    }
}
