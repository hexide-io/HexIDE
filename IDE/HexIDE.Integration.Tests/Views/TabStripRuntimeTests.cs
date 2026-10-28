using System;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

public class TabStripRuntimeTests
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           ClientHeight = 3015
           ClientWidth = 4560
           Begin MSComctlLib.TabStrip TabStrip1
              Left = 120
              Top = 120
              Width = 5000
              Height = 3000
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static async Task<string[]> Run(string code)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var values = await ContainerRuntimeHarness.Run(ctx, env, "On Error Resume Next\n" + code + "\n");
        return values.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "").ToArray();
    }

    [AvaloniaFact]
    public void Every_saved_TabStrip_opens_for_the_designer()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Join(directory.FullName, "corpus")))
            directory = directory.Parent;
        var paths = Directory.GetFiles(Path.Join(directory!.FullName,
            "corpus", "inbox-controls", "saved", "tabstrip"), "*.frm");
        paths.Should().NotBeEmpty();
        foreach (var path in paths)
        {
            var source = Vb6TextFile.ReadAllText(path);
            var project = new ProjectDefinition(VBProjectType.EXE, "P");
            var form = new FormDeserializer().Deserialize(project, source, new Sink())!;
            foreach (var instance in form.Components.Where(c => c.BaseClass is TabStripComponentClass))
            {
                var component = (TabStripComponentClass)instance.BaseClass;
                component.InstantiateForDesigner(instance).Should().BeOfType<VBTabStrip>();
            }
        }
    }

    [AvaloniaTheory]
    [InlineData("      ImageList = \"ImageList1\"")]
    [InlineData("      BeginProperty Font\n         Name = \"Arial\"\n         Size = 12\n      EndProperty")]
    public void Runtime_only_properties_do_not_refuse_designer_preview(string property)
    {
        var source = Form.Replace("   End", property + "\n   End", StringComparison.Ordinal);
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source, new Sink())!;
        var instance = form.Components.Single(c => c.BaseClass is TabStripComponentClass);
        TabStripComponentClass.Instance.InstantiateForDesigner(instance).Should().BeOfType<VBTabStrip>();
        Action run = () => TabStripComponentClass.Instance.Instantiate(instance);
        run.Should().Throw<NotImplementedException>();
    }

    [AvaloniaFact]
    public async Task A_form_runs_and_can_add_and_select_tabs_from_code()
    {
        var printed = await Run("""
            Debug.Print TypeName(TabStrip1), TabStrip1.Tabs.Count, TabStrip1.SelectedItem.Caption
            Dim t As Object
            Set t = TabStrip1.Tabs.Add(, "second", "Second")
            Set TabStrip1.SelectedItem = t
            Debug.Print TabStrip1.SelectedItem.Caption, t.Selected
            TabStrip1.Tabs(1).Selected = True
            Debug.Print TabStrip1.SelectedItem.Index
            """);
        printed.Should().Equal("TabStrip", "1", "", "Second", "True", "1");
    }

    [AvaloniaFact]
    public async Task Keys_indexes_and_collection_mutations_keep_the_measured_rules()
    {
        var printed = await Run("""
            TabStrip1.Tabs.Clear
            TabStrip1.Tabs.Add , "k", "First"
            TabStrip1.Tabs.Add , "K", "Second"
            TabStrip1.Tabs.Add , , "Third"
            Debug.Print TabStrip1.Tabs("K").Caption, TabStrip1.Tabs(1.5).Caption, TabStrip1.Tabs(2.5).Caption
            TabStrip1.Tabs(2).Selected = True
            TabStrip1.Tabs.Add 2, , "Inserted"
            Debug.Print TabStrip1.SelectedItem.Caption
            TabStrip1.Tabs.Remove 4
            Debug.Print TabStrip1.SelectedItem.Caption
            Dim tab As Object
            For Each tab In TabStrip1.Tabs
                Debug.Print tab.Caption
            Next
            TabStrip1.Tabs.Clear
            Debug.Print TabStrip1.SelectedItem Is Nothing
            """);
        printed.Should().Equal("Second", "Second", "Second", "Inserted", "First", "First", "Inserted", "Second", "True");
    }

    [AvaloniaFact]
    public async Task Code_selection_raises_events_in_order_and_Cancel_still_clicks()
    {
        var printed = await Run("""
            TabStrip1.Tabs.Add , , "Second"
            Set TabStrip1.SelectedItem = TabStrip1.Tabs(2)
            Debug.Print TabStrip1.SelectedItem.Index
            Set TabStrip1.SelectedItem = Nothing
            Set TabStrip1.SelectedItem = TabStrip1.Tabs(2)
            TabStrip1.Tabs(2).Selected = False
            Sub TabStrip1_BeforeClick(Cancel As Integer)
                Debug.Print "Before", TabStrip1.SelectedItem.Index
                Cancel = 1
            End Sub
            Sub TabStrip1_Click()
                Debug.Print "Click", TabStrip1.SelectedItem.Index
            End Sub
            """);
        printed.Should().Equal("Before", "1", "Click", "1", "1", "Click", "2", "Before", "2", "Click", "2");
    }

    [AvaloniaFact]
    public async Task Tab_properties_default_Caption_and_no_Set_selection_are_bound()
    {
        var printed = await Run("""
            TabStrip1.Tabs.Add , "k", "Second"
            TabStrip1.Tabs(2).Key = "renamed"
            TabStrip1.Tabs(2).Caption = "&Mnemonic"
            TabStrip1.Tabs(2).ToolTipText = "Tip"
            TabStrip1.Tabs(2).HighLighted = True
            TabStrip1.Tabs(2).Tag = 42
            TabStrip1.SelectedItem = TabStrip1.Tabs("renamed")
            Dim v
            v = TabStrip1.Tabs(2)
            Debug.Print v, TabStrip1.SelectedItem.ToolTipText, TabStrip1.SelectedItem.HighLighted
            Debug.Print TabStrip1.SelectedItem.Tag, TypeName(TabStrip1.SelectedItem.Tag)
            TabStrip1.Tabs(1) = "Renamed"
            Debug.Print TabStrip1.Tabs(1).Caption
            v = TabStrip1
            Debug.Print Err.Number
            """);
        printed.Should().Equal("&Mnemonic", "Tip", "True", "42", "Integer", "Renamed", "438");
    }

    [AvaloniaFact]
    public void Files_remain_read_only_until_the_faithful_writer_lands()
    {
        var (root, _, _, _) = ContainerRuntimeHarness.Spawn(Form);
        root.Should().NotBeNull();
        var form = new FormDeserializer().Deserialize(
            new ProjectDefinition(VBProjectType.EXE, "Test"),
            Form, new Sink())!;
        form.CanSaveFaithfully.Should().BeFalse();
    }

    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) { }
    }

    [AvaloniaTheory]
    [InlineData("Style", "2", "2")]
    [InlineData("TabWidthStyle", "2", "2")]
    [InlineData("TabStyle", "1", "1")]
    [InlineData("Placement", "3", "3")]
    [InlineData("OLEDropMode", "1", "1")]
    [InlineData("MousePointer", "99", "99")]
    [InlineData("TabFixedWidth", "1500", "1500")]
    [InlineData("MultiRow", "True", "True")]
    [InlineData("MultiSelect", "True", "True")]
    [InlineData("Separators", "True", "True")]
    [InlineData("ShowTips", "False", "False")]
    [InlineData("Enabled", "False", "False")]
    public async Task Measured_strip_properties_can_be_read_and_written(string property, string value, string expected)
    {
        (await Run($"TabStrip1.{property} = {value}\nDebug.Print TabStrip1.{property}")).Should().Equal(expected);
    }

    [AvaloniaTheory]
    [InlineData("TabStrip1.Tabs.Add 0", "35600")]
    [InlineData("TabStrip1.Tabs.Add , \"k\"\nTabStrip1.Tabs.Add , \"k\"", "35602")]
    [InlineData("TabStrip1.Tabs.Add , , , 1", "35613")]
    [InlineData("TabStrip1.Tabs(1).Image = 1", "35613")]
    [InlineData("Set TabStrip1.Tabs(1).Tag = TabStrip1", "35700")]
    [InlineData("Set TabStrip1.Tabs(1).Tag = TabStrip1.Tabs", "35700")]
    [InlineData("Set TabStrip1.Tabs(1).Tag = TabStrip1.Tabs(1)", "35700")]
    [InlineData("TabStrip1.HotTracking = True", "383")]
    [InlineData("TabStrip1.TabFixedHeight = 40000", "6")]
    [InlineData("TabStrip1.Tabs(1).Left = 0", "383")]
    [InlineData("TabStrip1.TabWidthStyle = -1", "380")]
    [InlineData("TabStrip1.Placement = 4", "380")]
    [InlineData("TabStrip1.TabStyle = 3", "380")]
    [InlineData("TabStrip1.OLEDropMode = 3", "380")]
    [InlineData("TabStrip1.TabFixedWidth = -1", "380")]
    [InlineData("TabStrip1.TabFixedHeight = -1", "380")]
    [InlineData("TabStrip1.MousePointer = -1", "380")]
    [InlineData("TabStrip1.ImageList = \"ImageList1\"", "13")]
    [InlineData("TabStrip1.Tabs.Add , \"7\"", "35603")]
    [InlineData("Debug.Print TabStrip1.Tabs(\"3\").Caption", "35601")]
    [InlineData("TabStrip1.Tabs.Count = 3", "383")]
    [InlineData("TabStrip1.Tabs(1).Index = 3", "383")]
    [InlineData("TabStrip1.SelectedItem = 2", "13")]
    [InlineData("TabStrip1.Style = 99", "380")]
    [InlineData("TabStrip1.TabFixedWidth = 40000", "6")]
    [InlineData("TabStrip1.ClientLeft = 0", "383")]
    public async Task Measured_errors_are_trappable(string code, string number)
    {
        (await Run(code + "\nDebug.Print Err.Number")).Should().Equal(number);
    }

    [AvaloniaFact]
    public async Task Strip_properties_and_sticky_MultiRow_are_reachable()
    {
        var printed = await Run("""
            Debug.Print TabStrip1.Style, TypeName(TabStrip1.Style), TabStrip1.MultiRow, TabStrip1.ShowTips
            Debug.Print TabStrip1.TabFixedHeight, TypeName(TabStrip1.TabFixedHeight), TabStrip1.TabMinWidth, TabStrip1.ImageList Is Nothing
            TabStrip1.Placement = 2
            TabStrip1.Placement = 0
            Debug.Print TabStrip1.MultiRow
            TabStrip1.Style = 1
            TabStrip1.MultiSelect = True
            TabStrip1.DeselectAll
            Debug.Print TabStrip1.SelectedItem Is Nothing
            """);
        printed.Should().Equal("0", "Long", "False", "True", "0", "Integer", "566.9291", "True", "True", "True");
    }
}
