using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class TabStripModelTests
{
    [Fact]
    public void A_block_without_tabs_starts_with_one_empty_selected_tab()
    {
        var strip = new TabStripModel();
        strip.Tabs.Should().ContainSingle();
        strip.SelectedItem.Should().BeSameAs(strip.Tabs[0]);
        strip.Tabs[0].Caption.Should().BeEmpty();
        strip.Tabs[0].Key.Should().BeEmpty();
    }

    [Fact]
    public void Keys_are_case_sensitive_and_rename_changes_lookup()
    {
        var strip = new TabStripModel();
        var lower = strip.Add(key: "k2");
        var upper = strip.Add(key: "K2");
        strip.Item(new Vb6Value("k2")).Should().BeSameAs(lower);
        strip.Item(new Vb6Value("K2")).Should().BeSameAs(upper);
        lower.Key = "renamed";
        strip.Item(new Vb6Value("renamed")).Should().BeSameAs(lower);
        Error(() => strip.Item(new Vb6Value("k2")), 35601);
        Error(() => upper.Key = "renamed", 35602);
        upper.Key.Should().Be("K2");
    }

    [Theory]
    [InlineData(1.4, 1)]
    [InlineData(1.5, 2)]
    [InlineData(1.6, 2)]
    [InlineData(2.5, 2)]
    [InlineData(2.6, 3)]
    [InlineData(3.5, 4)]
    public void Numeric_indexes_round_half_to_even(double index, int expected)
    {
        var strip = new TabStripModel();
        strip.Add();
        strip.Add();
        strip.Add();
        strip.Item(new Vb6Value(index)).Index.Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void Invalid_positions_are_35600(int index)
    {
        var strip = new TabStripModel();
        Error(() => strip.Item(new Vb6Value(index)), 35600);
        Error(() => strip.Remove(new Vb6Value(index)), 35600);
        Error(() => strip.Add(index), 35600);
        strip.Tabs.Should().ContainSingle();
    }

    [Fact]
    public void Add_checks_keys_and_string_indexes_are_always_keys()
    {
        var strip = new TabStripModel();
        strip.Add(key: "k2");
        Error(() => strip.Add(key: "k2"), 35602);
        Error(() => strip.Add(key: "7"), 35603);
        Error(() => strip.Item(new Vb6Value("3")), 35601);
        strip.Add(key: "");
        strip.Add(key: "");
        strip.Tabs.Count.Should().Be(4);
    }

    [Fact]
    public void Insertion_at_selection_takes_selection_but_insertion_after_does_not()
    {
        var strip = new TabStripModel();
        var original = strip.SelectedItem;
        strip.Add();
        strip.SelectedItem.Should().BeSameAs(original);
        var inserted = strip.Add(1, caption: "Zero");
        strip.SelectedItem.Should().BeSameAs(inserted);
        strip.Tabs.Select(t => t.Index).Should().Equal(1, 2, 3);
        original!.Index.Should().Be(2);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Every_remove_selects_the_first_tab_without_events(int removed)
    {
        var strip = new TabStripModel();
        strip.Add();
        strip.Add();
        strip.Select(strip.Add());
        strip.BeforeClick = () => throw new System.Exception("Unexpected BeforeClick");
        strip.Click = () => throw new System.Exception("Unexpected Click");
        strip.Remove(new Vb6Value(removed));
        strip.SelectedItem.Should().BeSameAs(strip.Tabs[0]);
    }

    [Fact]
    public void Clear_then_add_selects_the_new_tab_without_events()
    {
        var strip = new TabStripModel();
        strip.BeforeClick = () => throw new System.Exception("Unexpected BeforeClick");
        strip.Click = () => throw new System.Exception("Unexpected Click");
        strip.Clear();
        strip.Tabs.Should().BeEmpty();
        strip.SelectedItem.Should().BeNull();
        var tab = strip.Add();
        strip.SelectedItem.Should().BeSameAs(tab);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Selection_raises_before_with_old_selection_then_click_even_when_cancelled(bool cancel)
    {
        var strip = new TabStripModel();
        var old = strip.SelectedItem;
        var next = strip.Add();
        var events = new List<string>();
        strip.BeforeClick = () =>
        {
            strip.SelectedItem.Should().BeSameAs(old);
            events.Add("BeforeClick");
            return cancel;
        };
        strip.Click = () =>
        {
            strip.SelectedItem.Should().BeSameAs(cancel ? old : next);
            events.Add("Click");
        };
        strip.Select(next);
        events.Should().Equal("BeforeClick", "Click");
    }

    [Fact]
    public void Reselecting_and_deselecting_the_selected_tab_still_raise_both_events()
    {
        var strip = new TabStripModel();
        var events = new List<string>();
        strip.BeforeClick = () => { events.Add("BeforeClick"); return false; };
        strip.Click = () => events.Add("Click");
        strip.Select(strip.SelectedItem);
        strip.SelectedItem!.Selected = false;
        strip.SelectedItem.Selected.Should().BeTrue();
        events.Should().Equal("BeforeClick", "Click", "BeforeClick", "Click");
    }

    [Fact]
    public void Selecting_after_Nothing_raises_only_click()
    {
        var strip = new TabStripModel();
        var tab = strip.SelectedItem;
        var events = new List<string>();
        strip.BeforeClick = () => { events.Add("BeforeClick"); return false; };
        strip.Click = () => events.Add("Click");
        strip.Select(null);
        events.Should().BeEmpty();
        strip.SelectedItem.Should().BeNull();
        strip.Select(tab);
        events.Should().Equal("Click");
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void DeselectAll_only_clears_button_style(int style, bool selected)
    {
        var strip = new TabStripModel();
        strip.DeselectAll(style);
        (strip.SelectedItem is not null).Should().Be(selected);
    }

    [Fact]
    public void Captions_are_kept_verbatim_and_code_selection_is_single()
    {
        var strip = new TabStripModel();
        var second = strip.Add(caption: "&Mnemonic");
        second.Caption.Should().Be("&Mnemonic");
        second.Caption = "Renamed";
        second.Caption.Should().Be("Renamed");
        second.Selected = true;
        strip.Tabs.Select(t => t.Selected).Should().Equal(false, true);
        strip.Tabs[0].Selected = true;
        strip.Tabs.Select(t => t.Selected).Should().Equal(true, false);
    }

    [Fact]
    public void Unmeasured_edges_refuse_instead_of_inventing_values()
    {
        var strip = new TabStripModel();
        System.Action conversion = () => strip.Item(new Vb6Value(Vb6Value.ValueType.Null));
        conversion.Should().Throw<System.NotImplementedException>();
        System.Action foreign = () => strip.Select(new TabStripModel().SelectedItem);
        foreign.Should().Throw<System.NotImplementedException>();
        var removed = strip.SelectedItem!;
        strip.Remove(new Vb6Value(1));
        System.Action index = () => _ = removed.Index;
        index.Should().Throw<System.NotImplementedException>();
        var first = strip.Add();
        var second = strip.Add();
        System.Action deselect = () => second.Selected = false;
        deselect.Should().Throw<System.NotImplementedException>();
        first.Selected.Should().BeTrue();
        System.Action flat = () => strip.DeselectAll(2);
        flat.Should().Throw<System.NotImplementedException>();
    }

    private static void Error(System.Action action, int number) =>
        action.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(number);
}
