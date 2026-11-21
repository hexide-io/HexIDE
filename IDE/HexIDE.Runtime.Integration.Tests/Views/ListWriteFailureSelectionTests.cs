using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using HexIDE.Integration.Tests;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Integration.Tests.Views;

// SP6 native/p-code measurements: https://github.com/hexide-io/HexIDE/issues/5134,
// direct rows in oracle PR #5159; late-bound rows from 1 in oracle PR #5125, and from -1, 0, the last item and the only
// item in #5134's comments (2026-11-21).
public class ListWriteFailureSelectionTests : WindowTestFixture
{
    private static async Task Check(string kind, int style, bool sorted, int selected, int count,
        int index, int expected, string? text = null, string? referenceType = null)
    {
        var form = $"""
            VERSION 5.00
            Begin VB.Form Form1
               Begin VB.{kind} List1
                  Left = 120
                  Top = 120
                  Width = 1500
                  Height = 1200
                  Style = {style}
                  Sorted = {(sorted ? -1 : 0)}
               End
            End
            Attribute VB_Name = "Form1"
            """;
        var (canvas, recorder, ctx, env) = ContainerRuntimeHarness.Recorded(form);
        await ContainerRuntimeHarness.Run(ctx, env,
            "List1.AddItem \"c\"\n" + (count == 1 ? "" : "List1.AddItem \"d\"\nList1.AddItem \"e\"\n") +
            $"List1.ListIndex = {selected}\n");
        recorder.Calls.Clear();
        var printed = await ContainerRuntimeHarness.Run(ctx, env, $"""
            Dim c As {referenceType ?? kind}
            Set c = List1
            On Error Resume Next
            c.List({index}) = "v"
            Debug.Print Err.Number
            Debug.Print Err.Description
            Debug.Print c.ListIndex
            Debug.Print c.ListCount
            """ + (text is null ? "" : "\nDebug.Print c.Text\n"));

        var error = index > count ? 381 : referenceType is null ? -2147024882 : 7;
        var expectedOutput = new[]
        {
            error.ToString(CultureInfo.InvariantCulture),
            error == 381 ? "Invalid property array index" : "Out of memory",
            expected.ToString(CultureInfo.InvariantCulture), count.ToString(CultureInfo.InvariantCulture),
        };
        printed.Select(v => VbDisplay.ToText(v, CultureInfo.InvariantCulture)).Should()
            .Equal(text is null ? expectedOutput : expectedOutput.Append(text));
        ((ItemsControl)ContainerRuntimeHarness.Child(canvas, "List1")).Items.Cast<string>().Should()
            .Equal(count == 1 ? ["c"] : ["c", "d", "e"]);
        recorder.Calls.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(0, false, 0, 1, "d")]
    [InlineData(0, false, 1, 2, "e")]
    [InlineData(1, false, 0, 1, "d")]
    [InlineData(1, false, 1, 2, "e")]
    [InlineData(2, false, 0, 1, "d")]
    [InlineData(2, false, 1, 2, "e")]
    [InlineData(2, true, 0, 1, "d")]
    [InlineData(2, true, 1, 2, "e")]
    public Task Failed_List_write_moves_ComboBox_selection_and_Text_silently(int style, bool sorted,
        int selected, int expected, string text) =>
        Check("ComboBox", style, sorted, selected, 3, -2, expected, text);

    [AvaloniaFact]
    public Task List_minus_three_also_moves_the_selection_silently() =>
        Check("ComboBox", 0, false, 1, 3, -3, 2, "e");

    [AvaloniaTheory]
    [InlineData("Object")]
    [InlineData("Control")]
    [InlineData("Variant")]
    public Task Late_bound_List_write_moves_the_selection_silently_and_raises_seven(string referenceType) =>
        Check("ComboBox", 0, false, 1, 3, -2, 2, "e", referenceType);

    [AvaloniaTheory]
    [InlineData("Object", 0, false, 0, 3, 1, "d")]
    [InlineData("Control", 2, false, 0, 3, 1, "d")]
    [InlineData("Variant", 2, false, 0, 3, 1, "d")]
    [InlineData("Object", 2, true, 0, 3, 1, "d")]
    [InlineData("Object", 2, false, -1, 3, -1, null)]
    [InlineData("Variant", 0, false, 2, 3, 2, null)]
    [InlineData("Object", 2, false, 0, 1, 0, null)]
    public Task Late_bound_List_write_moves_the_selection_as_a_direct_one_does(string referenceType, int style, bool sorted,
        int selected, int count, int expected, string? text) =>
        Check("ComboBox", style, sorted, selected, count, -2, expected, text, referenceType);

    [AvaloniaTheory]
    [InlineData(0, false, -1, 3)]
    [InlineData(0, false, 2, 3)]
    [InlineData(0, false, 0, 1)]
    [InlineData(1, false, -1, 3)]
    [InlineData(1, false, 2, 3)]
    [InlineData(1, false, 0, 1)]
    [InlineData(2, false, -1, 3)]
    [InlineData(2, false, 2, 3)]
    [InlineData(2, false, 0, 1)]
    [InlineData(2, true, -1, 3)]
    [InlineData(2, true, 2, 3)]
    [InlineData(2, true, 0, 1)]
    public Task Failed_List_write_keeps_a_ComboBox_without_a_next_selection(int style, bool sorted,
        int selected, int count) => Check("ComboBox", style, sorted, selected, count, -2, selected);

    [AvaloniaTheory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(2)]
    public Task Failed_List_write_keeps_a_ListBox_selection(int selected) =>
        Check("ListBox", 0, false, selected, 3, -2, selected);

    [AvaloniaTheory]
    [InlineData("ComboBox", 4)]
    [InlineData("ComboBox", 5)]
    [InlineData("ListBox", 4)]
    [InlineData("ListBox", 5)]
    public Task List_write_past_ListCount_keeps_the_selection(string kind, int index) =>
        Check(kind, 0, false, 1, 3, index, 1);
}
