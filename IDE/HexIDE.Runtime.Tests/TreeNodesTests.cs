using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// The object-model slice of #1086; values are from the oracle's "In-box controls: the TreeView".
public class TreeNodesTests
{
    private static TreeNodes Nodes() => new();

    private static void Error(Action action, int number) =>
        action.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(number);

    [Fact]
    public void Empty_add_has_the_measured_defaults()
    {
        var nodes = Nodes();
        var n = nodes.Add();
        ((object)nodes.Count).Should().BeOfType<short>();
        ((object)n.Index).Should().BeOfType<short>();
        ((object)n.Children).Should().BeOfType<short>();
        nodes.Count.Should().Be(1);
        n.Index.Should().Be(1);
        n.Key.Should().Be("");
        n.Text.Should().Be("");
        n.Children.Should().Be(0);
        n.Parent.Should().BeNull();
        n.Child.Should().BeNull();
        n.Next.Should().BeNull();
        n.Previous.Should().BeNull();
        n.Root.Should().BeSameAs(n);
        n.FirstSibling.Should().BeSameAs(n);
        n.LastSibling.Should().BeSameAs(n);
        n.Expanded.Should().BeFalse();
        n.Visible.Should().BeTrue();
        nodes.SelectedItem.Should().BeNull();
    }

    [Fact]
    public void Relationships_change_tree_order_but_not_addition_indexes()
    {
        var nodes = Nodes();
        var root = nodes.Add(key: "r1");
        var c1 = nodes.Add(root, 4, "c1");
        nodes.Add(root, 4, "c2");
        nodes.Add(c1, 0, "first");
        nodes.Add(c1, 1, "last");
        nodes.Add(c1, 2, "next");
        nodes.Add(c1, 3, "prev");
        nodes.Add(c1, key: "norel");
        var keys = new List<string>();
        for (TreeNode? n = root.Child; n is not null; n = n.Next)
            keys.Add(n.Key);
        keys.Should().Equal("first", "prev", "c1", "norel", "next", "c2", "last");
        c1.Index.Should().Be(2);
        nodes.Item("norel").Index.Should().Be(8);
    }

    [Fact]
    public void Root_is_the_first_root_and_roots_are_siblings()
    {
        var nodes = Nodes();
        var r1 = nodes.Add(key: "r1");
        var r2 = nodes.Add(relationship: 4, key: "r2");
        var child = nodes.Add(r2, 4);
        var r0 = nodes.Add(r1, 0);
        child.Root.Should().BeSameAs(r0);
        r1.Next.Should().BeSameAs(r2);
        r1.Previous.Should().BeSameAs(r0);
        r2.FirstSibling.Should().BeSameAs(r0);
        r0.LastSibling.Should().BeSameAs(r2);
    }

    [Fact]
    public void Lookup_rounds_half_even_and_uses_addition_order()
    {
        var nodes = Nodes();
        var first = nodes.Add(key: "r1");
        var second = nodes.Add(first, 0, "r0");
        nodes.Add();
        nodes.Item(1).Should().BeSameAs(first);
        nodes.Item("r0").Should().BeSameAs(second);
        nodes.Item(1.5).Should().BeSameAs(second);
        nodes.Item(2.5).Should().BeSameAs(second);
    }

    [Theory]
    [InlineData(0, 35600)]
    [InlineData(999, 35600)]
    [InlineData("missing", 35601)]
    [InlineData("5", 35601)]
    [InlineData("", 35603)]
    public void Lookup_raises_the_measured_errors(object index, int error)
    {
        var nodes = Nodes();
        nodes.Add();
        Error(() => { nodes.Item(index); }, error);
        Error(() => { nodes.Remove(index); }, error);
    }

