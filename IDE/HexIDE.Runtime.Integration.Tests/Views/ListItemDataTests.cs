using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Integration.Tests.Views;

// SP6 native and p-code, direct calls: #5106's corrected supervisor comment (2026-11-20), measuring PR #5115.
public class ListItemDataTests
{
    internal static string Form(string type, int style, bool sorted, string saved = "") => $$"""
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.{{type}} Items1
              Width = 2400
              Height = 1200
              Style = {{style}}
              Sorted = {{(sorted ? -1 : 0)}}
              {{saved}}
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static async Task Check(string type, int style, bool sorted, string code, params string[] expected)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form(type, style, sorted));
        var printed = await ContainerRuntimeHarness.Run(ctx, env, code.ReplaceLineEndings("\r\n"));
        printed.Select(v => VbDisplay.ToText(v, CultureInfo.InvariantCulture)).Should().Equal(expected);
    }

    public static IEnumerable<object[]> Lists()
    {
        foreach (var (type, style) in new[] { ("ListBox", 0), ("ListBox", 1), ("ComboBox", 0), ("ComboBox", 1), ("ComboBox", 2) })
            foreach (var sorted in new[] { false, true })
                yield return [type, style, sorted];
    }

    [AvaloniaTheory]
    [MemberData(nameof(Lists))]
    public async Task NewIndex_tracks_insertions_replacements_failures_removals_and_Clear(string type, int style, bool sorted)
    {
        await Check(type, style, sorted, """
            Debug.Print Items1.NewIndex
            Debug.Print TypeName(Items1.NewIndex)
            Items1.AddItem "m"
            Debug.Print Items1.NewIndex
            Items1.AddItem "b"
            Debug.Print Items1.NewIndex
            Items1.AddItem "z"
            Debug.Print Items1.NewIndex
            Items1.AddItem "c", 0
            Debug.Print Items1.NewIndex
            Items1.AddItem "d", 2
            Debug.Print Items1.NewIndex
            Items1.AddItem "e", Items1.ListCount
            Debug.Print Items1.NewIndex
            On Error Resume Next
            Items1.AddItem "f", 99
            Debug.Print Err.Number
            Debug.Print Items1.NewIndex
            Err.Clear
            Items1.AddItem "g", -1
            Debug.Print Err.Number
            Debug.Print Items1.NewIndex
            Err.Clear
            Items1.RemoveItem 6
            Debug.Print Err.Number
            Debug.Print Items1.NewIndex
            Err.Clear
            Items1.List(-2) = "v"
            Debug.Print Err.Number
            Debug.Print Items1.NewIndex
            Err.Clear
            Items1.List(Items1.ListCount + 1) = "u"
            Debug.Print Err.Number
            Debug.Print Items1.NewIndex
            Items1.List(0) = "z"
            Items1.List(1) = "q"
            Debug.Print Items1.NewIndex
            Items1.List(Items1.ListCount) = "w"
            Debug.Print Items1.NewIndex
            Items1.List(-1) = "w"
            Debug.Print Items1.NewIndex
            Items1.RemoveItem 0
            Debug.Print Items1.NewIndex
            Items1.Clear
            Debug.Print Items1.NewIndex
            Items1.AddItem "y"
            Debug.Print Items1.NewIndex
            """, "-1", "Integer", "0", sorted ? "0" : "1", "2", "0", "2", "5",
            "5", "5", "5", "5", "5", "5", "-2147024882", "5", "381", "5", "5", "6", "7", "-1", "-1", "0");
        foreach (var index in new[] { 0, 1, 2, 3 })
            await Check(type, style, sorted, $"Items1.AddItem \"a\"\nItems1.AddItem \"b\"\nItems1.AddItem \"c\"\nItems1.AddItem \"d\", 1\nItems1.RemoveItem {index}\nDebug.Print Items1.NewIndex", "-1");
    }

    public static IEnumerable<object[]> Assignments()
    {
        foreach (var list in Lists())
            foreach (var (value, stored, error) in new[] {
                ("5", "5", "0"), ("-1", "-1", "0"), ("2147483647", "2147483647", "0"),
                ("2147483648#", "77", "6"), ("-2147483649#", "77", "6"),
                ("2.5", "2", "0"), ("3.5", "4", "0"), ("\"12\"", "12", "0"), ("\"&H10\"", "16", "0"),
                ("\"x\"", "77", "13"), ("Null", "77", "94"), ("True", "-1", "0"), ("Empty", "0", "0"),
                ("CCur(7.5)", "8", "0"), ("#1/2/2000#", "36527", "0"), ("CByte(200)", "200", "0") })
                yield return [.. list, value, stored, error];
    }

    [AvaloniaTheory]
    [MemberData(nameof(Assignments))]
    public Task ItemData_converts_to_Long_without_overwriting_on_error(string type, int style, bool sorted, string value, string stored, string error) =>
        Check(type, style, sorted, $"Items1.AddItem \"a\"\nItems1.ItemData(0) = 77\nOn Error Resume Next\nItems1.ItemData(0) = {value}\nDebug.Print Err.Number\nDebug.Print Items1.ItemData(0)", error, stored);

    public static IEnumerable<object[]> Indexes()
    {
        foreach (var list in Lists())
            foreach (var (index, result, error) in new[] {
                ("-1", "", "381"), ("-2", "", "381"), ("2", "", "381"), ("0.5", "10", "0"),
                ("1.5", "", "381"), ("\"1\"", "11", "0"), ("32768", "", "6"), ("65536", "", "6"),
                ("Null", "", "94"), ("\"q\"", "", "13") })
                yield return [.. list, index, result, error];
    }

    [AvaloniaTheory]
    [MemberData(nameof(Indexes))]
    public Task ItemData_converts_and_checks_its_index(string type, int style, bool sorted, string index, string result, string error) =>
        Check(type, style, sorted, $"Items1.AddItem \"a\"\nItems1.AddItem \"b\"\nItems1.ItemData(0) = 10\nItems1.ItemData(1) = 11\nOn Error Resume Next\nv = \"\"\nv = Items1.ItemData({index})\nDebug.Print v\nDebug.Print Err.Number", result, error);

    [AvaloniaTheory]
    [MemberData(nameof(Lists))]
    public async Task ItemData_follows_its_item_and_minus_one_append_zeroes_all_values(string type, int style, bool sorted)
    {
        var add = "";
        foreach (var (text, data) in new[] { ("b", 2), ("d", 4), ("c", 3), ("a", 1) })
            add += $"Items1.AddItem \"{text}\"\nItems1.ItemData(Items1.NewIndex) = {data}\n";
        const string dump = "For i = 0 To Items1.ListCount - 1\nDebug.Print Items1.List(i) & \"=\" & Items1.ItemData(i)\nNext\n";
        await Check(type, style, sorted, add + dump + "Items1.AddItem \"x\", 1\n" + dump + "Items1.RemoveItem 2\n" + dump + "Items1.List(0) = \"q\"\n" + dump,
            (sorted ? "a=1 b=2 c=3 d=4|a=1 x=0 b=2 c=3 d=4|a=1 x=0 c=3 d=4|q=1 x=0 c=3 d=4"
                : "b=2 d=4 c=3 a=1|b=2 x=0 d=4 c=3 a=1|b=2 x=0 c=3 a=1|q=2 x=0 c=3 a=1").Replace('|', ' ').Split(' '));
        await Check(type, style, sorted, """
            Items1.AddItem "a"
            Debug.Print Items1.ItemData(0)
            Debug.Print TypeName(Items1.ItemData(0))
            Items1.ItemData(Items1.NewIndex) = 42
            Debug.Print Items1.ItemData(0)
            Items1.ItemData(0) = 10
            Items1.AddItem "b"
            Items1.ItemData(1) = 11
            Items1.AddItem "c"
            Items1.ItemData(2) = 12
            Items1.List(Items1.ListCount) = "w"
            """ + "\n" + dump + "Items1.List(-1) = \"w\"\n" + dump + "Items1.ItemData(0) = 9\nItems1.Clear\nItems1.AddItem \"b\"\nDebug.Print Items1.ItemData(0)",
            "0", "Long", "42", "a=10", "b=11", "c=12", "w=0", "a=0", "b=0", "c=0", "w=0", "w=0", "0");
        foreach (var statement in new[] { "v = Items1.ItemData(0)", "Items1.ItemData(0) = 1" })
            await Check(type, style, sorted, "On Error Resume Next\n" + statement + "\nDebug.Print Err.Number", "381");
        foreach (var index in new[] { -1, 2 })
            await Check(type, style, sorted, $"Items1.AddItem \"a\"\nItems1.AddItem \"b\"\nOn Error Resume Next\nItems1.ItemData({index}) = 1\nDebug.Print Err.Number", "381");
    }

    // ItemData is a Long, so arithmetic on it is Long arithmetic. That's VB's own rule for a Long and an Integer, not
    // measured on a list; the test guards how the value is held.
    [AvaloniaTheory]
    [InlineData("ListBox", 0)]
    [InlineData("ComboBox", 2)]
    public async Task ItemData_reads_take_part_in_Long_arithmetic(string type, int style) =>
        await Check(type, style, false, """
            Items1.AddItem "a"
            Items1.ItemData(0) = 70000
            l = Items1.ItemData(0) + 1
            Debug.Print l
            Debug.Print TypeName(l)
            Debug.Print Items1.ItemData(0) * 2
            Items1.ItemData(0) = Items1.ItemData(0) - 70001
            Debug.Print Items1.ItemData(0)
            """, "70001", "Long", "140000", "-1");

    [AvaloniaTheory]
    [InlineData("Items1.NewIndex = 0", "Can't assign to read-only property")]
    [InlineData("v = Items1.ItemData", "Argument not optional")]
    public async Task Direct_call_reports_the_measured_compile_error(string code, string message)
    {
        foreach (var list in Lists())
        {
            var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form((string)list[0], (int)list[1], (bool)list[2]));
            Func<Task> act = () => ContainerRuntimeHarness.Run(ctx, env, code);
            await act.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*" + message);
        }
    }
}
