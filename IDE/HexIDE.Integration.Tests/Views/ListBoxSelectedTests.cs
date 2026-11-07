using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A ListBox's Selected(i), SelCount and Text (#1395). Every expectation is vb6.exe's, native and p-code agreeing (the
/// oracle's "A ListBox's Selected, SelCount and Text"), on a list holding <c>a</c>, <c>bc</c>, <c>b</c>, <c>c</c> and
/// <c>b</c>, so <c>bc</c> starts with <c>b</c> and <c>b</c> comes twice.
/// </summary>
public class ListBoxSelectedTests : WindowTestFixture
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.ListBox List1
              Left            =   120
              Top             =   120
              Width           =   1300
              Height          =   1200
           End
           Begin VB.ListBox List2
              Height          =   1200
              Left            =   1520
              MultiSelect     =   1  'Simple
              Top             =   120
              Width           =   1300
           End
           Begin VB.ListBox List3
              Height          =   1200
              Left            =   2920
              MultiSelect     =   2  'Extended
              Top             =   120
              Width           =   1300
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private const string FiveItems = "X.AddItem \"a\"\r\nX.AddItem \"bc\"\r\nX.AddItem \"b\"\r\nX.AddItem \"c\"\r\nX.AddItem \"b\"\r\n";

    /// <summary>Records each handler a control raises, with List1's ListIndex at that moment.</summary>
    private sealed class Recorder : Decorator, IModuleExecutionRoot
    {
        public ListBox? List { get; set; }
        public List<string> Calls { get; } = new();
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) =>
            Calls.Add($"{name} li={(List as HexIDE.Runtime.BuiltinControls.VBListBox)?.ListIndex}");
    }

    private sealed record Ran(List<string> Clicks, List<string> Printed);

    /// <summary>
    /// Fills <paramref name="control"/>, runs <paramref name="setup"/>, then <paramref name="code"/>, and returns the
    /// Clicks <paramref name="code"/> raised and what it printed.
    /// </summary>
    private static async Task<Ran> Run(string control, string setup, string code)
    {
        var (root, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var recorder = new Recorder { Child = root, List = (ListBox)ContainerRuntimeHarness.Child(canvas, control) };
        var window = new Window { Content = recorder, Width = 500, Height = 300 };
        ContainerRuntimeHarness.MergeAppResources(window);
        window.Show();
        await ContainerRuntimeHarness.Run(ctx, env, (FiveItems + setup).Replace("X.", control + "."));
        Dispatcher.UIThread.RunJobs();
        recorder.Calls.Clear();
        var printed = await ContainerRuntimeHarness.Run(ctx, env, code.ReplaceLineEndings("\r\n").Replace("X.", control + "."));
        Dispatcher.UIThread.RunJobs();
        return new Ran(recorder.Calls.Where(c => c.StartsWith(control + "_", StringComparison.Ordinal)).ToList(),
            printed.Select(v => VbDisplay.ToText(v, CultureInfo.InvariantCulture)).ToList());
    }

    [AvaloniaTheory]
    [InlineData("X.Selected(0)", "False", "0")]
    [InlineData("X.Selected(5)", "", "381")]
    [InlineData("X.Selected(-1)", "", "381")]
    [InlineData("X.Selected(100)", "", "381")]
    [InlineData("X.Selected(-2)", "", "381")]
    [InlineData("X.Selected(40000)", "", "6")]
    [InlineData("X.Selected(\"1\")", "False", "0")]
    [InlineData("X.Selected(1.6)", "False", "0")]
    public async Task Selected_reads_a_Boolean_and_381_outside_the_list(string expression, string expected, string error)
    {
        var ran = await Run("List1", "", $"On Error Resume Next\nv = Empty\nv = {expression}\nDebug.Print \"\" & v\nDebug.Print Err.Number\n");

        ran.Printed.Should().Equal(expected, error);
    }

    [AvaloniaFact]
    public async Task SelCount_is_an_Integer_and_Text_the_selected_item_s_text_or_nothing()
    {
        var ran = await Run("List1", "",
            "Debug.Print X.SelCount\nDebug.Print TypeName(X.SelCount)\nDebug.Print \"[\" & X.Text & \"]\"\nDebug.Print TypeName(X.Text)\n"
            + "X.Selected(1) = True\nDebug.Print X.SelCount\nDebug.Print X.Text\nDebug.Print X.Selected(1)\n");

        ran.Printed.Should().Equal("0", "Integer", "[]", "String", "1", "bc", "True");
    }

    [AvaloniaTheory]
    [InlineData("", "X.Selected(1) = True", "List1_Click li=1", "0", "1")]
    [InlineData("X.ListIndex = 1", "X.Selected(1) = True", null, "0", "1")]
    [InlineData("X.ListIndex = 1", "X.Selected(3) = True", "List1_Click li=3", "0", "3")]
    [InlineData("X.ListIndex = 3", "X.Selected(3) = False", "List1_Click li=-1", "0", "-1")]
    [InlineData("", "X.Selected(0) = False", null, "0", "-1")]
    [InlineData("", "X.Selected(5) = True", null, "381", "-1")]
    [InlineData("", "X.Selected(-1) = True", null, "381", "-1")]
    [InlineData("", "X.Selected(-1) = False", null, "381", "-1")]
    [InlineData("", "X.Selected(100) = True", null, "381", "-1")]
    [InlineData("", "X.Selected(2) = \"True\"", "List1_Click li=2", "0", "2")]
    [InlineData("X.ListIndex = 2", "X.Selected(2) = 0", "List1_Click li=-1", "0", "-1")]
    [InlineData("", "X.Selected(2) = 5", "List1_Click li=2", "0", "2")]
    [InlineData("X.ListIndex = 2", "X.Selected(2) = Null", null, "94", "2")]
    [InlineData("X.ListIndex = 2", "X.Selected(2) = \"x\"", null, "13", "2")]
    [InlineData("X.Clear", "X.Selected(0) = True", null, "381", "-1")]
    public async Task Selected_selects_or_clears_an_item_with_Click_only_when_the_selection_moves(
        string setup, string statement, string? click, string error, string listIndex)
    {
        var ran = await Run("List1", setup, $"On Error Resume Next\n{statement}\nDebug.Print Err.Number\nDebug.Print X.ListIndex\n");

        ran.Clicks.Should().Equal(click is null ? [] : [click]);
        ran.Printed.Should().Equal(error, listIndex);
    }

    [AvaloniaTheory]
    [InlineData("", "X.Text = \"b\"", "List1_Click li=2", "0")]   // the first whole match: not bc, nor the second b
    [InlineData("", "X.Text = \"B\"", "List1_Click li=2", "0")]   // without regard to case
    [InlineData("", "X.Text = \"x\"", "List1_Click li=-1", "0")]   // no item's text, and Click still comes
    [InlineData("X.ListIndex = 0", "X.Text = \"x\"", "List1_Click li=-1", "0")]
    [InlineData("X.ListIndex = 0", "X.Text = \"\"", "List1_Click li=-1", "0")]
    [InlineData("", "X.Text = \"bc\"", "List1_Click li=1", "0")]
    [InlineData("X.ListIndex = 1", "X.Text = \"bc\"", "List1_Click li=1", "0")]   // already selected, and Click again
    [InlineData("", "X.Text = 5", "List1_Click li=-1", "0")]   // the text 5, which no item has
    [InlineData("X.ListIndex = 3", "X.Text = Null", null, "94")]
    [InlineData("X.Clear", "X.Text = \"a\"", "List1_Click li=-1", "0")]
    public async Task Text_selects_the_first_item_it_names_and_raises_Click_every_time(
        string setup, string statement, string? click, string error)
    {
        var ran = await Run("List1", setup, $"On Error Resume Next\n{statement}\nDebug.Print Err.Number\n");

        ran.Clicks.Should().Equal(click is null ? [] : [click]);
        ran.Printed.Should().Equal(error);
    }

    // On a multi-select list, ListIndex is the focused item, 0 with nothing selected, and Text reads it (#1413). List2 is
    // Simple and List3 Extended, and vb6.exe's two agreed row for row.
    [AvaloniaTheory]
    [InlineData("List2")]
    [InlineData("List3")]
    public async Task A_multi_select_list_s_focus_is_on_its_first_item_with_nothing_selected(string control)
    {
        var ran = await Run(control, "", "Debug.Print X.ListIndex\nDebug.Print X.Text\nDebug.Print X.SelCount\n");

        ran.Printed.Should().Equal("0", "a", "0");
    }

    [AvaloniaTheory]
    [InlineData("List2")]
    [InlineData("List3")]
    public async Task On_a_multi_select_list_Selected_adds_and_removes_an_item_and_moves_the_focus_only_to_add(string control)
    {
        var ran = await Run(control, "",
            "X.Selected(1) = True\nX.Selected(3) = True\nDebug.Print X.SelCount\nX.Selected(3) = False\n"
            + "Debug.Print X.ListIndex\nDebug.Print X.Text\nX.Selected(0) = False\nDebug.Print X.Selected(1)\nDebug.Print X.SelCount\n");

        // Selected(0) = False on an item not selected changes nothing, and raises no Click.
        ran.Clicks.Should().Equal($"{control}_Click li=1", $"{control}_Click li=3", $"{control}_Click li=3");
        ran.Printed.Should().Equal("2", "3", "c", "True", "1");
    }

    [AvaloniaTheory]
    [InlineData("List2")]
    [InlineData("List3")]
    public async Task On_a_multi_select_list_ListIndex_moves_only_the_focus_and_Text_only_raises_Click(string control)
    {
        var ran = await Run(control, "X.Selected(1) = True\r\n",
            "Debug.Print X.ListIndex\nX.ListIndex = -1\nDebug.Print \"[\" & X.Text & \"]\"\nDebug.Print X.SelCount\n"
            + "X.Text = \"b\"\nX.ListIndex = 0\nX.Text = \"x\"\nDebug.Print X.ListIndex\nDebug.Print X.Selected(1)\nX.Clear\nDebug.Print X.ListIndex\n");

        ran.Clicks.Should().Equal($"{control}_Click li=-1", $"{control}_Click li=-1", $"{control}_Click li=0", $"{control}_Click li=0");
        ran.Printed.Should().Equal("1", "[]", "1", "0", "True", "0");
    }

    [AvaloniaFact]
    public async Task MultiSelect_reads_as_an_Integer_and_cannot_be_assigned()
    {
        var ran = await Run("List1", "", "Debug.Print List1.MultiSelect\nDebug.Print List2.MultiSelect\nDebug.Print List3.MultiSelect\nDebug.Print TypeName(List2.MultiSelect)\n");
        ran.Printed.Should().Equal("0", "1", "2", "Integer");

        var act = () => Run("List2", "", "X.MultiSelect = 0\n");
        (await act.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain("Can't assign to read-only property");
    }
}