    [Fact]
    public void Bad_add_arguments_leave_the_collection_unchanged()
    {
        var nodes = Nodes();
        nodes.Add(key: "r1");
        Error(() => { nodes.Add(key: "r1"); }, 35602);
        Error(() => { nodes.Add(key: "5"); }, 35603);
        Error(() => { nodes.Add("missing"); }, 35601);
        Error(() => { nodes.Add(999); }, 35600);
        Error(() => { nodes.Add(relationship: 5); }, 380);
        Error(() => { nodes.Add(relationship: -1); }, 380);
        nodes.Count.Should().Be(1);
        nodes.Add(key: "");
        nodes.Add(key: "");
        nodes.Count.Should().Be(3);
        var first = nodes.Item("r1");
        var other = nodes.Item(2);
        Error(() => { other.Key = "r1"; }, 35602);
        first.Key = "";
        other.Key = "r1";
        nodes.Item("r1").Should().BeSameAs(other);
        other.Key = "";
        Error(() => { nodes.Item("r1"); }, 35601);
    }

    [Fact]
    public void Parent_moves_the_subtree_to_the_first_child_without_reindexing()
    {
        var nodes = Nodes();
        var a = nodes.Add(text: "a");
        var a1 = nodes.Add(a, 4, text: "a1");
        var b = nodes.Add(text: "b");
        var b1 = nodes.Add(b, 4);
        a.Parent = b;
        b.Child.Should().BeSameAs(a);
        a.Next.Should().BeSameAs(b1);
        a1.Parent.Should().BeSameAs(a);
        a.Index.Should().Be(1);
        a1.FullPath.Should().Be("b\\a\\a1");
        nodes.PathSeparator = "ab";
        a1.FullPath.Should().Be("babaaba1");
        nodes.PathSeparator = "";
        a1.FullPath.Should().Be("baa1");
        a1.Text = "leaf";
        a1.FullPath.Should().Be("baleaf");
        a1.Index.Should().Be(2);
    }

    [Fact]
    public void Parent_refuses_cycles_and_nothing_without_moving_anything()
    {
        var nodes = Nodes();
        var a = nodes.Add();
        var child = nodes.Add(a, 4);
        Error(() => { a.Parent = a; }, 35614);
        Error(() => { a.Parent = child; }, 35614);
        Error(() => { child.Parent = null; }, 35610);
        child.Parent.Should().BeSameAs(a);
    }

    [Fact]
    public void EnsureVisible_expands_ancestors_and_always_returns_false()
    {
        var nodes = Nodes();
        var a = nodes.Add();
        var child = nodes.Add(a, 4);
        var leaf = nodes.Add(child, 4);
        leaf.Visible.Should().BeFalse();
        leaf.EnsureVisible().Should().BeFalse();
        a.Expanded.Should().BeTrue();
        child.Expanded.Should().BeTrue();
        leaf.Visible.Should().BeTrue();
        leaf.EnsureVisible().Should().BeFalse();
        leaf.Expanded = true;
        a.Expanded = false;
        leaf.Expanded.Should().BeTrue();
        child.Expanded.Should().BeTrue();
        leaf.Visible.Should().BeFalse();
    }

    [Fact]
    public void Selection_keeps_a_deselected_item_but_nothing_clears_it()
    {
        var nodes = Nodes();
        var a = nodes.Add();
        var b = nodes.Add();
        a.Selected = true;
        b.Selected = true;
        a.Selected.Should().BeFalse();
        b.Selected = false;
        nodes.SelectedItem.Should().BeSameAs(b);
        nodes.SelectedItem = a;
        a.Selected.Should().BeTrue();
        nodes.SelectedItem = null;
        nodes.SelectedItem.Should().BeNull();
        a.Selected.Should().BeFalse();
    }

    [Fact]
    public void Collapse_selects_the_ancestor_and_remove_selects_the_parent()
    {
        var nodes = Nodes();
        var a = nodes.Add();
        var child = nodes.Add(a, 4);
        var leaf = nodes.Add(child, 4);
        a.Expanded = true;
        child.Expanded = true;
        leaf.Selected = true;
        a.Expanded = false;
        nodes.SelectedItem.Should().BeSameAs(a);
        leaf.Selected = true;
        nodes.Remove(3);
        nodes.SelectedItem.Should().BeSameAs(child);
    }

