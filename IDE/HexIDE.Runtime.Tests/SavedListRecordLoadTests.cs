using System;
using System.Collections.Generic;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

// VB6 SP6 IDE measurements in #5147 (oracle PR #5314).
public class SavedListRecordLoadTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = [];
        public List<string> Keys { get; } = [];
        public void LogError(string error) => Errors.Add(error);
        public void LogError(string key, string fallback, params object[] arguments)
        {
            Keys.Add(key);
            LogError(string.Format(fallback, arguments));
        }
    }

    private static readonly byte[] TwoItems = [2, 0, 3, 0, 2, 0, (byte)'a', (byte)'b', 3, 0, (byte)'c', (byte)'d', (byte)'e'];
    private static readonly byte[] TwoValues = [2, 0, 3, 0, 1, 0, (byte)'5', 1, 0, (byte)'6'];
    private static readonly byte[] ThreeItems = [3, 0, .. TwoItems[2..], 1, 0, (byte)'f'];
    private static readonly byte[] ThreeValues = [3, 0, .. TwoValues[2..], 1, 0, (byte)'7'];

    private static (FormDefinition Form, Sink Log) Load(string type, byte[] list, byte[] data, bool listLast,
        string root = "Form", int style = 2)
    {
        var source = "VERSION 5.00\n" + $"Begin VB.{root} Form1\n" + new string('\n', 9)
            + $"Begin VB.{type} G\n"
            + (type == "ComboBox" ? $"Style = {style}\n" : "")
            + $"List = \"Form1.frx\":{(listLast ? data.Length : 0):X4}\n"
            + $"ItemData = \"Form1.frx\":{(listLast ? 0 : list.Length):X4}\nEnd\nEnd\n";
        byte[] bytes = listLast ? [.. data, .. list] : [.. list, .. data];
        var sink = new Sink();
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            source, sink, frxBlobs: FrxDeserializer.Read(bytes, source))!;
        return (form, sink);
    }

    [Theory]
    [InlineData("ListBox", "List", false, 2)]
    [InlineData("ListBox", "List", true, 1)]
    [InlineData("ListBox", "ItemData", false, 3)]
    [InlineData("ComboBox", "List", false, 2)]
    public void End_of_file_cut_recovers_whole_entries_and_logs_the_control_begin(
        string type, string property, bool insideItem, int count)
    {
        byte[] list = property == "ItemData" ? ThreeItems
            : insideItem ? TwoItems[..^1] : [3, 0, .. TwoItems[2..]];
        byte[] data = property == "ItemData" ? [3, 0, .. TwoValues[2..]] : ThreeValues;
        var (form, log) = Load(type, list, data, listLast: property == "List");
        var component = form.Components[1];
        form.LoadedWithErrors.Should().BeTrue();
        log.Errors.Should().Equal($"Line 12: Property {property} in G could not be set.");
        log.Keys.Should().Equal("Str.ProjectLoad.PropertyNotSet");

        var (items, values) = ListItemState.ReadSavedItems(component);
        items.Should().Equal(new[] { "ab", "cde", "f" }[..count]);
        values.Should().Equal(new long[] { 5, 6, 0 }[..count]);
    }

    [Fact]
    public void Save_writes_only_the_recovered_items_and_values()
    {
        var (form, _) = Load("ListBox", [3, 0, .. TwoItems[2..]], ThreeValues, listLast: true);
        form.CanSaveFaithfully.Should().BeTrue();
        var (source, companion) = new FormSerializer().Serialize(form, "Form1.frm");
        var records = FrxDeserializer.Read(companion!, source);
        records.Values.Should().HaveCount(2);
        FrxDeserializer.TryExtractBlob(source.Split('\n').Single(line => line.TrimStart().StartsWith("List ")), records)
            .Should().Equal(TwoItems);
        FrxDeserializer.TryExtractBlob(source.Split('\n').Single(line => line.TrimStart().StartsWith("ItemData ")), records)
            .Should().Equal(TwoValues);
    }

    [Fact]
    public void Whole_records_load_without_errors()
    {
        var (form, log) = Load("ListBox", ThreeItems, ThreeValues, listLast: true);
        log.Errors.Should().BeEmpty();
        form.LoadedWithErrors.Should().BeFalse();
        var (items, values) = ListItemState.ReadSavedItems(form.Components[1]);
        items.Should().Equal("ab", "cde", "f");
        values.Should().Equal(5, 6, 7);
    }

    [Theory]
    [InlineData(false, "Form", "ListBox", 0)]
    [InlineData(true, "UserControl", "ListBox", 0)]
    [InlineData(true, "PropertyPage", "ListBox", 0)]
    [InlineData(true, "Form", "ComboBox", 0)]
    [InlineData(true, "Form", "ComboBox", 1)]
    public void Other_List_cuts_keep_the_existing_refusal(bool listLast, string root, string type, int style)
    {
        var (form, log) = Load(type, [3, 0, .. TwoItems[2..]], ThreeValues, listLast, root, style);
        log.Errors.Should().BeEmpty();
        form.LoadedWithErrors.Should().BeFalse();
        var act = () => ListItemState.ReadSavedItems(form.Components[1]);
        act.Should().Throw<InvalidOperationException>().WithMessage("Property List in G could not be set");
    }
}
