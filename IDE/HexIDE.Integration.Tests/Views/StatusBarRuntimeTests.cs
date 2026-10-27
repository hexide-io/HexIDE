using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;

namespace HexIDE.Integration.Tests.Views;

// vb6.exe SP6, native and p-code: #1087, issuecomment-6427372779 and the StatusBar oracle entry.
public class StatusBarRuntimeTests
{
    internal const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           ClientHeight = 4500
           ClientWidth = 6000
           Begin MSComctlLib.StatusBar StatusBar1
              Height = 375
              Width = 6000
              Top = 4125
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static async Task<string[]> Run(string code)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var printed = await ContainerRuntimeHarness.Run(ctx, env, "On Error Resume Next\r\n" + code + "\r\n");
        return printed.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "").ToArray();
    }

    [AvaloniaFact]
    public async Task Defaults_and_interface_names_are_measured()
    {
        (await Run("""
            Debug.Print TypeName(StatusBar1), TypeName(StatusBar1.Panels), TypeName(StatusBar1.Panels(1))
            Debug.Print StatusBar1.Style, TypeName(StatusBar1.Style), StatusBar1.SimpleText, StatusBar1.ShowTips
            Debug.Print StatusBar1.Enabled, StatusBar1.MousePointer, StatusBar1.OLEDropMode, StatusBar1.Align, TypeName(StatusBar1.Align)
            Set p = StatusBar1.Panels(1)
            Debug.Print p.Index, p.Key, p.Text, p.Tag, p.ToolTipText, p.Width, p.MinWidth
            Debug.Print p.AutoSize, p.Alignment, p.Style, p.Bevel, p.Enabled, p.Visible
            Debug.Print p.Picture Is Nothing
            Debug.Print StatusBar1.FontName, StatusBar1.FontSize, StatusBar1.Tag, TypeName(StatusBar1.Tag), StatusBar1.ToolTipText
            """)).Should().Equal("StatusBar", "IPanels", "IPanel", "0", "Long", "", "True", "True", "0", "0", "2", "Integer",
                "1", "", "", "", "", "1440", "1440", "0", "0", "0", "1", "True", "True", "True", "MS Sans Serif", "8.25", "", "String", "");
    }

    [AvaloniaFact]
    public async Task Add_remove_clear_and_default_Text_work_through_chains_and_held_objects()
    {
        (await Run("""
            Set ps = StatusBar1.Panels
            ps.Clear
            Set p = ps.Add(, "ready", "Ready")
            Debug.Print p.Index, p.Key, p.Text
            p.Text = 42
            v = p
            Debug.Print v
            StatusBar1.Panels(1) = "changed"
            Debug.Print p.Text
            Set q = ps.Add(1, "first", "first")
            Debug.Print p.Index, ps(1).Text
            ps.Remove "first"
            Debug.Print p.Index, ps.Item("ready").Text
            ps.Clear
            Set q = ps.Add()
            Debug.Print q.Index, q.Width, q.MinWidth, q.Bevel
            """)).Should().Equal("1", "ready", "Ready", "42", "changed", "2", "first", "1", "changed", "1", "1440", "1440", "1");
    }

    [AvaloniaFact]
    public async Task Measured_member_types_and_empty_Count()
    {
        (await Run("""
            Set p = StatusBar1.Panels(1)
            Debug.Print StatusBar1.Panels.Count, TypeName(StatusBar1.Panels.Count), TypeName(p.Index)
            Debug.Print TypeName(p.Width), TypeName(p.MinWidth), TypeName(p.Left)
            Debug.Print TypeName(p.AutoSize), TypeName(p.Alignment), TypeName(p.Bevel), TypeName(p.Style)
            Debug.Print TypeName(p.Enabled), TypeName(p.Visible), TypeName(StatusBar1.ShowTips)
            Debug.Print TypeName(p.Key), TypeName(p.Text), TypeName(p.Tag), TypeName(StatusBar1.SimpleText)
            StatusBar1.Panels.Clear
            Debug.Print StatusBar1.Panels.Count, TypeName(StatusBar1.Panels.Count)
            """)).Should().Equal("1", "Integer", "Integer", "Single", "Single", "Single",
                "Long", "Long", "Long", "Long", "Boolean", "Boolean", "Boolean",
                "String", "String", "String", "String", "0", "Integer");
    }

    [AvaloniaFact]
    public async Task Springs_share_extra_above_MinWidth_and_keep_the_grip()
    {
        (await Run("""
            Set p = StatusBar1.Panels(1)
            Set q = StatusBar1.Panels.Add()
            Set r = StatusBar1.Panels.Add()
            q.AutoSize = 1
            Debug.Print CLng(q.Width * 1000)
            r.AutoSize = 1
            Debug.Print CLng(q.Width * 1000), CLng(r.Width * 1000)
            r.MinWidth = 100
            r.AutoSize = 1
            Debug.Print CLng(q.Width * 1000), CLng(r.Width * 1000)
            r.MinWidth = 3000
            r.AutoSize = 1
            Debug.Print CInt(q.Width), CInt(r.Width)
            q.MinWidth = 500
            Debug.Print CInt(q.Width)
            q.AutoSize = 1
            w = q.Width
            q.AutoSize = 0
            Debug.Print q.Width = w
            """)).Should().Equal("2789858", "2115213", "2115213", "2775118", "1434898", "1440", "3000", "500", "True");
    }

    [AvaloniaFact]
    public async Task Fixed_overflow_positions_and_hidden_panels_are_measured()
    {
        (await Run("""
            Set p = StatusBar1.Panels(1)
            Set q = StatusBar1.Panels.Add()
            Set r = StatusBar1.Panels.Add()
            p.Width = 5000
            q.Width = 5000
            r.AutoSize = 1
            Debug.Print CInt(p.Width), CInt(q.Width), CInt(r.Left), CInt(r.Width)
            p.Visible = False
            Debug.Print CInt(q.Left)
            """)).Should().Equal("5000", "5000", "10035", "1440", "15");
    }

    [AvaloniaTheory]
    [InlineData("StatusBar1.Panels.Add , \"a\": StatusBar1.Panels.Add , \"a\"", "35602")]
    [InlineData("StatusBar1.Panels.Add , \"5\"", "35603")]
    [InlineData("StatusBar1.Panels.Add 0", "35600")]
    [InlineData("StatusBar1.Panels.Add 3", "35600")]
    [InlineData("StatusBar1.Panels.Add , , , 8", "380")]
    [InlineData("Set p = StatusBar1.Panels(\"missing\")", "35601")]
    [InlineData("Set p = StatusBar1.Panels(\"5\")", "35601")]
    [InlineData("Set p = StatusBar1.Panels(\"\")", "35603")]
    [InlineData("Set p = StatusBar1.Panels(0)", "35600")]
    [InlineData("Set p = StatusBar1.Panels(99)", "35600")]
    [InlineData("StatusBar1.Panels.Remove \"missing\"", "35601")]
    [InlineData("StatusBar1.Panels.Count = 1", "383")]
    [InlineData("Set StatusBar1.Panels = Nothing", "383")]
    [InlineData("StatusBar1.Style = 2", "380")]
    [InlineData("x = StatusBar1.TabStop", "438")]
    [InlineData("x = StatusBar1.Index", "343")]
    public async Task Collection_and_control_errors_are_measured(string code, string error)
    {
        (await Run(code + "\r\nDebug.Print Err.Number")).Should().Equal(error);
    }

    [AvaloniaTheory]
    [InlineData("Alignment = 3", "380")]
    [InlineData("Bevel = 3", "380")]
    [InlineData("Style = 8", "380")]
    [InlineData("AutoSize = 3", "380")]
    [InlineData("Width = 0", "380")]
    [InlineData("Width = -5", "380")]
    [InlineData("MinWidth = 0", "380")]
    [InlineData("MinWidth = -5", "380")]
    [InlineData("Left = 100", "383")]
    [InlineData("Index = 1", "383")]
    public async Task Panel_errors_are_measured(string assignment, string error)
    {
        (await Run("Set p = StatusBar1.Panels(1)\r\np." + assignment + "\r\nDebug.Print Err.Number"))
            .Should().Equal(error);
    }

    [AvaloniaFact]
    public async Task Lookup_rounds_half_to_even_and_Key_changes_check_duplicates()
    {
        (await Run("""
            StatusBar1.Panels.Add , "second", "two"
            StatusBar1.Panels.Add , "third", "three"
            Debug.Print StatusBar1.Panels(1.5).Text, StatusBar1.Panels(2.5).Text
            Set p = StatusBar1.Panels(1)
            p.Key = "second"
            Debug.Print Err.Number, p.Key
            """)).Should().Equal("two", "two", "35602", "");
    }

    [AvaloniaFact]
    public async Task MinWidth_sets_Width_both_ways_and_fixed_Width_ignores_MinWidth()
    {
        (await Run("""
            Set p = StatusBar1.Panels(1)
            p.MinWidth = 2500
            Debug.Print CInt(p.Width), CInt(p.MinWidth)
            p.Width = 1000
            StatusBar1.Refresh
            Debug.Print CInt(p.Width), CInt(p.MinWidth)
            p.MinWidth = 500
            Debug.Print CInt(p.Width), CInt(p.MinWidth)
            p.Width = 2000
            Debug.Print CLng(p.Width * 1000)
            """)).Should().Equal("2500", "2500", "1000", "2500", "500", "500", "2000126");
    }

    [AvaloniaFact]
    public async Task Style_keeps_Text_and_simple_mode_keeps_panels()
    {
        (await Run("""
            Set p = StatusBar1.Panels(1)
            p.Text = String(300, "x")
            p.Style = 7
            p.Alignment = 2
            p.Bevel = 2
            p.Enabled = False
            p.Visible = False
            p.ToolTipText = "tip"
            p.Tag = "tag"
            StatusBar1.Style = 1
            StatusBar1.SimpleText = "simple"
            StatusBar1.ShowTips = False
            Debug.Print Len(p.Text), p.Style, p.Alignment, p.Bevel, p.Enabled, p.Visible, p.ToolTipText, p.Tag
            Debug.Print StatusBar1.Style, StatusBar1.SimpleText, StatusBar1.ShowTips, TypeName(StatusBar1.Panels(1))
            """)).Should().Equal("300", "7", "2", "2", "False", "False", "tip", "tag", "1", "simple", "False", "IPanel");
    }

    [AvaloniaFact]
    public async Task AutoSize_recomputes_an_empty_contents_panel_and_fixed_keeps_that_width()
    {
        (await Run("""
            Set p = StatusBar1.Panels(1)
            p.Width = 2000
            p.AutoSize = 2
            Debug.Print p.AutoSize, p.Width
            p.AutoSize = 0
            Debug.Print p.Width
            """)).Should().Equal("2", "1440", "1440");
    }

    [AvaloniaFact]
    public async Task A_hidden_panel_takes_no_space()
    {
        (await Run("""
            Set p = StatusBar1.Panels(1)
            Set q = StatusBar1.Panels.Add()
            x = p.Left
            p.Visible = False
            Debug.Print q.Left = x
            """)).Should().Equal("True");
    }

    [AvaloniaTheory]
    [InlineData("x = StatusBar1.hWnd", "*hWnd*")]
    [InlineData("StatusBar1.Panels.Add , \"a\": Set p = StatusBar1.Panels(\"A\")", "*key case*")]
    [InlineData("Set p = StatusBar1.Panels(1): StatusBar1.Panels.Clear: x = p.Text", "*removed panel*")]
    [InlineData("StatusBar1.MousePointer = 1", "*assignment*not been measured*")]
    [InlineData("Set p = StatusBar1.Panels(1): p.AutoSize = 1.5", "*enum conversion*")]
    public async Task Unmeasured_members_refuse_instead_of_guessing(string code, string message)
    {
        var act = () => Run(code);
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage(message);
    }

    [AvaloniaFact]
    public async Task Saved_panels_load_HIMETRIC_lengths_and_their_properties()
    {
        var frm = Form.Replace("Top = 4125", """
            Top = 4125
            BeginProperty Panels
               NumPanels = 2
               BeginProperty Panel1
                  Object.Width = 1000
                  MinWidth = 2000
                  Text = "Ready"
                  TextSave = "old display"
                  Key = "status"
                  Object.Tag = "t1"
                  Object.ToolTipText = "tip1"
                  Alignment = 1
                  Bevel = 2
                  Enabled = 0
               EndProperty
               BeginProperty Panel2
                  Style = 6
                  Object.Visible = 0
                  TextSave = "26/10/2026"
               EndProperty
            EndProperty
            """);
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(frm);
        var printed = await ContainerRuntimeHarness.Run(ctx, env, """
            Set p = StatusBar1.Panels(1)
            Debug.Print StatusBar1.Panels.Count, p.Text, p.Key, p.Tag, p.ToolTipText
            Debug.Print CLng(p.Width * 2540 / 1440), CLng(p.MinWidth * 2540 / 1440)
            Debug.Print p.Alignment, p.Bevel, p.Enabled
            Debug.Print StatusBar1.Panels(2).Style, StatusBar1.Panels(2).Visible, StatusBar1.Panels(2).Text
            """);
        printed.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "")
            .Should().Equal("2", "Ready", "status", "t1", "tip1", "1000", "2000", "1", "2", "False", "6", "False", "");
    }

    [AvaloniaTheory]
    [InlineData("statusbar", "4", "Ready")]
    [InlineData("statusbar-picture", "3", "red")]
    public async Task Measured_saved_forms_run_with_their_saved_panels(string folder, string count, string text)
    {
        var at = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (at != null && !System.IO.Directory.Exists(System.IO.Path.Join(at.FullName, "corpus"))) at = at.Parent;
        at.Should().NotBeNull();
        var root = System.IO.Path.Join(at!.FullName, "corpus", "inbox-controls");
        var path = System.IO.Path.Join(root, "saved", folder, "Form1.frm");
        if (!System.IO.File.Exists(path)) path = System.IO.Path.Join(root, "pending", folder, "Form1.frm");
        var source = System.IO.File.ReadAllText(path);
        var companion = System.IO.Path.ChangeExtension(path, ".frx");
        var blobs = System.IO.File.Exists(companion)
            ? HexIDE.Runtime.Serialization.FrxDeserializer.Read(System.IO.File.ReadAllBytes(companion), source) : null;
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(source, blobs);
        var printed = await ContainerRuntimeHarness.Run(ctx, env,
            "Debug.Print StatusBar1.Panels.Count, StatusBar1.Panels(1).Text");
        printed.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "").Should().Equal(count, text);
        if (folder == "statusbar-picture")
        {
            var pictures = await ContainerRuntimeHarness.Run(ctx, env, """
                Set p = StatusBar1.Panels(1).Picture
                Debug.Print p Is Nothing
                Set StatusBar1.Panels(2).Picture = p
                Debug.Print StatusBar1.Panels(2).Picture Is Nothing
                Set q = StatusBar1.Panels.Add(, , , , p)
                Debug.Print q.Picture Is Nothing
                Set StatusBar1.Panels(1).Picture = Nothing
                Debug.Print StatusBar1.Panels(1).Picture Is Nothing
                """);
            pictures.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture)).Should().Equal("False", "False", "False", "True");
            foreach (var member in new[] { "Type", "Width", "Height", "Handle" })
            {
                var readMember = () => ContainerRuntimeHarness.Run(ctx, env,
                    $"Debug.Print StatusBar1.Panels(2).Picture.{member}");
                await readMember.Should().ThrowAsync<NotImplementedException>().WithMessage($"*Panel.Picture.{member}*");
            }
            var typeName = () => ContainerRuntimeHarness.Run(ctx, env,
                "Debug.Print TypeName(StatusBar1.Panels(2).Picture)");
            await typeName.Should().ThrowAsync<NotImplementedException>().WithMessage("*TypeName*unmeasured*");
        }
    }

    [AvaloniaFact]
    public async Task Nothing_clears_a_panel_picture_and_is_accepted_by_Add()
    {
        (await Run("""
            Set p = StatusBar1.Panels(1)
            Set p.Picture = Nothing
            Set q = StatusBar1.Panels.Add(, , , , Nothing)
            Debug.Print p.Picture Is Nothing, q.Picture Is Nothing
            """)).Should().Equal("True", "True");
    }

    [AvaloniaFact]
    public async Task Align_top_and_bottom_keep_a_settable_Height()
    {
        (await Run("""
            StatusBar1.Align = 1
            Debug.Print StatusBar1.Top
            StatusBar1.Height = 600
            StatusBar1.Align = 2
            Debug.Print StatusBar1.Height, StatusBar1.Top
            StatusBar1.Align = 0
            Debug.Print StatusBar1.Top
            """)).Should().Equal("0", "600", "3900", "3900");
    }

    [AvaloniaFact]
    public async Task A_panel_refuses_a_circular_Tag()
    {
        (await Run("Set p = StatusBar1.Panels(1)\r\nSet p.Tag = StatusBar1\r\nDebug.Print Err.Number, p.Tag"))
            .Should().Equal("35700", "");
    }
}
