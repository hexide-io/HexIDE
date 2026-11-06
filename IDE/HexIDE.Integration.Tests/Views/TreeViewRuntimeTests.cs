using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

// The control-binding slice of #1086; expectations are the oracle's measured TreeView rows.
public class TreeViewRuntimeTests
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           ClientHeight = 3015
           ClientWidth = 4560
           Begin MSComctlLib.TreeView TreeView1
              Height = 1500
              Width = 1500
              Style = 7
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static async Task<string[]> Run(string code, string? form = null)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(form ?? Form);
        using (VbLocale.Use(CultureInfo.GetCultureInfo("en-GB")))
        {
            var values = await ContainerRuntimeHarness.Run(ctx, env, code);
            return values.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "").ToArray();
        }
    }

    // Shared dispatch regression (#2557): the oracle pages setfocus.md, move-scalewidth-and-scaleheight.md
    // and what-zorder-does-at-run-time.md record the generic semantics; no new TreeView values are inferred.
    [AvaloniaFact]
    public async Task SetFocus_reaches_the_running_tree()
    {
        var (root, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var window = new VBFormRuntime { Content = root };
        window.Show();
        try
        {
            await ContainerRuntimeHarness.Run(ctx, env, "TreeView1.SetFocus");
            window.FocusManager!.GetFocusedElement().Should().Be(ContainerRuntimeHarness.Child(canvas, "TreeView1"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Move_sets_the_running_trees_position_and_size()
    {
        var printed = await Run("""
            TreeView1.Move 300, 600, 1800, 2100
            Debug.Print TreeView1.Left, TreeView1.Top, TreeView1.Width, TreeView1.Height
            """);
        printed.Should().Equal("300", "600", "1800", "2100");
    }

    [AvaloniaFact]
    public async Task ZOrder_moves_the_running_tree_among_its_siblings()
    {
        var (_, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var tree = ContainerRuntimeHarness.Child(canvas, "TreeView1");
        var sibling = new VBTextBox();
        canvas.Children.Add(sibling);
        await ContainerRuntimeHarness.Run(ctx, env, "TreeView1.ZOrder 0");
        tree.ZIndex.Should().BeGreaterThan(sibling.ZIndex);
        await ContainerRuntimeHarness.Run(ctx, env, "TreeView1.ZOrder 1");
        tree.ZIndex.Should().BeLessThan(sibling.ZIndex);
    }

    [AvaloniaTheory]
    [InlineData("Refresh")]
    [InlineData("HitTest 0, 0")]
    [InlineData("OLEDrag")]
    public async Task Missing_methods_name_the_member_and_tracking_issue(string call)
    {
        Func<Task> act = () => Run($"TreeView1.{call}");
        (await act.Should().ThrowAsync<NotImplementedException>()).Which.Message
            .Should().Contain(call.Split(' ')[0]).And.Contain("#1086");
    }

    [AvaloniaTheory]
    [InlineData(1200, 1, 0, 4)]
    [InlineData(1200, 1, 1, 4)]
    [InlineData(1200, 0, 0, 5)]
    [InlineData(1200, 0, 1, 4)]
    [InlineData(3000, 1, 0, 12)]
    [InlineData(3000, 1, 1, 12)]
    [InlineData(3000, 0, 0, 12)]
    [InlineData(3000, 0, 1, 12)]
    public async Task Visible_count_uses_the_measured_client_before_and_after_layout(int twips, int appearance, int border, int expected)
    {
        var form = Form.Replace("Height = 1500", $"Height = {twips}")
            .Replace("Style = 7", $"Style = 7\nAppearance = {appearance}\nBorderStyle = {border}");
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(form);
        var before = await ContainerRuntimeHarness.Run(ctx, env, "Debug.Print TreeView1.GetVisibleCount, TypeName(TreeView1.GetVisibleCount)");
        before.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture)).Should().Equal(expected.ToString(CultureInfo.InvariantCulture), "Long");
        var (_, window, laidCtx, laidEnv) = ContainerRuntimeHarness.Laid(form);
        try
        {
            var after = await ContainerRuntimeHarness.Run(laidCtx, laidEnv, """
                Debug.Print TreeView1.GetVisibleCount
                Dim i As Long
                For i = 1 To 30
                    TreeView1.Nodes.Add , , , "node"
                Next
                Debug.Print TreeView1.GetVisibleCount
                """);
            after.Select(v => v.Value).Should().Equal((long)expected, (long)expected);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1200, "4")]
    [InlineData(3000, "12")]
    public void Form_load_reads_capacity_before_show(int twips, string expected)
    {
        var source = Form.Replace("Height = 1500", $"Height = {twips}") + "\n" + """
            Private Sub Form_Load()
                Debug.Print TreeView1.GetVisibleCount
                Debug.Print TypeName(TreeView1.GetVisibleCount)
            End Sub
            """;
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddForm(new FormDeserializer().Deserialize(project, source.ReplaceLineEndings("\r\n"), new NullSink())!);
        var printed = new List<string>();
        VBDebugConsole.Output += printed.Add;
        using var cancel = new CancellationTokenSource();
        try
        {
            _ = VBLoader.RunForm(project.StartupForm!, cancel.Token, out _);
            Dispatcher.UIThread.RunJobs();
            printed.Select(value => value.Trim()).Should().Equal(expected, "Long");
        }
        finally
        {
            cancel.Cancel();
            Dispatcher.UIThread.RunJobs();
            VBDebugConsole.Output -= printed.Add;
        }
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    [AvaloniaFact]
    public async Task Visible_count_is_twelve_long_rows_for_the_oracle_control_even_when_empty()
    {
        var (_, window, ctx, env) = ContainerRuntimeHarness.Laid(Form.Replace("Height = 1500", "Height = 3000"));
        try
        {
            var printed = await ContainerRuntimeHarness.Run(ctx, env, """
                Debug.Print TreeView1.GetVisibleCount, TypeName(TreeView1.GetVisibleCount)
                TreeView1.Nodes.Add , , , "root"
                Debug.Print TreeView1.GetVisibleCount()
                """);
            printed.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture)).Should().Equal("12", "Long", "12");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_form_can_build_and_walk_its_tree()
    {
        var printed = await Run("""
            Dim r As Object, c As Object
            Set r = TreeView1.Nodes.Add(, , "r", "root")
            Set c = TreeView1.Nodes.Add("r", 4, "c", "child")
            Debug.Print TreeView1.Nodes.Count, TreeView1.Nodes("c").Parent.Text
            Debug.Print c.FullPath, TreeView1.Nodes.Item(2).Text
            Debug.Print TreeView1.SelectedItem Is Nothing
            """);
        printed.Should().Equal("2", "root", "root\\child", "child", "True");
    }

    [AvaloniaFact]
    public async Task Own_properties_read_write_and_indentation_stores_whole_himetric()
    {
        FormDeserializer.TryGetComponentClass("MSComctlLib.TreeView", out var component).Should().BeTrue();
        var fresh = (ICSharpPropertyBag)component!.Instantiate(new ComponentInstance(component, "TreeView1"));
        fresh.TryGetProperty("Style", out var style).Should().BeTrue();
        style.Value.Should().Be(7L);

        var printed = await Run("""
            Debug.Print TreeView1.Style, TreeView1.LineStyle, TreeView1.LabelEdit, TreeView1.HideSelection
            Debug.Print TreeView1.Sorted, TreeView1.Checkboxes, TreeView1.PathSeparator
            Debug.Print TreeView1.Indentation
            TreeView1.Style = 3
            TreeView1.LineStyle = 1
            TreeView1.LabelEdit = 1
            TreeView1.HideSelection = False
            TreeView1.PathSeparator = "ab"
            TreeView1.Indentation = 100
            Debug.Print TreeView1.Style, TreeView1.LineStyle, TreeView1.LabelEdit, TreeView1.HideSelection
            Debug.Print TreeView1.PathSeparator, TreeView1.Indentation
            """);
        printed[..7].Should().Equal("7", "0", "0", "True", "False", "False", "\\");
        float.Parse(printed[7], CultureInfo.InvariantCulture).Should().BeApproximately(566.9291f, 0.0001f);
        printed[8..13].Should().Equal("3", "1", "1", "False", "ab");
        float.Parse(printed[13], CultureInfo.InvariantCulture).Should().BeApproximately(99.77953f, 0.00001f);
    }

    [AvaloniaFact]
    public async Task Selection_checkbox_reset_path_and_root_sort_reach_the_owned_model()
    {
        var printed = await Run("""
            Dim b As Object, a As Object, c As Object
            Set b = TreeView1.Nodes.Add(, , "b", "b")
            Set a = TreeView1.Nodes.Add(, , "a", "a")
            Set c = TreeView1.Nodes.Add("b", 4, "c", "c")
            Set TreeView1.SelectedItem = c
            Debug.Print TreeView1.SelectedItem Is c, c.Selected
            Set TreeView1.SelectedItem = Nothing
            Debug.Print TreeView1.SelectedItem Is Nothing, c.Selected
            c.Checked = True
            TreeView1.Checkboxes = True
            Debug.Print c.Checked
            c.Checked = True
            TreeView1.Checkboxes = False
            Debug.Print c.Checked
            TreeView1.Checkboxes = True
            Debug.Print c.Checked
            TreeView1.PathSeparator = "/"
            TreeView1.Sorted = True
            Debug.Print b.Root.Text, c.FullPath, TreeView1.Sorted
            """);
        printed.Should().Equal("True", "True", "True", "False", "False", "True", "False", "a", "b/c", "True");
    }

    [AvaloniaTheory]
    [InlineData("Style", "8")]
    [InlineData("Style", "-1")]
    [InlineData("LineStyle", "2")]
    [InlineData("LineStyle", "-1")]
    [InlineData("LabelEdit", "2")]
    [InlineData("LabelEdit", "-1")]
    [InlineData("Indentation", "-1")]
    public async Task Invalid_choices_raise_380_and_leave_the_property(string property, string value)
    {
        var printed = await Run($"On Error Resume Next\r\nDim old: old = TreeView1.{property}\r\nTreeView1.{property} = {value}\r\nDebug.Print Err.Number, TreeView1.{property} = old");
        printed.Should().Equal("380", "True");
    }

    [AvaloniaFact]
    public async Task Missing_style_loads_as_zero_and_carried_properties_initialize_the_binding()
    {
        var printed = await Run("Debug.Print TreeView1.Style, TreeView1.LineStyle, TreeView1.PathSeparator, TreeView1.HideSelection, TreeView1.Indentation",
            Form.Replace("Style = 7", "LineStyle = 1\nPathSeparator = \"/\"\nHideSelection = 0\nIndentation = 529"));
        printed[..4].Should().Equal("0", "1", "/", "False");
        float.Parse(printed[4], CultureInfo.InvariantCulture).Should().BeApproximately(299.9055f, 0.0001f);
    }

    [AvaloniaFact]
    public async Task Sorted_roots_wait_until_the_dispatcher_turn()
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var before = await ContainerRuntimeHarness.Run(ctx, env, """
            Dim b As Object, a As Object
            Set b = TreeView1.Nodes.Add(, , "b", "b")
            TreeView1.Sorted = True
            Set a = TreeView1.Nodes.Add(, , "a", "a")
            Debug.Print b.Root.Text
            """);
        before.Single().Value.Should().Be("b");
        Dispatcher.UIThread.RunJobs();
        var printed = await ContainerRuntimeHarness.Run(ctx, env, "Debug.Print b.Root.Text");
        printed.Single().Value.Should().Be("a");
    }

    [AvaloniaFact]
    public async Task Nodes_is_a_read_only_object_property()
    {
        var printed = await Run("On Error Resume Next\r\nSet TreeView1.Nodes = Nothing\r\nDebug.Print Err.Number");
        printed.Should().Equal("383");
    }

    [AvaloniaTheory]
    [InlineData("Debug.Print TreeView1.ImageList")]
    [InlineData("Debug.Print TreeView1.HitTest(-1, -1)")]
    [InlineData("TreeView1.FullRowSelect = True")]
    public async Task Later_slices_still_refuse_unimplemented_members(string code)
    {
        Func<Task> act = () => Run(code);
        await act.Should().ThrowAsync<NotImplementedException>();
    }
}
