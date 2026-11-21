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
    private static FormDefinition Load(string type, int field, bool sorted, byte[] list, byte[]? data, string code = "")
    {
        var source = ListItemDataTests.Form(type, type == "ComboBox" ? 2 : 0, sorted, data is null
                ? "List = \"Form1.frx\":0000"
                : $"ItemData = \"Form1.frx\":0000\nList = \"Form1.frx\":{data.Length:X4}")
            + "\nPrivate Sub Form_Load()\n" + code + "\nEnd\nEnd Sub\n";
        var project = new ProjectDefinition(VBProjectType.EXE, "SavedLists");
        var form = new FormDeserializer().Deserialize(project, source.ReplaceLineEndings("\r\n"), new Sink(),
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

    public static IEnumerable<object[]> InvalidRecords()
    {
        yield return [new byte[] { 3 }, Record(0, "1", "2", "3")];
        yield return [new byte[] { 3, 0, 2, 0, 97, 98, 3, 0, 99, 100, 101, 4, 0, 102, 103, 104, 105 }, Record(0, "1", "2", "3")];
        yield return [Record(0, "a", "b", "c"), new byte[] { 3, 0, 0, 0, 5, 0, 0, 0, 249, 255, 255, 255, 112, 17, 1, 0 }];
        yield return [Record(0, "a", "b", "c"), Record(0, "1", "2")];
        yield return [Record(0, "a", "b", "c"), Record(0, "1", "x", "3")];
    }

    [AvaloniaTheory]
    [MemberData(nameof(InvalidRecords))]
    public async Task Undecodable_records_refuse_before_showing_a_partial_list(byte[] list, byte[] data)
    {
        foreach (var type in new[] { "ListBox", "ComboBox" })
        {
            var form = Load(type, 0, false, list, data);
            Func<Task> act = () => Run(form);
            await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*#5114*");
        }
    }
}
