using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A ListBox's and a ComboBox's list members: ListCount, List(i), AddItem's index and RemoveItem (#821). Every
/// expectation is vb6.exe's, native and p-code agreeing, and the two controls agreed row for row (the oracle's "A list's
/// members"). Each case starts from <c>c</c>, <c>a</c> and <c>b</c>, added in that order.
/// </summary>
public class ListMembersTests
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.ListBox List1
              Left            =   120
              Top             =   120
              Width           =   2400
              Height          =   1200
           End
           Begin VB.ComboBox Combo1
              Left            =   2700
              Top             =   120
              Width           =   1500
              Height          =   315
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static async Task<(List<string> Printed, ItemsControl List)> Run(string control, string code)
    {
        var (_, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var program = "On Error Resume Next\r\nX.AddItem \"c\"\r\nX.AddItem \"a\"\r\nX.AddItem \"b\"\r\n" + code.ReplaceLineEndings("\r\n");
        var printed = await ContainerRuntimeHarness.Run(ctx, env, program.Replace("X.", control + "."));
        return (printed.Select(v => VbDisplay.ToText(v, CultureInfo.InvariantCulture)).ToList(),
            (ItemsControl)ContainerRuntimeHarness.Child(canvas, control));
    }

    private static string[] Items(ItemsControl list) => list.Items.Cast<string>().ToArray();

    [AvaloniaTheory]
    [InlineData("List1")]
    [InlineData("Combo1")]
    public async Task ListCount_is_an_Integer_counting_the_items(string control)
    {
        var (printed, _) = await Run(control, "Debug.Print X.ListCount\nDebug.Print TypeName(X.ListCount)\nX.Clear\nDebug.Print X.ListCount\n");

        printed.Should().Equal("3", "Integer", "0");
    }

    [AvaloniaTheory]
    [InlineData("List1", "X.List(0)", "c")]
    [InlineData("List1", "X.List(2)", "b")]
    [InlineData("List1", "X.List(\"1\")", "a")]
    [InlineData("List1", "X.List(1.6)", "b")]
    [InlineData("List1", "X.List(3)", "")]
    [InlineData("List1", "X.List(-1)", "")]
    [InlineData("List1", "X.List(-2)", "")]
    [InlineData("List1", "X.List(100)", "")]
    [InlineData("Combo1", "X.List(0)", "c")]
    [InlineData("Combo1", "X.List(3)", "")]
    [InlineData("Combo1", "X.List(-1)", "")]
    public async Task List_reads_an_item_and_reads_nothing_outside_the_list(string control, string expression, string expected)
    {
        var (printed, _) = await Run(control, $"Debug.Print \"[\" & {expression} & \"]\"\nDebug.Print Err.Number\n");

        printed.Should().Equal($"[{expected}]", "0");
    }

    [AvaloniaTheory]
    [InlineData("List1", "X.List(40000)", 6)]
    [InlineData("List1", "X.List(\"q\")", 13)]
    [InlineData("List1", "X.List(Null)", 94)]
    [InlineData("Combo1", "X.List(40000)", 6)]
    public async Task List_converts_its_index_as_an_Integer(string control, string expression, int error)
    {
        var (printed, _) = await Run(control, $"v = {expression}\nDebug.Print Err.Number\n");

        printed.Should().Equal(error.ToString(CultureInfo.InvariantCulture));
    }

    [AvaloniaTheory]
    [InlineData("List1")]
    [InlineData("Combo1")]
    public async Task List_written_replaces_an_item_keeping_the_selection_and_appends_at_ListCount_or_minus_one(string control)
    {
        var (printed, list) = await Run(control,
            "X.ListIndex = 1\nX.List(1) = \"B\"\nDebug.Print X.ListIndex\nX.List(3) = \"d\"\nX.List(-1) = \"z\"\nDebug.Print Err.Number\n");

        printed.Should().Equal("1", "0");
        Items(list).Should().Equal("c", "B", "b", "d", "z");
    }

    [AvaloniaTheory]
    [InlineData("List1", "X.List(10) = \"e\"", 381)]
    [InlineData("List1", "X.List(-2) = \"y\"", -2147024882)]
    [InlineData("Combo1", "X.List(10) = \"e\"", 381)]
    [InlineData("Combo1", "X.List(-2) = \"y\"", -2147024882)]
    public async Task List_written_outside_the_list_is_an_error_and_adds_nothing(string control, string statement, int error)
    {
        var (printed, list) = await Run(control, $"{statement}\nDebug.Print Err.Number\nDebug.Print Err.Description\n");

        printed.Should().Equal(error.ToString(CultureInfo.InvariantCulture), error == 381 ? "Invalid property array index" : "Out of memory");
        Items(list).Should().Equal("c", "a", "b");
    }

    [AvaloniaTheory]
    [InlineData("List1")]
    [InlineData("Combo1")]
    public async Task AddItem_inserts_at_its_index_and_the_selection_follows_its_item(string control)
    {
        var (printed, list) = await Run(control,
            "X.ListIndex = 1\nX.AddItem \"z\", 0\nDebug.Print X.ListIndex\nX.AddItem \"y\", X.ListCount\nX.AddItem \"u\", \"1\"\nDebug.Print X.ListIndex\nDebug.Print Err.Number\n");

        printed.Should().Equal("2", "3", "0");
        Items(list).Should().Equal("z", "u", "c", "a", "b", "y");
    }

    [AvaloniaTheory]
    [InlineData("List1", "X.AddItem \"w\", X.ListCount + 1", 5)]
    [InlineData("List1", "X.AddItem \"v\", -1", 5)]
    [InlineData("List1", "X.AddItem \"x\", 40000", 6)]
    [InlineData("List1", "X.AddItem \"n\", Null", 13)]
    [InlineData("List1", "X.AddItem Null", 94)]
    [InlineData("Combo1", "X.AddItem \"w\", X.ListCount + 1", 5)]
    [InlineData("Combo1", "X.AddItem \"n\", Null", 13)]
    public async Task AddItem_outside_0_to_ListCount_or_with_a_bad_argument_is_an_error_and_adds_nothing(string control, string statement, int error)
    {
        var (printed, list) = await Run(control, $"{statement}\nDebug.Print Err.Number\n");

        printed.Should().Equal(error.ToString(CultureInfo.InvariantCulture));
        Items(list).Should().Equal("c", "a", "b");
    }

    [AvaloniaTheory]
    [InlineData("List1", "X.ListIndex = 2\nX.RemoveItem 0", 1)]
    [InlineData("List1", "X.ListIndex = 0\nX.RemoveItem 0", -1)]
    [InlineData("List1", "X.ListIndex = 1\nX.RemoveItem 2", 1)]
    [InlineData("Combo1", "X.ListIndex = 2\nX.RemoveItem 0", 1)]
    [InlineData("Combo1", "X.ListIndex = 0\nX.RemoveItem 0", -1)]
    public async Task RemoveItem_removes_an_item_and_the_selection_follows_its_item_or_empties_with_it(string control, string statement, int listIndex)
    {
        var (printed, list) = await Run(control, $"{statement}\nDebug.Print X.ListIndex\nDebug.Print Err.Number\n");

        printed.Should().Equal(listIndex.ToString(CultureInfo.InvariantCulture), "0");
        list.Items.Count.Should().Be(2);
    }

    [AvaloniaTheory]
    [InlineData("List1", "X.RemoveItem X.ListCount", 5)]
    [InlineData("List1", "X.RemoveItem -1", 5)]
    [InlineData("List1", "X.RemoveItem 40000", 6)]
    [InlineData("List1", "X.Clear\nX.RemoveItem 0", 5)]
    [InlineData("Combo1", "X.RemoveItem X.ListCount", 5)]
    public async Task RemoveItem_outside_the_list_is_5(string control, string statement, int error)
    {
        var (printed, _) = await Run(control, $"{statement}\nDebug.Print Err.Number\n");

        printed.Should().Equal(error.ToString(CultureInfo.InvariantCulture));
    }

    [AvaloniaTheory]
    [InlineData("List1")]
    [InlineData("Combo1")]
    public async Task Assigning_ListCount_is_VB6_s_compile_error(string control)
    {
        var act = () => Run(control, "X.ListCount = 5\n");

        (await act.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain("Can't assign to read-only property");
    }
}