    [Fact]
    public void Remove_invalidates_the_subtree_reindexes_and_releases_keys()
    {
        var nodes = Nodes();
        var a = nodes.Add(key: "a");
        var child = nodes.Add(a, 4, "child");
        var b = nodes.Add(key: "b");
        nodes.Remove("a");
        nodes.Count.Should().Be(1);
        b.Index.Should().Be(1);
        Error(() => { var unused = a.Text; }, 35605);
        Error(() => { var unused = child.Parent; }, 35605);
        var replacement = nodes.Add(key: "a");
        replacement.Index.Should().Be(2);
        nodes.Clear();
        nodes.Count.Should().Be(0);
        Error(() => { var unused = b.Index; }, 35605);
        nodes.SelectedItem.Should().BeNull();
    }

    [Fact]
    public void Checkboxes_on_clears_checked_each_time_and_off_preserves_it()
    {
        var nodes = Nodes();
        var a = nodes.Add();
        a.Checked = true;
        nodes.Checkboxes = true;
        a.Checked.Should().BeFalse();
        a.Checked = true;
        nodes.Checkboxes = false;
        a.Checked.Should().BeTrue();
        nodes.Checkboxes = true;
        a.Checked.Should().BeFalse();
    }

    [Fact]
    public void Counts_and_indexes_wrap_but_lookup_uses_the_full_index()
    {
        var nodes = Nodes();
        var root = nodes.Add();
        var last = root;
        for (var i = 2; i <= 32768; i++) last = nodes.Add(root, 4);
        nodes.Count.Should().Be(short.MinValue);
        last.Index.Should().Be(short.MinValue);
        nodes.Item(32768L).Should().BeSameAs(last);
        Error(() => { nodes.Item(-32768); }, 35600);
        for (var i = 32769; i <= 65536; i++) nodes.Add(root, 4);
        nodes.Count.Should().Be(0);
        nodes.Add(root, 4);
        root.Children.Should().Be(0);
        nodes.Count.Should().Be(1);
    }

    [Fact]
    public void Root_sort_matches_the_measured_word_order_and_keeps_addition_indexes()
    {
        var nodes = Nodes();
        string[] labels = ["b", "B", "a10", "a2", "10", "9", "", "Zeta", "_x", "-y", " space", "co-op", "coop", "eé", "ez", "Ole", "OLE"];
        var added = labels.Select(label => nodes.Add(text: label)).ToArray();
        nodes.Sorted = true;
        nodes.Roots.Select(n => n.Text).Should().Equal("", " space", "_x", "10", "9", "a10", "a2", "b", "B", "coop", "co-op", "eé", "ez", "Ole", "OLE", "-y", "Zeta");
        added.Select(n => n.Index).Should().Equal(Enumerable.Range(1, labels.Length).Select(i => (short)i));
        added[0].Root.Should().BeSameAs(added[6]);
        nodes.Item(1).Should().BeSameAs(added[0]);
    }

    [Fact]
    public void Root_and_node_sorting_are_independent()
    {
        var nodes = Nodes();
        var z = nodes.Add(text: "Zeta");
        var b = nodes.Add(z, 4, text: "b");
        var a = nodes.Add(z, 4, text: "a10");
        var root = nodes.Add(text: "a2");
        nodes.Sorted = true;
        nodes.Roots.Should().Equal(root, z);
        z.Child.Should().BeSameAs(b);
        z.Sorted = true;
        z.Child.Should().BeSameAs(a);
        a.Next.Should().BeSameAs(b);
        nodes.Roots.Should().Equal(root, z);
    }

