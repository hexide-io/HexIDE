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

    public static IEnumerable<object[]> LateBoundErrors()
    {
        foreach (var (type, style) in new[] { ("ListBox", 0), ("ComboBox", 2), ("ComboBox", 0) })
            foreach (var binding in new[] { "Object", "Control", "VB.Control", "Variant", "Object parameter" })
            {
                yield return [type, style, binding, "c.List(-2) = \"v\"", "Err.Description, Items1.ListCount", "7|Out of memory|3"];
                yield return [type, style, binding, "c.ItemData(0) = v", "Items1.ItemData(0)", "13|77"];
                yield return [type, style, binding, "c.NewIndex = 0", "Items1.NewIndex", "383|2"];
                yield return [type, style, binding, "v = c.ItemData", "", "385"];
            }
    }

    // #5117, SP6 native and p-code (oracle PRs #5118 and #5125).
    [AvaloniaTheory]
    [MemberData(nameof(LateBoundErrors))]
    public Task Late_bound_list_members_report_the_measured_runtime_errors(
        string type, int style, string binding, string statement, string readBack, string expected)
    {
        var probe = $"""
            Dim v As Variant
            v = Null
            On Error Resume Next
            {statement}
            Debug.Print Err.Number{(readBack.Length == 0 ? "" : ", " + readBack)}
            """;
        var call = binding == "Object parameter"
            ? $"Probe Items1\nSub Probe(c As Object)\n{probe}\nEnd Sub"
            : $"Dim c As {binding}\nSet c = Items1\n{probe}";
        return Check(type, style, false, """
            Items1.AddItem "c"
            Items1.AddItem "a"
            Items1.AddItem "b"
            Items1.ListIndex = 1
            Items1.ItemData(0) = 77
            """ + "\n" + call, expected.Split('|'));
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

    // Typed variables have the same answers as direct calls in #5117.
    [AvaloniaTheory]
    [InlineData("ListBox", 0)]
    [InlineData("ComboBox", 2)]
    [InlineData("ComboBox", 0)]
    public Task Typed_list_writes_keep_the_direct_runtime_errors(string type, int style) =>
        Check(type, style, false, $$"""
            Dim c As {{type}}
            Set c = Items1
            c.AddItem "a"
            c.ItemData(0) = 77
            Dim v As Variant
            v = Null
            On Error Resume Next
            c.List(-2) = "v"
            Debug.Print Err.Number, Err.Description
            Err.Clear
            c.ItemData(0) = v
            Debug.Print Err.Number, c.ItemData(0)
            """, "-2147024882", "Out of memory", "94", "77");

    public static IEnumerable<object[]> OtherLateBoundErrors()
    {
        var cases = new (string Statement, int Error)[]
        {
            ("col(1).List(-2) = \"v\"", 7),
            ("col(1).ItemData(0) = vn", 13),
            ("col(1).NewIndex = 0", 383),
            ("v = col(1).ItemData", 385),
            ("v = GetO().ItemData", 385),
            ("v = GetC().ItemData", 385),
            ("GetO().List(-2) = \"v\"", 7),
            ("GetC().List(-2) = \"v\"", 7),
            ("v = CallByName(Items1, \"ItemData\", VbGet)", 385),
            ("CallByName Items1, \"List\", VbLet, -2, \"v\"", 7),
            ("CallByName Items1, \"NewIndex\", VbLet, 0", 383),
            ("v = o.List", 385),
            ("o.ListCount = 1", 383),
            ("o.ItemData = 5", 385),
            ("o.List = \"v\"", 385),
            ("v = o.ItemData()", 385),
            ("o.List(-3) = \"v\"", 7),
            ("v = k.List", 385),
            ("k.ListCount = 1", 383)
        };
        foreach (var (type, style) in new[] { ("ListBox", 0), ("ComboBox", 2), ("ComboBox", 0) })
            foreach (var (statement, error) in cases)
                yield return [type, style, statement, error];
    }

    // #5135, SP6 native/p-code: docs/vb6-fidelity-oracle/a-lists-members-listcount-list.md,
    // "Other ways in" (measuring PR #5189), plus #5135's explicit missing-index write directive.
    // State expectations also guard the unchanged values recorded by #5135's main probe.
    [AvaloniaTheory]
    [MemberData(nameof(OtherLateBoundErrors))]
    public Task Other_late_bound_receivers_report_measured_errors(string type, int style, string statement, int error) =>
        Check(type, style, false, $$"""
            Items1.AddItem "c"
            Items1.AddItem "a"
            Items1.AddItem "b"
            Items1.ListIndex = 1
            Items1.ItemData(0) = 77
            Dim col As New Collection
            col.Add Items1
            Dim o As Object
            Set o = Items1
            Dim k As Control
            Set k = Items1
            Dim vn As Variant
            vn = Null
            On Error Resume Next
            {{statement}}
            Debug.Print Err.Number
            Debug.Print Items1.ListCount, Items1.NewIndex, Items1.ItemData(0), Items1.ListIndex
            Function GetO() As Object
                Set GetO = Items1
            End Function
            Function GetC() As Control
                Set GetC = Items1
            End Function
            """, error.ToString(CultureInfo.InvariantCulture), "3", "2", "77",
            type == "ComboBox" && (statement.Contains(".List(", StringComparison.Ordinal)
                || statement.Contains("VbLet, -2", StringComparison.Ordinal)) ? "2" : "1");

    // #5135 guards: direct -3 is measured in #5134; below -3 remains today's unmeasured answer.
    [AvaloniaTheory]
    [InlineData("ListBox", 0)]
    [InlineData("ComboBox", 2)]
    public Task Direct_and_unmeasured_negative_indexes_keep_the_existing_error(string type, int style) =>
        Check(type, style, false, """
            Items1.AddItem "c"
            Items1.AddItem "a"
            Items1.AddItem "b"
            Items1.ListIndex = 1
            Items1.ItemData(0) = 77
            Dim o As Object
            Set o = Items1
            On Error Resume Next
            Items1.List(-3) = "v"
            Debug.Print Err.Number
            Err.Clear
            o.List(-4) = "v"
            Debug.Print Err.Number
            Debug.Print Items1.ListCount, Items1.NewIndex, Items1.ItemData(0)
            """, "-2147024882", "-2147024882", "3", "2", "77");

    [AvaloniaTheory]
    [InlineData("Items1.NewIndex = 0", "Can't assign to read-only property")]
    [InlineData("Items1.ListCount = 1", "Can't assign to read-only property")]
    [InlineData("v = Items1.ItemData", "Argument not optional")]
    public async Task Direct_call_reports_the_measured_compile_error(string code, string message)
    {
        foreach (var list in Lists())
        {
            var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form((string)list[0], (int)list[1], (bool)list[2]));
            Func<Task> act = () => ContainerRuntimeHarness.Run(ctx, env, code);
            await act.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*" + message);

            (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form((string)list[0], (int)list[1], (bool)list[2]));
            var typedCode = $"Dim c As {list[0]}\r\nSet c = Items1\r\n" + code.Replace("Items1.", "c.");
            act = () => ContainerRuntimeHarness.Run(ctx, env, typedCode);
            await act.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*" + message);
        }
    }
}
