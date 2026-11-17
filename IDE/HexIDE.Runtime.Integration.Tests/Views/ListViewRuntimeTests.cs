using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

// Measured on vb6.exe SP6, native and p-code: oracle/in-box-controls-the-listview.md (#1085).
public class ListViewRuntimeTests
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           ClientHeight = 4500
           ClientWidth = 6000
           Begin MSComctlLib.ListView ListView1
              Height = 3000
              Width = 5000
              BorderStyle = 1
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static async Task<string[]> Run(string code)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var printed = await ContainerRuntimeHarness.Run(ctx, env, "On Error Resume Next\n" + code + "\n");
        return printed.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "").ToArray();
    }

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    [AvaloniaFact]
    public void Text_only_designed_ListView_can_be_saved_and_run_again()
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), Form, new NullSink())!;
        var output = new FormSerializer().Serialize(form, "Form1.frm");
        ContainerRuntimeHarness.Spawn(output.frmText).canvas.Children.Should().ContainSingle();
    }

    [AvaloniaFact]
    public void Unresolved_saved_ImageList_binding_refuses()
    {
        Action load = () => ContainerRuntimeHarness.Spawn(Form.Replace("Height = 3000", "Height = 3000\nIcons = \"ImageList1\""));
        load.Should().Throw<NotImplementedException>().WithMessage("*ListView saved ImageList reference*#1085*");
    }

    [AvaloniaFact]
    public async Task Items_have_Long_counts_indexes_and_Text_defaults()
    {
        (await Run("""
            Debug.Print ListView1.ListItems.Count, TypeName(ListView1.ListItems.Count)
            Dim a
            Set a = ListView1.ListItems.Add(, "a", "Alpha")
            Debug.Print a.Text, a.Key, a.Index, TypeName(a.Index), TypeName(a), TypeName(ListView1.ListItems)
            Dim s As String
            s = a
            Debug.Print s, a.Tag, IsEmpty(a.Icon), IsEmpty(a.SmallIcon)
            a.Text = 42
            Debug.Print a.Text, TypeName(a.Text)
            """)).Should().Equal("0", "Long", "Alpha", "a", "1", "Long", "IListItem", "IListItems",
                "Alpha", "", "True", "True", "42", "String");
    }

    [AvaloniaFact]
    public async Task Selection_keeps_the_measured_pointer_and_MultiSelect_traps()
    {
        (await Run("""
            Dim a, b
            Set a = ListView1.ListItems.Add(, , "a")
            Set b = ListView1.ListItems.Add(, , "b")
            Debug.Print a.Selected, b.Selected, ListView1.SelectedItem.Index
            b.Selected = True
            Debug.Print a.Selected, b.Selected
            b.Selected = False
            Debug.Print b.Selected, ListView1.SelectedItem.Index
            Set ListView1.SelectedItem = a
            Set ListView1.SelectedItem = Nothing
            Debug.Print a.Selected, ListView1.SelectedItem Is Nothing
            ListView1.MultiSelect = True
            b.Selected = True
            ListView1.MultiSelect = False
            Debug.Print a.Selected, b.Selected
            """)).Should().Equal("True", "False", "1", "False", "True", "False", "2", "True", "True", "True", "True");
    }

    // #4414, oracle PR #4413: Removing the SelectedItem, native and p-code (R1–R10).
    [AvaloniaTheory]
    [InlineData(1, "ListView1.ListItems(3).Selected = True", "ListView1.ListItems.Remove 3", "k4", "3", "False", "")]
    [InlineData(2, "Set ListView1.SelectedItem = ListView1.ListItems(3)", "ListView1.ListItems.Remove \"k3\"", "k4", "3", "False", "")]
    [InlineData(3, "ListView1.ListItems(5).Selected = True", "ListView1.ListItems.Remove 5", "k4", "4", "False", "")]
    [InlineData(4, "ListView1.ListItems(3).Selected = True\nListView1.ListItems(3).Selected = False", "ListView1.ListItems.Remove 3", "k4", "3", "False", "")]
    [InlineData(5, "ListView1.MultiSelect = True\nListView1.ListItems(1).Selected = True\nListView1.ListItems(3).Selected = True", "ListView1.ListItems.Remove 3", "k4", "3", "False", "k1")]
    [InlineData(6, "ListView1.ListItems(4).Selected = True", "ListView1.ListItems.Remove 2", "k4", "3", "True", "k4")]
    [InlineData(7, "ListView1.ListItems(3).Selected = True", "ListView1.ListItems.Clear", null, null, null, "")]
    [InlineData(8, "ListView1.ListItems(1).Selected = True", "ListView1.ListItems.Remove 1", "k2", "1", "False", "")]
    [InlineData(9, "ListView1.ListItems.Remove 5\nListView1.ListItems.Remove 4\nListView1.ListItems.Remove 3\nListView1.ListItems.Remove 2\nListView1.ListItems(1).Selected = True", "ListView1.ListItems.Remove 1", null, null, null, "")]
    [InlineData(10, "ListView1.ListItems(3).Selected = True", "ListView1.ListItems.Remove 3\nCall ListView1.ListItems.Add(, \"k9\")", "k4", "3", "False", "")]
    public async Task Removing_items_preserves_the_measured_SelectedItem(int row, string before, string remove,
        string? key, string? index, string? selected, string selectedKeys)
    {
        var output = await Run("""
            ListView1.View = 3
            Call ListView1.ListItems.Add(, "k1")
            Call ListView1.ListItems.Add(, "k2")
            Call ListView1.ListItems.Add(, "k3")
            Call ListView1.ListItems.Add(, "k4")
            Call ListView1.ListItems.Add(, "k5")
            """ + "\n" + before + "\n" + remove + "\n" + """
            If ListView1.SelectedItem Is Nothing Then
                Debug.Print "Nothing"
            Else
                Debug.Print ListView1.SelectedItem.Key, ListView1.SelectedItem.Index, ListView1.SelectedItem.Selected
            End If
            Dim i As Long, keys As String
            keys = ""
            For i = 1 To ListView1.ListItems.Count
                If ListView1.ListItems(i).Selected Then keys = keys & ListView1.ListItems(i).Key
            Next
            Debug.Print keys
            """);
        string[] expected = key == null ? ["Nothing", selectedKeys] : [key, index!, selected!, selectedKeys];
        output.Should().Equal(expected, $"measured row R{row}");
    }

    [AvaloniaFact]
    public async Task Checkbox_toggles_preserve_each_items_state()
    {
        (await Run("""
            Dim a, b
            Set a = ListView1.ListItems.Add
            Debug.Print a.Checked
            ListView1.Checkboxes = True
            Set b = ListView1.ListItems.Add
            Debug.Print a.Checked, b.Checked
            a.Checked = False
            ListView1.Checkboxes = False
            ListView1.Checkboxes = True
            Debug.Print a.Checked, b.Checked
            """)).Should().Equal("True", "True", "False", "False", "False");
    }

    [AvaloniaFact]
    public async Task Lookup_rounds_and_removal_invalidates_held_items()
    {
        (await Run("""
            Dim a, b, c
            Set a = ListView1.ListItems.Add(, "a", "a")
            Set b = ListView1.ListItems.Add(, "b", "b")
            Set c = ListView1.ListItems.Add(, "c", "c")
            Debug.Print ListView1.ListItems(1.5).Text, ListView1.ListItems(2.5).Text, ListView1.ListItems("b").Text
            ListView1.ListItems.Remove 1
            Debug.Print c.Index
            Err.Clear
            Debug.Print a.Text
            Debug.Print Err.Number, Err.Description
            ListView1.ListItems.Clear
            Debug.Print ListView1.ListItems.Count
            """)).Should().Equal("b", "b", "b", "2", "35605", "This item's control has been deleted", "0");
    }

    [AvaloniaFact]
    public async Task Named_arguments_insertion_key_changes_and_item_methods_work()
    {
        (await Run("""
            Dim a, b
            Set a = ListView1.ListItems.Add(Key:="a", Text:="Alpha")
            Set b = ListView1.ListItems.Add(Index:=1, Text:="Front")
            Debug.Print a.Index, b.Index, a.EnsureVisible
            a.Key = "new"
            a.Tag = "tag"
            a.Left = 5
            Debug.Print Err.Number, ListView1.ListItems.Item("new").Text, a.Tag
            ListView1.ListItems.Count = 5
            Debug.Print Err.Number
            """)).Should().Equal("2", "1", "True", "0", "Alpha", "tag", "383");
    }

    [AvaloniaTheory]
    [InlineData("Debug.Print ListView1.hWnd", "hWnd")]
    [InlineData("Dim a\nSet a = ListView1.ListItems.Add\nDebug.Print a.CreateDragImage", "CreateDragImage")]
    public async Task Pending_members_refuse_explicitly(string code, string member)
    {
        Func<Task> action = async () => await Run(code);
        await action.Should().ThrowAsync<NotImplementedException>().WithMessage("*" + member + "*#1085*");
    }

    [AvaloniaTheory]
    [InlineData("a.Text = 43", "text conversion")]
    [InlineData("a.Text = 42#", "text conversion")]
    [InlineData("a.Text = CDate(42)", "text conversion")]
    [InlineData("Debug.Print ListView1.ListItems(CDate(2)).Text", "index conversion")]
    [InlineData("a.Tag = 42", "Tag value")]
    [InlineData("ListView1.Checkboxes = 1", "Boolean conversion")]
    [InlineData("Debug.Print ListView1.ListItems(\"A\").Text", "key case comparison")]
    [InlineData("Call ListView1.ListItems.Add(, \"6\")", "numeric ListView key")]
    [InlineData("Call ListView1.ListItems.Add(, \"a\")\nDebug.Print Err.Description", "error description")]
    public async Task Unmeasured_values_refuse_instead_of_guessing(string code, string detail)
    {
        Func<Task> action = async () => await Run("Dim a\nSet a = ListView1.ListItems.Add(, \"a\", \"Alpha\")\n" + code);
        await action.Should().ThrowAsync<NotImplementedException>().WithMessage("*" + detail + "*#1085*");
    }

    [AvaloniaTheory]
    [InlineData("Call ListView1.ListItems.Add(, \"a\")", "35602")]
    [InlineData("Call ListView1.ListItems.Add(, \"5\")", "35603")]
    [InlineData("Call ListView1.ListItems.Add(0)", "35600")]
    [InlineData("Call ListView1.ListItems.Add(99)", "35600")]
    [InlineData("Call ListView1.ListItems.Add(, , , 1)", "35613")]
    [InlineData("Call ListView1.ListItems.Add(, , , , 1)", "35613")]
    [InlineData("Debug.Print ListView1.ListItems(\"missing\").Text", "35601")]
    [InlineData("Debug.Print ListView1.ListItems(\"\").Text", "35603")]
    [InlineData("ListView1.ListItems.Remove 99", "35600")]
    [InlineData("ListView1.ListItems.Remove \"missing\"", "35601")]
    [InlineData("a.Index = 2", "383")]
    [InlineData("a.Width = 2", "383")]
    [InlineData("Set a.Tag = ListView1", "35700")]
    [InlineData("Set ListView1.ListItems = Nothing", "438")]
    public async Task Measured_errors_leave_the_collection_intact(string statement, string error)
    {
        var description = error == "35613" ? "\nDebug.Print Err.Description" : "";
        string[] expected = error == "35613"
            ? [error, "1", "Alpha", "ImageList must be initialized before it can be used"] : [error, "1", "Alpha"];
        (await Run("Dim a\nSet a = ListView1.ListItems.Add(, \"a\", \"Alpha\")\nErr.Clear\n"
            + statement + "\nDebug.Print Err.Number, ListView1.ListItems.Count, a.Text" + description))
            .Should().Equal(expected);
    }
}
