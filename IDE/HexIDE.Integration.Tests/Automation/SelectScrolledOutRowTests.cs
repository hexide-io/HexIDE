using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
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
    private static (Window Window, ListBox List, List<Row> Rows) ScrolledRows(params int[] hidden) => ScrolledRows(false, hidden);

    private static (Window Window, ListBox List, List<Row> Rows) ScrolledRows(bool editable, params int[] hidden)
    {
        var rows = Enumerable.Range(0, 30).Select(i => new Row($"Row{i}", !hidden.Contains(i))).ToList();
        var list = new ListBox
        {
            Height = 40,
            Focusable = !editable,
            ItemsSource = rows,
            ItemTemplate = new FuncDataTemplate<Row>((r, _) =>
            {
                Control text = editable && r?.Name == "Row28"
                    ? new TextBox { Text = "original" }
                    : new TextBlock { Text = r?.Name };
                Avalonia.Automation.AutomationProperties.SetAutomationId(text, $"txt_{r?.Name}");
                return new Border { Height = 16, Child = text };
            }),
        };
        list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(Avalonia.Visual.IsVisibleProperty, new Avalonia.Data.Binding(nameof(Row.Visible))),
                new Setter(Avalonia.Input.InputElement.FocusableProperty, !editable),
            },
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

    // The scroll a select made and put back leaves a released row in the tree, holding the item it last showed, and once
    // that item shows again so does the row, by its bound visibility. The dump listed it as a row, a second copy of an
    // item or a row for one out of view, and a path or an id reached it (#1292). It lists only the rows the list holds.
    [AvaloniaFact]
    public void The_dump_leaves_out_a_row_the_list_has_let_go_of()
    {
        var (window, list, rows) = ScrolledRows(28);
        try
        {
            UiAutomationDriver.Interact(list, "select", "Row28").Success.Should().BeFalse();
            rows[28].Visible = true;
            Dispatcher.UIThread.RunJobs();
            var released = list.GetVisualDescendants().OfType<ListBoxItem>().Where(i => list.IndexFromContainer(i) < 0).ToList();
            released.Should().Contain(i => i.IsEffectivelyVisible, "a released row must show for this to test anything");

            var dump = Flatten(UiAutomationDriver.Dump(window, "Window", 12, false)).ToList();
            var rowNames = dump.Where(n => n.ControlType == "ListItem").Select(n => n.Name).ToList();

            var held = Enumerable.Range(0, list.ItemCount).Where(i => list.ContainerFromIndex(i) is not null)
                .Select(i => $"Row{i}").ToList();
            rowNames.Should().BeEquivalentTo(held, "the dump lists the rows the list holds, each once");
            dump.Should().NotContain(n => n.Name == "Row28", "nothing of the released row is listed, its text included");
            var heldPath = dump.Single(n => n.ControlType == "ListItem" && n.Name == "Row0").Path;
            UiAutomationDriver.Resolve(window, heldPath).control.Should().NotBeNull("a held row's path resolves");
            UiAutomationDriver.Resolve(window, heldPath.Replace("Row0", "Row28")).control.Should().BeNull("a path cannot reach it either");
            UiAutomationDriver.Resolve(window, "Window/#txt_Row0").control.Should().NotBeNull("a held row's text is found by its id");
            UiAutomationDriver.Resolve(window, "Window/#txt_Row28").control.Should().BeNull("nor can an id");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_dump_lists_scrolled_rows_in_item_order(bool home)
    {
        var (window, list, _) = ScrolledRows();
        try
        {
            list.Height = 100;
            list.UpdateLayout();
            UiAutomationDriver.Interact(list, "select", home ? "Row2" : "Row28").Success.Should().BeTrue();
            if (home)
            {
                list.ScrollIntoView(28);
                list.UpdateLayout();
                list.ScrollIntoView(0);
                list.UpdateLayout();
            }

            var dump = Flatten(UiAutomationDriver.Dump(window, "Window", 12, false)).ToList();
            var rows = dump.Where(n => n.ControlType == "ListItem").ToList();
            var indices = rows.Select(n => int.Parse(n.Name![3..])).ToList();
            indices.Count.Should().BeGreaterThan(2);
            indices.Should().BeInAscendingOrder();
            indices.Should().OnlyHaveUniqueItems();
            foreach (var row in rows)
                UiAutomationDriver.Resolve(window, row.Path).control.Should().BeSameAs(list.ContainerFromIndex(int.Parse(row.Name![3..])));
        }
        finally { window.Close(); }
    }

    // Only Row28 has an input surface; panel order cannot decide which item receives input (#1327).
    private static (Window Window, ListBox List, TextBox Box) ReleasedTextBox()
    {
        var (window, list, rows) = ScrolledRows(true, 28);
        try
        {
            UiAutomationDriver.Interact(list, "select", "Row28").Success.Should().BeFalse();
            rows[28].Visible = true;
            Dispatcher.UIThread.RunJobs();
            var box = list.GetVisualDescendants().OfType<TextBox>().Single();
            Avalonia.Automation.AutomationProperties.GetAutomationId(box).Should().Be("txt_Row28");
            var row = box.GetVisualAncestors().OfType<ListBoxItem>().Single();
            list.IndexFromContainer(row).Should().BeNegative("the list must have released the row holding the box");
            ItemsControl.ItemsControlFromItemContainer(row).Should().BeNull();
            row.IsEffectivelyVisible.Should().BeTrue("the released row must show to exercise the receiver gap");
            box.IsEffectivelyVisible.Should().BeTrue();
            return (window, list, box);
        }
        catch { window.Close(); throw; }
    }

    [AvaloniaFact]
    public void Press_key_does_not_reach_a_box_in_a_released_row()
    {
        var (window, list, box) = ReleasedTextBox();
        try
        {
            var keyDown = false;
            box.KeyDown += (_, _) => keyDown = true;

            var outcome = UiAutomationDriver.PressKey(list, "A", null, "Window/ListBox");

            keyDown.Should().BeFalse("a released row must receive no key");
            outcome.Success.Should().BeFalse();
            outcome.Error.Should().NotBeNullOrEmpty();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Type_text_does_not_reach_a_box_in_a_released_row()
    {
        var (window, list, box) = ReleasedTextBox();
        try
        {
            var before = box.Text;

            var outcome = UiAutomationDriver.TypeText(list, "x", "Window/ListBox");

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("has no text surface");
            box.Text.Should().Be(before);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Hover_does_not_choose_an_editor_in_a_released_row()
    {
        var (window, list, box) = ReleasedTextBox();
        try
        {
            // Keep the released container and replace its content with the surface hover resolves.
            var border = (Border)box.Parent!;
            var editor = new TextEditor { Text = "original" };
            border.Child = editor;
            list.UpdateLayout();
            var row = editor.GetVisualAncestors().OfType<ListBoxItem>().Single();
            list.IndexFromContainer(row).Should().BeNegative();
            editor.IsEffectivelyVisible.Should().BeTrue();

            var outcome = UiAutomationDriver.Hover(list, null, null, "Window/ListBox", out _);

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().NotContain("on TextEditor", "hover must use the list rather than its released editor");
        }
        finally { UiAutomationDriver.EndHover(list); window.Close(); }
    }

    // A container written into a template, or set as content, is no list's row, and no list let it go: it is listed, with
    // what it holds (#1292 review).
    [AvaloniaFact]
    public void A_container_a_template_holds_is_listed()
    {
        var items = new ItemsControl
        {
            ItemsSource = new[] { "I0", "I1" },
            ItemTemplate = new FuncDataTemplate<string>((s, _) => new ListBoxItem { Content = new Button { Content = $"btn {s}" } }),
        };
        var window = new Window { Content = items, Width = 300, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Flatten(UiAutomationDriver.Dump(window, "Window", 12, false)).Select(n => n.Name)
                .Should().Contain(["btn I0", "btn I1"]);
        }
        finally { window.Close(); }
    }
}
