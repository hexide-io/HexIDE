using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;

namespace HexIDE.Integration.Tests.Views;

public class ListViewObjectModelTests
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin MSComctlLib.ListView ListView1
              Height = 3000
              Width = 5000
              BorderStyle = 1
              BackColor = -2147483643
              ForeColor = -2147483640
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static async Task<string[]> Run(string code)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        return (await ContainerRuntimeHarness.Run(ctx, env, "On Error Resume Next\n" + code + "\n"))
            .Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "").ToArray();
    }

    [AvaloniaFact]
    public async Task Columns_have_measured_defaults_types_and_Text_default()
    {
        (await Run("""
            Dim c, s As String
            Set c = ListView1.ColumnHeaders.Add
            Debug.Print ListView1.ColumnHeaders.Count, TypeName(ListView1.ColumnHeaders.Count)
            Debug.Print c.Index, TypeName(c.Index), TypeName(c), TypeName(ListView1.ColumnHeaders)
            Debug.Print c.Text, c.Key, c.Tag, c.Width, c.Alignment, c.Position, c.SubItemIndex, c.Left, c.Icon
            Debug.Print TypeName(c.Position), TypeName(c.SubItemIndex), TypeName(c.Icon)
            c.Text = "Name"
            s = c
            Debug.Print s
            """)).Should().Equal("1", "Long", "1", "Long", "IColumnHeader", "IColumnHeaders",
                "", "", "", "1440", "0", "1", "0", "0", "0", "Integer", "Integer", "Integer", "Name");
    }

    [AvaloniaFact]
    public async Task Column_insertion_position_and_removal_preserve_the_text_column()
    {
        (await Run("""
            Dim a, b, c, front, held
            Set a = ListView1.ColumnHeaders.Add(Key:="name", Text:="Name", Width:=2000)
            Set b = ListView1.ColumnHeaders.Add(, "size", "Size")
            Set c = ListView1.ColumnHeaders.Add(, "kind", "Kind", 1200)
            Debug.Print a.Left, b.Left, c.Left
            c.Position = 1
            Debug.Print a.Position, b.Position, c.Position, a.Index, c.SubItemIndex
            Debug.Print a.Left, b.Left, c.Left
            Set front = ListView1.ColumnHeaders.Add(1, "front", "Front")
            Debug.Print front.SubItemIndex, front.Position, a.Index, a.SubItemIndex
            ListView1.ColumnHeaders.Remove "front"
            Debug.Print front.Text, a.SubItemIndex, c.SubItemIndex
            Set held = ListView1.ColumnHeaders
            Debug.Print held("name").Text
            ListView1.ListItems.Clear
            Debug.Print held.Count
            ListView1.ColumnHeaders.Clear
            Debug.Print held.Count
            """)).Should().Equal("0", "2000.126", "3440.126", "2", "3", "1", "1", "2",
                "0", "2000.126", "3440.126", "0", "1", "2", "1", "Front", "0", "2", "Name", "3", "0");
    }

    [AvaloniaTheory]
    [InlineData("Call ListView1.ColumnHeaders.Add(, \"a\")", "35602")]
    [InlineData("Call ListView1.ColumnHeaders.Add(, \"5\")", "35603")]
    [InlineData("Call ListView1.ColumnHeaders.Add(99)", "35600")]
    [InlineData("Call ListView1.ColumnHeaders.Add(, , , , 3)", "380")]
    [InlineData("Call ListView1.ColumnHeaders.Add(, , , , , 1)", "35613")]
    [InlineData("a.Alignment = 1", "35604")]
    [InlineData("a.Width = -5", "380")]
    [InlineData("a.Index = 2", "383")]
    [InlineData("a.Left = 2", "383")]
    [InlineData("a.SubItemIndex = 1", "382")]
    [InlineData("a.Position = 0", "380")]
    [InlineData("a.Position = 2", "380")]
    [InlineData("ListView1.ColumnHeaders.Count = 2", "383")]
    public async Task Column_errors_are_measured_and_leave_state_intact(string statement, string number)
    {
        (await Run("Dim a\nSet a = ListView1.ColumnHeaders.Add(, \"a\")\n" + statement
            + "\nDebug.Print Err.Number, ListView1.ColumnHeaders.Count, a.Width"))
            .Should().Equal(number, "1", "1440");
    }

    [AvaloniaFact]
    public async Task SubItems_are_column_bounded_but_ListSubItems_are_not()
    {
        (await Run("""
            Dim a, subitem, held, s As String
            Set a = ListView1.ListItems.Add
            a.SubItems(1) = "No columns"
            Debug.Print Err.Number
            ListView1.ColumnHeaders.Add
            ListView1.ColumnHeaders.Add
            ListView1.ColumnHeaders.Add
            Debug.Print a.ListSubItems.Count, TypeName(a.ListSubItems.Count), a.SubItems(1)
            a.SubItems(2) = "Two"
            Debug.Print a.ListSubItems.Count, a.SubItems(1), a.SubItems(2)
            a.SubItems(1) = 42
            Set subitem = a.ListSubItems.Add(, "third", "Three")
            Debug.Print subitem.Index, TypeName(subitem.Index), TypeName(subitem)
            Debug.Print subitem.Key, subitem.Tag, IsEmpty(subitem.ReportIcon), subitem.Bold, subitem.ToolTipText
            s = subitem
            Debug.Print s
            Set held = a.ListSubItems
            Debug.Print held("third").Text
            a.ListSubItems.Remove 1
            Debug.Print a.SubItems(1), a.SubItems(2), subitem.Index
            a.ListSubItems.Clear
            Debug.Print a.ListSubItems.Count
            ListView1.ListItems(1).SubItems(1) = "direct"
            Debug.Print a.SubItems(1), a.ListSubItems.Count
            """)).Should().Equal("380", "0", "Long", "", "2", "", "Two", "3", "Long", "IListSubItem",
                "third", "", "True", "False", "", "Three", "Three", "Two", "Three", "2", "0", "direct", "1");
    }

    [AvaloniaTheory]
    [InlineData("Debug.Print a.SubItems(0)", "380")]
    [InlineData("a.SubItems(2) = \"bad\"", "380")]
    [InlineData("Debug.Print a.SubItems(2)", "380")]
    [InlineData("Call a.ListSubItems.Add(, \"a\")", "35602")]
    [InlineData("Call a.ListSubItems.Add(99)", "35600")]
    [InlineData("Call a.ListSubItems.Add(, , , 1)", "35613")]
    public async Task Subitem_errors_preserve_the_collection(string statement, string number)
    {
        (await Run("""
            Dim a
            ListView1.ColumnHeaders.Add
            ListView1.ColumnHeaders.Add
            Set a = ListView1.ListItems.Add
            a.ListSubItems.Add , "a", "Alpha"
            """ + "\n" + statement + "\nDebug.Print Err.Number, a.ListSubItems.Count, a.SubItems(1)"))
            .Should().Equal(number, "1", "Alpha");
    }

    [AvaloniaFact]
    public async Task Control_defaults_and_item_styles_are_measured()
    {
        (await Run("""
            Debug.Print ListView1.View, TypeName(ListView1.View), ListView1.Arrange, ListView1.LabelEdit
            Debug.Print ListView1.SortOrder, ListView1.TextBackground, ListView1.PictureAlignment
            Debug.Print ListView1.SortKey, TypeName(ListView1.SortKey), ListView1.LabelWrap, ListView1.HideSelection
            Debug.Print ListView1.HideColumnHeaders, ListView1.FullRowSelect, ListView1.GridLines, ListView1.Sorted
            Debug.Print ListView1.AllowColumnReorder, ListView1.FlatScrollBar, ListView1.HotTracking, ListView1.HoverSelection
            Debug.Print ListView1.BorderStyle, ListView1.Appearance, ListView1.BackColor, ListView1.ForeColor
            Debug.Print ListView1.Enabled, ListView1.OLEDragMode, ListView1.OLEDropMode, ListView1.MousePointer
            Debug.Print ListView1.Picture Is Nothing, ListView1.DropHighlight Is Nothing, ListView1.GetFirstVisible Is Nothing
            Debug.Print ListView1.TabStop
            Debug.Print ListView1.Font.Name, ListView1.Font.Size
            Debug.Print ListView1.Icons Is Nothing, ListView1.SmallIcons Is Nothing, ListView1.ColumnHeaderIcons Is Nothing
            Dim a
            Set a = ListView1.ListItems.Add
            Debug.Print a.Ghosted, a.Bold, a.ForeColor, a.ToolTipText
            a.Ghosted = True
            a.Bold = True
            a.ForeColor = vbRed
            ListView1.HideSelection = False
            ListView1.FullRowSelect = True
            ListView1.GridLines = True
            Debug.Print a.Ghosted, a.Bold, a.ForeColor, a.ToolTipText
            Debug.Print ListView1.HideSelection, ListView1.FullRowSelect, ListView1.GridLines
            """)).Should().Equal("0", "Long", "0", "0", "0", "0", "0", "0", "Integer", "True", "True",
                "False", "False", "False", "False", "False", "False", "False", "False", "1", "1",
                "-2147483643", "-2147483640", "True", "0", "0", "0", "True", "True", "True", "True", "MS Sans Serif", "8.25", "True", "True", "True",
                "False", "False", "-2147483640", "", "True", "True", "255", "", "False", "True", "True");
    }

    [AvaloniaTheory]
    [InlineData("LabelWrap")]
    [InlineData("HideSelection")]
    [InlineData("HideColumnHeaders")]
    [InlineData("FullRowSelect")]
    [InlineData("GridLines")]
    [InlineData("AllowColumnReorder")]
    [InlineData("FlatScrollBar")]
    [InlineData("HotTracking")]
    [InlineData("HoverSelection")]
    [InlineData("Enabled")]
    public async Task Boolean_properties_keep_both_values(string name)
    {
        (await Run($"ListView1.{name} = True\nDebug.Print ListView1.{name}\nListView1.{name} = False\nDebug.Print ListView1.{name}"))
            .Should().Equal("True", "False");
    }

    [AvaloniaTheory]
    [InlineData("View", 3)]
    [InlineData("Arrange", 2)]
    [InlineData("LabelEdit", 1)]
    [InlineData("SortOrder", 1)]
    [InlineData("TextBackground", 1)]
    [InlineData("PictureAlignment", 5)]
    public async Task Enum_limits_leave_the_last_valid_value(string name, int maximum)
    {
        (await Run($"ListView1.{name} = {maximum}\nListView1.{name} = {maximum + 1}\nDebug.Print Err.Number, ListView1.{name}"))
            .Should().Equal("380", maximum.ToString(CultureInfo.InvariantCulture));
    }

    [AvaloniaFact]
    public async Task SortKey_must_name_an_existing_column()
    {
        (await Run("""
            ListView1.SortKey = 0
            ListView1.SortKey = 1
            Debug.Print Err.Number, ListView1.SortKey
            ListView1.ColumnHeaders.Add
            ListView1.ColumnHeaders.Add
            ListView1.ColumnHeaders.Add
            ListView1.SortKey = 2
            ListView1.SortKey = 3
            Debug.Print Err.Number, ListView1.SortKey
            ListView1.SortKey = -1
            Debug.Print Err.Number
            ListView1.SortKey = 32767
            Debug.Print Err.Number
            """)).Should().Equal("380", "0", "380", "2", "380", "380");
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Sorting_matches_the_measured_word_order_and_stable_case_ties(int direction)
    {
        string[] input = ["b", "B", "a10", "a2", "10", "9", "", "Zeta", "alpha", "_x", "-y", " space", "a", "A", "ab", "AB", "~t", "1e3", "co-op", "coop", "it's", "its", "eé", "ez", "Ole", "OLE"];
        string[] ascending = ["", " space", "_x", "~t", "10", "1e3", "9", "a", "A", "a10", "a2", "ab", "AB", "alpha", "b", "B", "coop", "co-op", "eé", "ez", "its", "it's", "Ole", "OLE", "-y", "Zeta"];
        string[] descending = ["Zeta", "-y", "Ole", "OLE", "it's", "its", "ez", "eé", "co-op", "coop", "b", "B", "alpha", "ab", "AB", "a2", "a10", "a", "A", "9", "1e3", "10", "~t", "_x", " space", ""];
        var code = string.Join("\n", input.Select(s => $"ListView1.ListItems.Add , , \"{s}\""));
        code += $"\nListView1.SortOrder = {direction}\nListView1.Sorted = True\n";
        code += string.Join("\n", Enumerable.Range(1, input.Length).Select(i => $"Debug.Print ListView1.ListItems({i}).Text"));
        (await Run(code)).Should().Equal(direction == 0 ? ascending : descending);
    }

    [AvaloniaFact]
    public async Task Sorting_uses_subitems_and_only_repeats_on_the_measured_triggers()
    {
        (await Run("""
            Dim a, b
            ListView1.ColumnHeaders.Add
            ListView1.ColumnHeaders.Add
            Set a = ListView1.ListItems.Add(, , "charlie")
            Set b = ListView1.ListItems.Add(, , "alpha")
            a.SubItems(1) = "1"
            b.SubItems(1) = "2"
            ListView1.Sorted = True
            Debug.Print a.Index, b.Index
            ListView1.SortKey = 1
            Debug.Print a.Index, b.Index
            ListView1.Sorted = True
            Debug.Print a.Index, b.Index
            a.Text = "zulu"
            ListView1.Refresh
            Debug.Print a.Index, b.Index
            ListView1.Sorted = False
            Debug.Print a.Index, b.Index
            """)).Should().Equal("2", "1", "2", "1", "1", "2", "1", "2", "1", "2");
        Func<Task> readRefresh = async () => await Run("Debug.Print ListView1.Refresh");
        await readRefresh.Should().ThrowAsync<NotImplementedException>().WithMessage("*Refresh as a value*#1085*");
    }

    [AvaloniaFact]
    public async Task Sorted_add_is_appended_until_the_dispatcher_turn_at_DoEvents()
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var printed = await ContainerRuntimeHarness.Run(ctx, env, """
            ListView1.ListItems.Add , , "charlie"
            ListView1.ListItems.Add , , "alpha"
            ListView1.Sorted = True
            ListView1.ListItems.Add , , "bravo"
            Debug.Print ListView1.ListItems(3).Text
            DoEvents
            Debug.Print ListView1.ListItems(2).Text
            """, new HexIDE.Runtime.EventPump());
        printed.Select(v => v.Value).Should().Equal("bravo", "bravo");
    }

    [AvaloniaFact]
    public async Task FindItem_uses_case_blind_whole_or_prefix_matches_without_wrapping()
    {
        (await Run("""
            Dim a, i
            ListView1.ColumnHeaders.Add
            ListView1.ColumnHeaders.Add
            For i = 1 To 6
                Set a = ListView1.ListItems.Add(, , Choose(i, "Apple", "apricot", "Banana", "app", "Cherry", "apple"))
                a.SubItems(1) = "s" & a.Text
                a.Tag = "t" & a.Text
            Next
            Debug.Print ListView1.FindItem("APPLE").Index
            Debug.Print ListView1.FindItem("ap") Is Nothing
            Debug.Print ListView1.FindItem("ap", 0, , 1).Index
            Debug.Print ListView1.FindItem("app", 0, 5, 1).Index
            Debug.Print ListView1.FindItem("Cherry", 0, 6) Is Nothing
            Debug.Print ListView1.FindItem("sBanana", 1).Index
            Debug.Print ListView1.FindItem("sban", 1, , 1) Is Nothing
            Debug.Print ListView1.FindItem("tCherry", 2).Index
            Debug.Print ListView1.FindItem("", 0, , 1).Index
            Debug.Print ListView1.FindItem("zzz", 0, , 1) Is Nothing
            Debug.Print ListView1.GetFirstVisible.Index
            """)).Should().Equal("1", "True", "1", "6", "True", "3", "True", "5", "1", "True", "1");
        Func<Task> partialTag = async () => await Run("Debug.Print ListView1.FindItem(\"t\", 2, , 1)");
        await partialTag.Should().ThrowAsync<NotImplementedException>().WithMessage("*partial Tag*#1085*");
    }

    [AvaloniaFact]
    public async Task Column_and_subitem_edits_and_known_error_descriptions_work()
    {
        (await Run("""
            Dim a, b, item, subitem
            Set a = ListView1.ColumnHeaders.Add
            Set b = ListView1.ColumnHeaders.Add(, "new", "Size")
            a.Alignment = 1
            Debug.Print Err.Number, Err.Description
            a.SubItemIndex = 1
            Debug.Print Err.Number, Err.Description
            a.Width = 0
            b.Alignment = 2
            Debug.Print a.Width, a.Tag, b.Alignment, ListView1.ColumnHeaders("new").Text
            Set item = ListView1.ListItems.Add
            Set subitem = item.ListSubItems.Add(Key:="new", Text:="Value")
            item.SubItems(1) = 42
            subitem.Bold = True
            Debug.Print subitem.Text, TypeName(subitem.Text), subitem.Key, subitem.Tag, subitem.Bold, subitem.ToolTipText
            Debug.Print ListView1.HitTest(-1, -1) Is Nothing, ListView1.HitTest(100000, 100000) Is Nothing
            """)).Should().Equal("35604", "The first column in a ListView control must be left aligned",
                "382", "Property cannot be set at runtime", "0", "", "2", "Size",
                "42", "String", "new", "", "True", "", "True", "True");
    }

    [AvaloniaTheory]
    [InlineData("a.Width = 999", "Width conversion")]
    [InlineData("ListView1.Sorted = True\nListView1.SortOrder = 1", "SortOrder timing")]
    [InlineData("Debug.Print subitem.ForeColor", "default ForeColor")]
    [InlineData("subitem.ForeColor = 255", "ForeColor values")]
    [InlineData("item.ListSubItems.Remove 1\nDebug.Print subitem.Text", "Removed ListSubItem")]
    [InlineData("ListView1.ListItems.Remove 1\nDebug.Print held.Count", "after removing its owner")]
    [InlineData("ListView1.ColumnHeaders.Remove 1\nDebug.Print a.Index", "Removed ColumnHeader")]
    [InlineData("ListView1.BackColor = 255", "BackColor assignment")]
    [InlineData("ListView1.MouseIcon = 1", "MouseIcon")]
    [InlineData("Set ListView1.Icons = Nothing", "Icons assignment")]
    [InlineData("Set ListView1.SmallIcons = Nothing", "SmallIcons assignment")]
    [InlineData("Set ListView1.ColumnHeaderIcons = Nothing", "ColumnHeaderIcons assignment")]
    [InlineData("ListView1.ListItems.Add , , \"co-op\"\nListView1.ListItems.Add , , \"co'op\"\nListView1.Sorted = True", "punctuated words")]
    public async Task Unmeasured_edges_refuse_explicitly(string code, string detail)
    {
        Func<Task> run = async () => await Run("""
            Dim a, item, subitem, held
            Set a = ListView1.ColumnHeaders.Add
            Set item = ListView1.ListItems.Add
            Set subitem = item.ListSubItems.Add
            Set held = item.ListSubItems
            """ + "\n" + code);
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*" + detail + "*#1085*");
    }

    [AvaloniaTheory]
    [InlineData("column.Text = 42")]
    [InlineData("subitem.Text = 42")]
    [InlineData("Call ListView1.ColumnHeaders.Add(, , 42)")]
    [InlineData("Call item.ListSubItems.Add(, , 42)")]
    [InlineData("column.Key = \"changed\"")]
    [InlineData("column.Tag = \"tag\"")]
    [InlineData("subitem.Key = \"changed\"")]
    [InlineData("subitem.Tag = \"tag\"")]
    [InlineData("item.ToolTipText = \"tip\"")]
    [InlineData("subitem.ToolTipText = \"tip\"")]
    [InlineData("Call item.ListSubItems.Add(, , , , \"tip\")")]
    public async Task Assignments_not_measured_for_these_members_refuse(string statement)
    {
        Func<Task> run = async () => await Run("""
            Dim column, item, subitem
            Set column = ListView1.ColumnHeaders.Add
            Set item = ListView1.ListItems.Add
            Set subitem = item.ListSubItems.Add
            """ + "\n" + statement);
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*unmeasured*#1085*");
    }

    [AvaloniaTheory]
    [InlineData("ListView1.FindItem(\"a\", 3)")]
    [InlineData("ListView1.FindItem(\"a\", 0, 99)")]
    [InlineData("ListView1.FindItem(\"a\", 0, , 2)")]
    public async Task FindItem_rejects_measured_invalid_arguments(string call)
    {
        (await Run("Debug.Print " + call + "\nDebug.Print Err.Number")).Should().Equal("380");
    }
}
