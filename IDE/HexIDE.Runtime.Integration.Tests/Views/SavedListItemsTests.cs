using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

// #5106 / #5114, SP6 native and p-code measurements in #5106's corrected comment, oracle PR #5115.
public class SavedListItemsTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) => throw new InvalidOperationException(error);
    }

    private static byte[] Record(int field, params string[] items)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        writer.Write((ushort)items.Length);
        if (items.Length > 0) writer.Write((ushort)field);
        foreach (var item in items)
        {
            writer.Write((ushort)item.Length);
            writer.Write(Encoding.ASCII.GetBytes(item));
        }
        return stream.ToArray();
    }

    // A null data record leaves the ItemData line out, so the List record is cited alone.
    private static FormDefinition Load(string type, int field, bool sorted, byte[] list, byte[]? data, string code = "",
        IDeserializeErrorSink? sink = null)
    {
        var source = ListItemDataTests.Form(type, type == "ComboBox" ? 2 : 0, sorted, data is null
                ? "List = \"Form1.frx\":0000"
                : $"ItemData = \"Form1.frx\":0000\nList = \"Form1.frx\":{data.Length:X4}")
            + "\nPrivate Sub Form_Load()\n" + code + "\nEnd\nEnd Sub\n";
        var project = new ProjectDefinition(VBProjectType.EXE, "SavedLists");
        var form = new FormDeserializer().Deserialize(project, source.ReplaceLineEndings("\r\n"), sink ?? new Sink(),
            frxBlobs: FrxDeserializer.Read([.. data ?? [], .. list], source))!;
        project.AddForm(form);
        return form;
    }

    private static async Task<List<string>> Run(FormDefinition form)
    {
        var printed = new List<string>();
        var errors = new List<Exception>();
        using var stop = new CancellationTokenSource();
        void Faulted(VBWindowContext _, Exception error)
        {
            errors.Add(error);
            stop.Cancel();
        }
        void RuntimeError(VBWindowContext context, VBRunTimeException error) => Faulted(context, error);
        void CompileError(VBWindowContext context, VBCompileErrorException error) => Faulted(context, error);
        VBDebugConsole.Output += printed.Add;
        VBWindowContext.Faulted += Faulted;
        VBWindowContext.RunTimeError += RuntimeError;
        VBWindowContext.CompileError += CompileError;
        VBFormRuntime? window = null;
        try
        {
            await VBLoader.RunForm(form, stop.Token, out window);
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            window?.Close();
            VBDebugConsole.Output -= printed.Add;
            VBWindowContext.Faulted -= Faulted;
            VBWindowContext.RunTimeError -= RuntimeError;
            VBWindowContext.CompileError -= CompileError;
        }
        errors.Should().BeEmpty();
        return printed.Select(line => line.Trim()).ToList();
    }

    [AvaloniaTheory]
    [InlineData("ListBox", 0, false)]
    [InlineData("ListBox", 2, true)]
    [InlineData("ListBox", 9, false)]
    [InlineData("ComboBox", 0, false)]
    [InlineData("ComboBox", 2, true)]
    [InlineData("ComboBox", 9, false)]
    public async Task Saved_items_and_decimal_Longs_load_in_file_order_with_no_selection(string type, int field, bool sorted)
    {
        var items = type == "ListBox" ? new[] { "s2", "s1", "s3" } : new[] { "t1", "t2", "t3" };
        var values = type == "ListBox" ? new[] { "20", "10", "30" } : new[] { "7", "8", "9" };
        var form = Load(type, field, sorted, Record(field, items), Record(99, values), """
            For i = 0 To Items1.ListCount - 1
                Debug.Print Items1.List(i) & "=" & Items1.ItemData(i)
            Next
            Debug.Print Items1.NewIndex
            Debug.Print Items1.ListIndex
            """ + (type == "ComboBox" ? "\nDebug.Print \"[\" & Items1.Text & \"]\"" : "") + """

            Items1.AddItem "n"
            Debug.Print Items1.NewIndex
            Debug.Print Items1.ItemData(Items1.NewIndex)
            """);
        var newIndex = sorted ? "0" : "3";
        (await Run(form)).Should().Equal(items.Zip(values, (item, value) => item + "=" + value)
            .Concat(type == "ComboBox" ? new[] { "-1", "-1", "[]", newIndex, "0" } : new[] { "-1", "-1", newIndex, "0" }));
    }

    [AvaloniaTheory]
    [InlineData("ListBox")]
    [InlineData("ComboBox")]
    public async Task Saved_ItemData_is_decimal_text_including_negative_and_large_values(string type) =>
        (await Run(Load(type, 0, false, Record(0, "ab", "cde", "fghi"), Record(99, "5", "-7", "70000"),
            "For i = 0 To 2\nDebug.Print Items1.ItemData(i)\nNext"))).Should().Equal("5", "-7", "70000");

    // VB6 designer measurement in #5121's supervisor comment (oracle PR #5131).
    [AvaloniaFact]
    public void The_designer_preview_keeps_saved_ListBox_items_in_file_order()
    {
        var form = Load("ListBox", 2, false, Record(2, "s2", "s1"), Record(2, "20", "10"));
        var list = form.Components.First(c => c.BaseClass is ListBoxComponentClass);
        ListBoxComponentClass.Instance.InstantiateForDesigner(list).Should().BeAssignableTo<ItemsControl>()
            .Which.Items.Should().Equal("s2", "s1");
    }

    // Measured on a ListBox only: a List record cited with no ItemData line loads, and every value reads 0.
    [AvaloniaFact]
    public async Task A_List_record_without_an_ItemData_line_loads_with_every_value_0() =>
        (await Run(Load("ListBox", 2, false, Record(2, "ab", "cde", "fghi"), null,
            "For i = 0 To Items1.ListCount - 1\nDebug.Print Items1.List(i) & \"=\" & Items1.ItemData(i)\nNext")))
            .Should().Equal("ab=0", "cde=0", "fghi=0");

    [AvaloniaTheory]
    [InlineData("ListBox")]
    [InlineData("ComboBox")]
    public async Task Empty_records_still_load(string type) =>
        (await Run(Load(type, 0, false, [0, 0], [0, 0], "Debug.Print Items1.ListCount"))).Should().Equal("0");

    [AvaloniaTheory]
    [InlineData("ListBox")]
    [InlineData("ComboBox")]
    public async Task Loading_and_running_preserves_the_populated_companion_bytes_for_save(string type)
    {
        var list = Record(9, "ab", "cde", "fghi");
        var data = Record(99, "5", "-7", "70000");
        var form = Load(type, 9, false, list, data);
        await Run(form);
        var (source, companion) = new FormSerializer().Serialize(form, "Form1.frm");
        var records = FrxDeserializer.Read(companion!, source).Values;
        records.Should().HaveCount(2);
        records.Should().ContainSingle(record => record.SequenceEqual(list));
        records.Should().ContainSingle(record => record.SequenceEqual(data));
    }

    // Part of #5127: SP6 native and p-code measurements, oracle PRs #5130 and #5155.
    public static IEnumerable<object[]> MeasuredRecords()
    {
        var list = Record(0, "ab", "cde", "fghi");
        yield return [list, Record(0, "5", "6"), new[] { "ab=5", "cde=6", "fghi=0" }];
        yield return [list, Record(0, "5", "6", "7", "8"), new[] { "ab=5", "cde=6", "fghi=7" }];
        yield return [list, Record(0, "abc", "6", "7"), new[] { "ab=0", "cde=6", "fghi=7" }];
        yield return [list, Record(0, "", "6", "7"), new[] { "ab=0", "cde=6", "fghi=7" }];
        yield return [list, Record(0, " 5", "6x", "&H10"), new[] { "ab=5", "cde=6", "fghi=0" }];
        yield return [list, Record(0, "3000000000", "-1", "1.5"), new[] { "ab=-1294967296", "cde=-1", "fghi=1" }];
        yield return [list, Record(0, "5", "6", "7").Append((byte)42).ToArray(), new[] { "ab=5", "cde=6", "fghi=7" }];
        yield return [list.Concat(new byte[] { 122, 122 }).ToArray(), Record(0, "5", "6", "7"), new[] { "ab=5", "cde=6", "fghi=7" }];
        yield return [list, Record(0, "+5", "\t6", "x5"), new[] { "ab=5", "cde=6", "fghi=0" }];
        yield return [list, Record(0, "\r7", "+ 8", "- 9"), new[] { "ab=7", "cde=0", "fghi=0" }];
        yield return [list, Record(0, "+-7", "\n8", "--9"), new[] { "ab=0", "cde=8", "fghi=0" }];
    }

    [AvaloniaTheory]
    [MemberData(nameof(MeasuredRecords))]
    public async Task Measured_saved_records_load_as_VB6_reads_them(byte[] list, byte[] data, string[] expected) =>
        (await Run(Load("ListBox", 0, false, list, data,
            "For i = 0 To Items1.ListCount - 1\nDebug.Print Items1.List(i) & \"=\" & Items1.ItemData(i)\nNext")))
            .Should().Equal(expected);

    [AvaloniaFact]
    public async Task Saved_1252_text_keeps_undefined_bytes_as_same_number_characters()
    {
        var oldPage = Vb6TextEncoding.Ansi.CodePage;
        try
        {
            Vb6TextEncoding.SimulateHostCodePage(1252);
            byte[] list = [3, 0, 0, 0, 3, 0, 0x61, 0x81, 0x62, 1, 0, 0xE9, 4, 0, 0x8D, 0x8F, 0x90, 0x9D];
            (await Run(Load("ListBox", 0, false, list, null, """
                For i = 0 To Items1.ListCount - 1
                    For j = 1 To Len(Items1.List(i))
                        Debug.Print AscW(Mid$(Items1.List(i), j, 1))
                    Next
                Next
                """))).Should().Equal("97", "129", "98", "233", "141", "143", "144", "157");
        }
        finally
        {
            Vb6TextEncoding.SimulateHostCodePage(oldPage);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dropdown_list_without_ItemData_keeps_every_item_deliberately(bool sorted) =>
        (await Run(Load("ComboBox", 0, sorted, Record(0, "b", "a", "c"), null,
            "For i = 0 To Items1.ListCount - 1\nDebug.Print Items1.List(i)\nNext"))).Should().Equal("b", "a", "c");

    private sealed class LoadLog : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = [];
        public void LogError(string error) => Errors.Add(error);
    }

    // #5147, VB6 SP6 IDE (oracle PR #5314).
    [AvaloniaFact]
    public async Task A_List_cut_at_the_end_of_the_companion_logs_the_error_and_runs()
    {
        var list = Record(3, "ab", "cde");
        list[0] = 3;
        var log = new LoadLog();
        var form = Load("ListBox", 3, false, list, Record(3, "5", "6", "7"), """
            Debug.Print Items1.ListCount
            For i = 0 To Items1.ListCount - 1
                Debug.Print Items1.List(i) & "=" & Items1.ItemData(i)
            Next
            """, log);
        (await Run(form)).Should().Equal("2", "ab=5", "cde=6");
        log.Errors.Should().ContainSingle().Which.Should().EndWith("Property List in Items1 could not be set.");
    }

    public static IEnumerable<object[]> InvalidRecords()
    {
        yield return [new byte[] { 3 }];
        yield return [new byte[] { 1, 0, 0, 0, 5, 0, 97 }];
        yield return [Record(0, "\v5", "6", "7")];
        yield return [Record(0, " \f5", "6", "7")];
    }

    [AvaloniaTheory]
    [MemberData(nameof(InvalidRecords))]
    public async Task Unmeasured_ItemData_records_still_refuse(byte[] data)
    {
        foreach (var type in new[] { "ListBox", "ComboBox" })
        {
            var form = Load(type, 0, false, Record(0, "a", "b", "c"), data);
            Func<Task> act = () => Run(form);
            await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*#5127*");
        }
    }
}
