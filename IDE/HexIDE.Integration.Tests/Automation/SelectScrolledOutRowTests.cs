using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// <c>select</c> with a value finds a list row that is scrolled out of view (#627). A list creates rows only for
/// what is on screen, so such a row had no container and nothing to match, and the refusal counted only the rows
/// on screen, as if the row did not exist.
/// </summary>
public class SelectScrolledOutRowTests
{
    private static (Window Window, ListBox List) ScrolledToTheMiddle()
    {
        var items = new ObservableCollection<string>(Enumerable.Range(0, 30).Select(i => $"Row{i}"));
        items[0] = "(Name)";
        var list = new ListBox
        {
            Height = 40,
            ItemsSource = items,
            ItemTemplate = new FuncDataTemplate<string>((s, _) => new Border { Height = 16, Child = new TextBlock { Text = s } }),
        };
        var window = new Window { Content = list, Width = 300, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        // The middle rather than the end: a search that checks every row ends near the end, so a list that
        // started there would look put back whether or not it was.
        list.ScrollIntoView(15);
        list.UpdateLayout();
        list.ContainerFromIndex(0).Should().BeNull("the first row must be scrolled out of view for this to test anything");
        return (window, list);
    }

    [AvaloniaFact]
    public void A_row_scrolled_out_of_view_is_scrolled_to_and_selected()
    {
        var (window, list) = ScrolledToTheMiddle();
        try
        {
            var outcome = UiAutomationDriver.Interact(list, "select", "(Name)");

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().Be("selected '(Name)' (it was scrolled out of view, so the list was scrolled to it first)");
            list.SelectedItem.Should().Be("(Name)");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_row_on_screen_is_selected_without_saying_it_was_scrolled_to()
    {
        var (window, list) = ScrolledToTheMiddle();
        try
        {
            var outcome = UiAutomationDriver.Interact(list, "select", "Row15");

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().Be("selected 'Row15'");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_miss_counts_every_item_and_leaves_the_list_where_it_was()
    {
        var (window, list) = ScrolledToTheMiddle();
        try
        {
            var before = list.Scroll!.Offset;

            var outcome = UiAutomationDriver.Interact(list, "select", "Nowhere");

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().StartWith("no selectable item matching 'Nowhere' among the 30 item(s)");
            list.Scroll!.Offset.Should().Be(before, "a search that found nothing must not leave the list scrolled somewhere else");
            list.SelectedItem.Should().BeNull();
        }
        finally { window.Close(); }
    }

    private sealed class Row(string name, bool visible = true) : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _visible = visible;
        public string Name { get; } = name;
        public bool Visible
        {
            get => _visible;
            set { _visible = value; PropertyChanged?.Invoke(this, new(nameof(Visible))); }
        }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public override string ToString() => Name;
    }

    // Rows that are view models with their visibility bound, as the Properties window's are.
    private static (Window Window, ListBox List, List<Row> Rows) ScrolledRows(params int[] hidden)
    {
        var rows = Enumerable.Range(0, 30).Select(i => new Row($"Row{i}", !hidden.Contains(i))).ToList();
        var list = new ListBox
        {
            Height = 40,
            ItemsSource = rows,
            ItemTemplate = new FuncDataTemplate<Row>((r, _) => new Border { Height = 16, Child = new TextBlock { Text = r?.Name } }),
        };
        list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(Avalonia.Visual.IsVisibleProperty, new Avalonia.Data.Binding(nameof(Row.Visible))) },
        });
        var window = new Window { Content = list, Width = 300, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, list, rows);
    }

    // A row the list hides is not selected by its text when scrolled in either. The refusal names that row, read as it is
    // found: by the time the list is put back, its container shows another row, which the refusal named (#1256 review).
    // The row the scrolling let go of does not stand in for it afterwards.
    [AvaloniaFact]
    public void A_hidden_row_out_of_view_is_refused_by_its_own_name_and_selected_once_it_shows()
    {
        var (window, list, rows) = ScrolledRows(28);
        try
        {
            list.ContainerFromIndex(28).Should().BeNull("the row must be out of view for this to test anything");
            var before = list.Scroll!.Offset;

            var outcome = UiAutomationDriver.Interact(list, "select", "Row28");

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().StartWith("'Row28' is not showing");
            list.SelectedItem.Should().BeNull();
            list.Scroll!.Offset.Should().Be(before);

            rows[28].Visible = true;
            Dispatcher.UIThread.RunJobs();
            var selected = UiAutomationDriver.Interact(list, "select", "Row28");
            selected.Success.Should().BeTrue(selected.Error);
            list.SelectedItem.Should().BeSameAs(rows[28]);
        }
        finally { window.Close(); }
    }

    // A miss counts the rows that show and says how many do not, those not yet scrolled in included (#1256 review).
    [AvaloniaFact]
    public void A_miss_counts_the_rows_that_show_and_those_that_do_not()
    {
        var (window, list, _) = ScrolledRows(1, 20, 28);
        try
        {
            var outcome = UiAutomationDriver.Interact(list, "select", "Nowhere");

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().StartWith("no selectable item matching 'Nowhere' among the 27 item(s) showing in ")
                .And.Contain("; 3 more are not showing and were not matched —");
        }
        finally { window.Close(); }
    }

    private static IEnumerable<UiNode> Flatten(UiNode node) => node.Children.SelectMany(Flatten).Prepend(node);

    // The scroll a select made and put back leaves a released row in the tree, still holding the item it last showed and
    // still showing by its bound visibility. The dump listed it as a row: a second copy of an item, or a row for one out of
    // view (#1292). It lists only the rows the list holds.
    [AvaloniaFact]
    public void The_dump_leaves_out_a_row_the_list_has_let_go_of()
    {
        var (window, list, _) = ScrolledRows(28);
        try
        {
            UiAutomationDriver.Interact(list, "select", "Row28").Success.Should().BeFalse();
            Dispatcher.UIThread.RunJobs();
            var released = list.GetVisualDescendants().OfType<ListBoxItem>().Where(i => list.IndexFromContainer(i) < 0).ToList();
            released.Should().NotBeEmpty("the scroll must have left a released row for this to test anything");

            var dump = Flatten(UiAutomationDriver.Dump(window, "Window", 12, false)).ToList();
            var rows = dump.Where(n => n.ControlType == "ListItem").Select(n => n.Name).ToList();

            var held = Enumerable.Range(0, list.ItemCount).Where(i => list.ContainerFromIndex(i) is not null)
                .Select(i => $"Row{i}").ToList();
            rows.Should().BeEquivalentTo(held, "the dump lists the rows the list holds, each once");
            dump.Should().NotContain(n => n.Name == "Row28", "nothing of the released row is listed, its text included");
            var heldPath = dump.Single(n => n.ControlType == "ListItem" && n.Name == "Row0").Path;
            UiAutomationDriver.Resolve(window, heldPath).control.Should().NotBeNull("a held row's path resolves");
            UiAutomationDriver.Resolve(window, heldPath.Replace("Row0", "Row28")).control.Should().BeNull("a path cannot reach it either");
        }
        finally { window.Close(); }
    }
}
