using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Integration.Tests;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Integration.Tests.Views;

// #4209, round 2 supervisor comment: measured 2026-11-20 on SP6, native and p-code agreeing.
public class ListBoxCheckboxItemsTests : WindowTestFixture
{
    private sealed class Recorder : Decorator, IModuleExecutionRoot
    {
        internal VBListBox List { get; set; } = null!;
        internal List<string> Calls { get; } = [];
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
        {
            if (name == "List1_ItemCheck") Calls.Add($"ItemCheck {args![0].Value}");
            else if (name == "List1_Click") Calls.Add($"Click {List.ListIndex}");
            else if (name == "List1_Scroll") Calls.Add("Scroll");
        }
    }

    private const string Four = "For i = 0 To 3\nList1.AddItem \"i\" & i\nNext\n";
    private const string Checked = Four + "List1.Selected(2) = True\nList1.Selected(3) = True\nList1.ListIndex = 2\n";
    private const string Inserted = Checked + "List1.AddItem \"z\", 0\nList1.AddItem \"y\", 4\nList1.AddItem \"x\", 3\n";

    private static async Task<(Recorder Recorder, List<object?> Values)> Run(string setup, string code, int style = 1, bool sorted = false)
    {
        var form = $$"""
            VERSION 5.00
            Begin VB.Form Form1
               Begin VB.ListBox List1
                  Width = 2400
                  Height = 2205
                  Style = {{style}}
                  Sorted = {{(sorted ? -1 : 0)}}
               End
            End
            Attribute VB_Name = "Form1"
            """;
        var (root, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(form);
        var recorder = new Recorder { Child = root, List = (VBListBox)ContainerRuntimeHarness.Child(canvas, "List1") };
        var window = new Window { Content = recorder, Width = 400, Height = 300 };
        ContainerRuntimeHarness.MergeAppResources(window);
        window.Show();
        await ContainerRuntimeHarness.Run(ctx, env, setup.ReplaceLineEndings("\r\n"));
        Dispatcher.UIThread.RunJobs();
        recorder.Calls.Clear();
        var values = await ContainerRuntimeHarness.Run(ctx, env, code.ReplaceLineEndings("\r\n"));
        Dispatcher.UIThread.RunJobs();
        return (recorder, values.Select(v => v.Value).ToList());
    }

    [AvaloniaTheory]
    [InlineData("", 0, false, false)]
    [InlineData("List1.Selected(1) = True\n", 1, true, false)]
    [InlineData("List1.Selected(1) = True\nList1.Selected(3) = True\n", 2, true, true)]
    [InlineData("List1.Selected(1) = True\nList1.Selected(3) = True\nList1.Selected(1) = False\n", 1, false, true)]
    public async Task SelCount_counts_checks(string writes, int count, bool first, bool third)
    {
        var (_, values) = await Run(Four + writes,
            "Debug.Print List1.SelCount\nDebug.Print List1.Selected(1)\nDebug.Print List1.Selected(3)\n");
        values.Should().Equal(count, first, third);
    }

    [AvaloniaTheory]
    [InlineData("List1.ListIndex = 0\n", "List1.Selected(1) = True", 1, true, "ItemCheck 1|Click 1")]
    [InlineData("List1.Selected(3) = True\n", "List1.Selected(3) = True", 3, true, "")]
    [InlineData("List1.Selected(1) = True\nList1.Selected(3) = True\n", "List1.Selected(1) = False", 3, false, "ItemCheck 1|Click 3")]
    public async Task Selected_writes_raise_measured_events_and_preserve_focus_when_unchecking(
        string setup, string statement, int index, bool check, string events)
    {
        var item = statement.Contains("(3)", StringComparison.Ordinal) ? 3 : 1;
        var (recorder, values) = await Run(Four + setup,
            $"{statement}\nDebug.Print List1.ListIndex\nDebug.Print List1.Text\nDebug.Print List1.Selected({item})\n");
        values.Should().Equal(index, $"i{index}", check);
        recorder.Calls.Should().Equal(events.Length == 0 ? [] : events.Split('|'));
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ListIndex_moves_without_changing_checks(int index)
    {
        var (recorder, values) = await Run(Four + "List1.Selected(3) = True\n",
            $"List1.ListIndex = {index}\nDebug.Print List1.ListIndex\nDebug.Print List1.Selected(3)\n");
        values.Should().Equal(index, true);
        recorder.Calls.Should().Equal($"Click {index}");
    }

    [AvaloniaTheory]
    [InlineData("", "List1.AddItem \"a\"", "a", "", 0, "a", false)]
    [InlineData("List1.AddItem \"a\"\nList1.ListIndex = 0\n", "List1.AddItem \"b\", 0", "b|a", "", 0, "b", false)]
    [InlineData(Four + "List1.ListIndex = -1\n", "List1.AddItem \"n\"", "i0|i1|i2|i3|n", "", -1, "", false)]
    [InlineData(Checked, "List1.AddItem \"z\", 0", "z|i0|i1|i2|i3", "3|4", 2, "i1", false)]
    [InlineData(Checked + "List1.AddItem \"z\", 0\n", "List1.AddItem \"y\", 4", "z|i0|i1|i2|y|i3", "3|5", 2, "i1", false)]
    [InlineData(Checked + "List1.AddItem \"z\", 0\nList1.AddItem \"y\", 4\n", "List1.AddItem \"x\", 3", "z|i0|i1|x|i2|y|i3", "4|6", 2, "i1", false)]
    [InlineData(Inserted, "List1.RemoveItem 0", "i0|i1|x|i2|y|i3", "3|5", 2, "x", false)]
    [InlineData(Four + "List1.Selected(3) = True\nList1.ListIndex = 1\n", "List1.RemoveItem 0", "i1|i2|i3", "2", 1, "i2", false)]
    [InlineData(Four + "List1.Selected(2) = True\nList1.ListIndex = 0\n", "List1.RemoveItem 2", "i0|i1|i3", "", 0, "i0", false)]
    [InlineData(Checked, "List1.Clear", "", "", -1, "", false)]
    [InlineData(Checked + "List1.Clear\n", "List1.AddItem \"c\"", "c", "", 0, "c", false)]
    [InlineData("", "List1.AddItem \"b\"", "b", "", 0, "b", true)]
    [InlineData("List1.AddItem \"b\"\nList1.ListIndex = 0\n", "List1.AddItem \"a\"", "a|b", "", 0, "a", true)]
    [InlineData("List1.AddItem \"b\"\nList1.AddItem \"a\"\nList1.Selected(1) = True\n", "List1.AddItem \"0\"", "0|a|b", "2", 1, "a", true)]
    [InlineData(Inserted + "List1.RemoveItem 0\nList1.ListIndex = 2\n", "List1.RemoveItem 2", "i0|i1|i2|y|i3", "2|4", 1, "i1", false)]
    [InlineData("List1.AddItem \"i0\"\nList1.AddItem \"i1\"\nList1.AddItem \"i2\"\nList1.ListIndex = 2\n", "List1.RemoveItem 2", "i0|i1", "", 1, "i1", false)]
    [InlineData(Four + "List1.ListIndex = 1\n", "List1.RemoveItem 1", "i0|i2|i3", "", 0, "i0", false)]
    [InlineData("List1.AddItem \"i0\"\nList1.AddItem \"i1\"\nList1.AddItem \"i2\"\nList1.ListIndex = 0\n", "List1.RemoveItem 0", "i1|i2", "", 0, "i1", false)]
    [InlineData("List1.AddItem \"i0\"\nList1.ListIndex = 0\n", "List1.RemoveItem 0", "", "", -1, "", false)]
    public async Task Item_edits_keep_the_position_and_move_checks_with_items_without_events(
        string setup, string statement, string items, string checkedIndexes, int index, string text, bool sorted)
    {
        var (recorder, values) = await Run(setup, $"{statement}\nDebug.Print List1.ListIndex\nDebug.Print List1.Text\nDebug.Print List1.SelCount\n", sorted: sorted);
        var checks = checkedIndexes.Length == 0 ? [] : checkedIndexes.Split('|').Select(int.Parse).ToArray();
        recorder.List.Items.Cast<string>().Should().Equal(items.Length == 0 ? [] : items.Split('|'));
        values.Should().Equal(index, text, checks.Length);
        Enumerable.Range(0, recorder.List.ItemCount).Where(recorder.List.IsItemSelected).Should().Equal(checks);
        recorder.Calls.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData("", "List1.AddItem \"a\"", -1, "")]
    [InlineData(Four + "List1.ListIndex = 2\n", "List1.AddItem \"z\", 0", 3, "i2")]
    [InlineData(Four + "List1.ListIndex = 2\n", "List1.RemoveItem 0", 1, "i2")]
    [InlineData(Four + "List1.ListIndex = 2\n", "List1.RemoveItem 2", -1, "")]
    public async Task Plain_list_keeps_its_existing_item_selection(string setup, string statement, int index, string text)
    {
        var (recorder, values) = await Run(setup, $"{statement}\nDebug.Print List1.ListIndex\nDebug.Print List1.Text\n", style: 0);
        values.Should().Equal(index, text);
        recorder.Calls.Should().BeEmpty();
    }
}