    [Fact]
    public void Renaming_does_not_sort_but_setting_sorted_true_again_does()
    {
        var nodes = Nodes();
        var a = nodes.Add(text: "a10");
        var b = nodes.Add(text: "b");
        nodes.Sorted = true;
        a.Text = "Zeta";
        nodes.ProcessPendingSort();
        nodes.Roots.Should().Equal(a, b);
        nodes.Sorted = true;
        nodes.Roots.Should().Equal(b, a);
        b.Text = "Zeta";
        a.Text = "a10";
        nodes.Sorted = false;
        nodes.ProcessPendingSort();
        nodes.Roots.Should().Equal(b, a);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Sorted_children_ignore_the_add_relationship(int relationship)
    {
        var nodes = Nodes();
        var parent = nodes.Add();
        var mike = nodes.Add(parent, 4, text: "mike");
        parent.Sorted = true;
        var yankee = nodes.Add(mike, relationship, text: "yankee");
        var xray = nodes.Add(mike, relationship, text: "xray");
        parent.Child.Should().BeSameAs(mike);
        mike.Next.Should().BeSameAs(xray);
        xray.Next.Should().BeSameAs(yankee);
        xray.Index.Should().Be(4);
    }

    [Fact]
    public void Root_added_without_relative_sorts_only_when_pending_work_is_processed()
    {
        var nodes = Nodes();
        var z = nodes.Add(text: "Zeta");
        nodes.Sorted = true;
        var a = nodes.Add(text: "a10");
        nodes.Roots.Should().Equal(z, a);
        nodes.ProcessPendingSort();
        nodes.Roots.Should().Equal(a, z);
        var first = nodes.Add(a, 0, text: "ez");
        nodes.ProcessPendingSort();
        nodes.Roots.Should().Equal(first, a, z);
        nodes.Sorted = true;
        nodes.Roots.Should().Equal(a, first, z);
    }

    [Fact]
    public void Node_rename_waits_for_an_explicit_resort_and_false_keeps_order()
    {
        var nodes = Nodes();
        var parent = nodes.Add();
        var a = nodes.Add(parent, 4, text: "a10");
        var b = nodes.Add(parent, 4, text: "b");
        parent.Sorted = true;
        a.Text = "Zeta";
        nodes.ProcessPendingSort();
        parent.Child.Should().BeSameAs(a);
        parent.Sorted = false;
        parent.Child.Should().BeSameAs(a);
        parent.Sorted = true;
        parent.Child.Should().BeSameAs(b);
    }


    [Fact]
    public void Sorting_refuses_unmeasured_characters_without_changing_order_or_sorted()
    {
        var nodes = Nodes();
        var first = nodes.Add(text: "Zeta");
        var second = nodes.Add(text: "\u4e00");
        Action sort = () => nodes.Sorted = true;
        sort.Should().Throw<NotImplementedException>();
        nodes.Sorted.Should().BeFalse();
        nodes.Roots.Should().Equal(first, second);
        var parent = nodes.Add();
        nodes.Add(parent, 4, text: "\u4e00");
        Action sortChildren = () => parent.Sorted = true;
        sortChildren.Should().Throw<NotImplementedException>();
        parent.Sorted.Should().BeFalse();
        var measured = Nodes();
        measured.Add(text: "\u0152");
        Action measuredSort = () => measured.Sorted = true;
        measuredSort.Should().NotThrow();
    }

    [Fact]
    public void Sorted_child_add_refuses_unmeasured_text_without_adding_a_node()
    {
        var nodes = Nodes();
        var parent = nodes.Add();
        var child = nodes.Add(parent, 4, text: "mike");
        parent.Sorted = true;
        Action add = () => nodes.Add(parent, 4, "new", "\u4e00");
        add.Should().Throw<NotImplementedException>();
        nodes.Count.Should().Be(2);
        parent.Child.Should().BeSameAs(child);
        child.Next.Should().BeNull();
        nodes.Add(key: "new");
    }

}
