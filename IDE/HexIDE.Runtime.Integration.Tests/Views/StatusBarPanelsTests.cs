using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Integration.Tests;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

// #4882, SP6 native and p-code, MSCOMCTL.OCX 6.01.9782.
public class StatusBarPanelsTests
{
    private static readonly string Form = StatusBarRuntimeTests.Form.Replace("   Begin MSComctlLib.StatusBar StatusBar1", """
               Begin VB.Label Label1
                  Caption = "Label1"
               End
               Begin MSComctlLib.StatusBar StatusBar2
                  Height = 375
                  Width = 6000
               End
               Begin MSComctlLib.StatusBar StatusBar1
            """);

    private static async Task<string[]> Run(string code)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var printed = await ContainerRuntimeHarness.Run(ctx, env, """
            Dim ps As Object, p As Object, q As Object, c As Object, n As Object
            On Error Resume Next
            Set ps = StatusBar1.Panels
            ps.Clear
            Set p = ps.Add(, "one", "one")
            Err.Clear
            """ + "\r\n" + code + "\r\n");
        return printed.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "").ToArray();
    }

    [AvaloniaTheory]
    [InlineData("ps.Add 1, \"k\", \"t\", 0, Nothing, 6", "450")]
    [InlineData("Set q = ps.Item(1, 2)", "450")]
    [InlineData("Set q = ps(1, 2)", "450")]
    [InlineData("ps.Remove 1, 2", "450")]
    [InlineData("ps.Clear 1", "450")]
    [InlineData("Set q = ps.Item()", "450")]
    [InlineData("ps.Remove", "450")]
    [InlineData("x = ps.Count(1)", "451")]
    public async Task Argument_counts_leave_the_collection_intact(string call, string error)
    {
        (await Run($"""
            {call}
            Debug.Print Err.Number, ps.Count, ps(1).Key
            If Err.Number = 451 Then Debug.Print Err.Description
            """)).Should().Equal(error == "451"
                ? [error, "1", "one", "Property let procedure not defined and property get procedure did not return an object"]
                : [error, "1", "one"]);
    }

    [AvaloniaTheory]
    [InlineData("\"abc\"", "\"ABC\"")]
    [InlineData("ChrW(233)", "ChrW(201)")]
    [InlineData("\"i\"", "\"I\"")]
    [InlineData("\"ss\"", "ChrW(223)")]
    public async Task Keys_are_ordinal(string first, string other)
    {
        (await Run($"""
            Set p = ps.Add(, {first})
            Set q = ps({other})
            Debug.Print Err.Number
            Err.Clear
            ps.Remove {other}
            Debug.Print Err.Number, ps.Count
            Err.Clear
            Set q = ps.Add(, {other})
            Debug.Print Err.Number, ps.Count, q.Key = {other}
            q.Key = "second"
            q.Key = {other}
            Debug.Print Err.Number, q.Key = {other}
            q.Key = "second"
            p.Key = {other}
            Debug.Print Err.Number, p.Key = {other}
            """)).Should().Equal("35601", "35601", "2", "0", "3", "True", "0", "True", "0", "True");
    }

    [AvaloniaTheory]
    [InlineData("CDate(2)", "0", "0")]
    [InlineData("CCur(2)", "0", "0")]
    [InlineData("CDec(2)", "0", "0")]
    [InlineData("CByte(2)", "0", "0")]
    [InlineData("CLng(2)", "0", "0")]
    [InlineData("CSng(2.5)", "0", "0")]
    [InlineData("CSng(1.5)", "0", "0")]
    [InlineData("CCur(2.5)", "0", "0")]
    [InlineData("CDec(2.5)", "0", "0")]
    [InlineData("True", "35600", "35600")]
    [InlineData("False", "35600", "35600")]
    [InlineData("Empty", "35601", "35600")]
    [InlineData("Null", "35603", "13")]
    [InlineData("1E+10", "35603", "13")]
    [InlineData("-1E+10", "35603", "13")]
    [InlineData("2147483647.5", "35603", "13")]
    [InlineData("CVErr(2)", "35601", "13")]
    [InlineData("n", "35603", "13")]
    [InlineData("Array(2)", "35603", "13")]
    public async Task Index_conversion(string value, string itemError, string addError)
    {
        (await Run($"""
            ps.Add , "two"
            ps.Add , "three"
            Set q = ps({value})
            Debug.Print Err.Number
            If Err.Number = 0 Then Debug.Print q.Key
            Err.Clear
            ps.Remove {value}
            Debug.Print Err.Number, ps.Count, ps(2).Key
            If Err.Number = 0 Then ps.Add 2, "two"
            Err.Clear
            ps.Add {value}, "new"
            Debug.Print Err.Number, ps.Count, ps(2).Key
            """)).Should().Equal(itemError == "0"
                ? ["0", "two", "0", "2", "three", "0", "4", "new"]
                : [itemError, itemError, "3", "two", addError, "3", "two"]);
    }

    [AvaloniaTheory]
    [InlineData("1.5", "0", "2")]
    [InlineData("CSng(1.5)", "0", "2")]
    [InlineData("CCur(1.5)", "0", "2")]
    [InlineData("2.5", "0", "2")]
    [InlineData("0.5", "0", "0")]
    [InlineData("\"1\"", "0", "1")]
    [InlineData("\" 1 \"", "0", "1")]
    [InlineData("\"&H1\"", "0", "1")]
    [InlineData("CCur(1)", "0", "1")]
    [InlineData("CDec(1)", "0", "1")]
    [InlineData("False", "0", "0")]
    [InlineData("Empty", "0", "0")]
    [InlineData("True", "380", "1")]
    [InlineData("-1", "380", "1")]
    [InlineData("\"abc\"", "13", "1")]
    [InlineData("Null", "13", "1")]
    [InlineData("CVErr(1)", "13", "1")]
    [InlineData("n", "13", "1")]
    [InlineData("2147483648#", "6", "1")]
    [InlineData("-2147483649#", "6", "1")]
    public async Task Enum_conversion(string value, string error, string readback)
    {
        var code = string.Join("\r\n", new[] { "Style", "Alignment", "Bevel", "AutoSize" }.Select(member => $"""
            p.{member} = 1
            Err.Clear
            p.{member} = {value}
            Debug.Print Err.Number, p.{member}
            """));
        (await Run(code + "\r\n" + $"""
            Err.Clear
            Set q = ps.Add(, "new", , {value})
            Debug.Print Err.Number, ps.Count
            If Err.Number = 0 Then Debug.Print q.Style
            """)).Should().Equal(Enumerable.Repeat(new[] { error, readback }, 4).SelectMany(row => row)
                .Concat(error == "0" ? ["0", "2", readback] : [error == "6" ? "13" : error, "1"]));
    }

    [AvaloniaFact]
    public async Task Tag_keeps_an_object_and_can_be_replaced()
    {
        (await Run("""
            Set c = New Collection
            c.Add 1
            Set p.Tag = c
            Debug.Print p.Tag Is c
            Set c = Nothing
            Debug.Print p.Tag.Count
            x = p.Tag
            Debug.Print Err.Number
            Err.Clear
            p.Tag = StatusBar1
            Debug.Print Err.Number
            p.Tag = "s"
            Debug.Print p.Tag
            Set p.Tag = Nothing
            Debug.Print p.Tag Is Nothing
            """)).Should().Equal("True", "1", "450", "438", "s", "True");
    }

    [AvaloniaTheory]
    [InlineData("StatusBar1", "35700")]
    [InlineData("ps", "35700")]
    [InlineData("p", "35700")]
    [InlineData("q", "35700")]
    [InlineData("Label1", "0")]
    [InlineData("StatusBar2", "0")]
    [InlineData("StatusBar2.Panels", "0")]
    [InlineData("StatusBar2.Panels(1)", "0")]
    public async Task Tag_rejects_only_the_measured_own_bar_cycles(string value, string error)
    {
        (await Run($"""
            Set q = ps.Add()
            Set c = {value}
            Set p.Tag = c
            Debug.Print Err.Number
            If Err.Number = 0 Then Debug.Print p.Tag Is c
            """)).Should().Equal(error == "0" ? ["0", "True"] : [error]);
    }

    [AvaloniaFact]
    public async Task Tag_accepts_the_first_StatusBar_on_a_panel_of_the_second()
    {
        (await Run("""
            Set q = StatusBar2.Panels(1)
            Set q.Tag = StatusBar1
            Debug.Print Err.Number, q.Tag Is StatusBar1
            """)).Should().Equal("0", "True");
    }

    [AvaloniaTheory]
    [InlineData("Form1")]
    [InlineData("Form1.Label1")]
    [InlineData("Form1.Icon")]
    [InlineData("Form1.Controls")]
    public void Tag_accepts_form_objects(string source)
    {
        RunInForm($"""
            Set c = {source}
            Set p.Tag = c
            Debug.Print Err.Number & "|" & (p.Tag Is c)
            """).Should().Equal("0|True");
    }

    [AvaloniaTheory]
    [InlineData("Set ")]
    [InlineData("")]
    public void Both_picture_puts_keep_the_form_icon(string put)
    {
        RunInForm($"""
            Set c = Form1.Icon
            {put}p.Picture = c
            Debug.Print Err.Number & "|" & (p.Picture Is c) & "|" & p.Picture.Type
            Err.Clear
            Set q = New Collection
            {put}p.Picture = q
            Debug.Print Err.Number & "|" & (p.Picture Is c)
            Set p.Picture = Nothing
            Debug.Print p.Picture Is Nothing
            """).Should().Equal("0|True|3", "13|True", "True");
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static List<string> RunInForm(string code)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Probe");
        var source = Form.Replace("Top = 4125", """
            Top = 4125
            BeginProperty Panels
               NumPanels = 1
               BeginProperty Panel1
                  Picture = "Form1.frx":0352
               EndProperty
            EndProperty
            """) + """

            Private Sub Form_Load()
                Dim p As Object, c As Object, q As Object
                Set p = StatusBar1.Panels(1)
                Set Form1.Icon = p.Picture
                Set p.Picture = Nothing
                On Error Resume Next
            """ + "\r\n" + code + "\r\nEnd Sub";
        var path = Path.Combine(RepoTree.Root(), "corpus", "inbox-controls", "saved", "statusbar-picture", "Form1.frm");
        var records = FrxDeserializer.Read(File.ReadAllBytes(Path.ChangeExtension(path, ".frx")), File.ReadAllText(path));
        var form = new FormDeserializer().Deserialize(project, source.ReplaceLineEndings("\r\n"), new NullSink(), frxBlobs: records)!;
        project.AddForm(form);

        var printed = new List<string>();
        var errors = new List<Exception>();
        void Faulted(VBWindowContext _, Exception error) => errors.Add(error);
        VBDebugConsole.Output += printed.Add;
        VBWindowContext.Faulted += Faulted;
        try
        {
            VBLoader.RunForm(form, CancellationToken.None, out var window);
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }
        finally
        {
            VBDebugConsole.Output -= printed.Add;
            VBWindowContext.Faulted -= Faulted;
        }
        errors.Should().BeEmpty();
        return printed;
    }

    [AvaloniaTheory]
    [InlineData("c")]
    [InlineData("5")]
    [InlineData("\"x\"")]
    [InlineData("Null")]
    [InlineData("Empty")]
    [InlineData("StatusBar1")]
    [InlineData("Label1")]
    public async Task Picture_puts_reject_nonpictures_and_preserve_the_reference(string value)
    {
        (await Run($"""
            Set c = New Collection
            p.Picture = {value}
            Debug.Print Err.Number, p.Picture Is Nothing
            Err.Clear
            Set q = LoadPicture()
            p.Picture = q
            Debug.Print Err.Number, p.Picture Is q, p.Picture.Type
            p.Picture = {value}
            Debug.Print Err.Number, p.Picture Is q
            Set p.Picture = Nothing
            Debug.Print p.Picture Is Nothing
            """)).Should().Equal("13", "True", "0", "True", "0", "13", "True", "True");
    }

    [AvaloniaTheory]
    [InlineData("ps.Remove 1")]
    [InlineData("ps.Clear")]
    [InlineData("Set ps = StatusBar2.Panels: Set p = ps(1): p.Text = \"one\": p.Key = \"one\": ps.Clear")]
    public async Task Removed_panel_keeps_values_but_loses_layout_and_most_setters(string remove)
    {
        var reads = new[] { "Index", "Width", "MinWidth", "Left" };
        var writes = new[] { "Text", "Style", "Alignment", "AutoSize", "Bevel", "Enabled", "Visible", "Width", "MinWidth", "Picture" };
        var code = $"""
            {remove}
            Debug.Print p.Text, p.Key, p.Tag, p.ToolTipText, p.Style, p.Alignment, p.AutoSize, p.Bevel, p.Enabled, p.Visible
            Debug.Print p.Picture Is Nothing, TypeName(p)
            x = p
            Debug.Print x
            """ + "\r\n" + string.Join("\r\n", reads.Select(member => $"Err.Clear: x = p.{member}: Debug.Print Err.Number"))
            + "\r\n" + string.Join("\r\n", writes.Select(member => $"Err.Clear: p.{member} = {(member == "Text" ? "\"x\"" : "1")}: Debug.Print Err.Number"))
            + "\r\n" + """
            Err.Clear
            p.Key = "new"
            p.Tag = "tag"
            p.ToolTipText = "tip"
            Debug.Print Err.Number, p.Text, p.Key, p.Tag, p.ToolTipText
            Set c = New Collection
            Set p.Tag = c
            Debug.Print p.Tag Is c
            ps.Add
            x = p.Index
            Debug.Print Err.Number, Err.Description
            """;
        (await Run(code)).Should().Equal(new[] { "one", "one", "", "", "0", "0", "0", "1", "True", "True", "True", "IPanel", "one" }
            .Concat(Enumerable.Repeat("35605", reads.Length + writes.Length))
            .Concat(["0", "one", "new", "tag", "tip", "True", "35605", "This item's control has been deleted"]));
    }

    [AvaloniaFact]
    public async Task A_removed_third_panel_does_not_reattach_when_another_is_added()
    {
        (await Run("""
            ps.Add
            Set q = ps.Add()
            ps.Remove 3
            ps.Add
            x = q.Index
            Debug.Print Err.Number, ps.Count
            """)).Should().Equal("35605", "3");
    }

    [AvaloniaTheory]
    [InlineData("Width")]
    [InlineData("MinWidth")]
    public async Task Lengths_above_the_measured_range_refuse(string member)
    {
        var assign = () => Run($"p.{member} = 2E+7");
        await assign.Should().ThrowAsync<NotImplementedException>().WithMessage("*#4882*");
    }

    [AvaloniaTheory]
    [InlineData("\"2000\"", "0", 2000.126f)]
    [InlineData("\" 2000 \"", "0", 2000.126f)]
    [InlineData("CCur(2000)", "0", 2000.126f)]
    [InlineData("CDec(2000)", "0", 2000.126f)]
    [InlineData("CDate(1)", "0", 1.133858f)]
    [InlineData("\"abc\"", "13", 1440)]
    [InlineData("Null", "13", 1440)]
    [InlineData("CVErr(1)", "13", 1440)]
    [InlineData("n", "13", 1440)]
    [InlineData("Array(1)", "13", 1440)]
    [InlineData("Empty", "380", 1440)]
    [InlineData("True", "380", 1440)]
    [InlineData("0.2", "380", 1440)]
    [InlineData("0.28", "380", 1440)]
    [InlineData("0.3", "0", 0.5669292f)]
    [InlineData("36", "0", 35.71654f)]
    [InlineData("108", "0", 107.7165f)]
    [InlineData("180", "0", 179.7165f)]
    [InlineData("252", "0", 251.7166f)]
    [InlineData("35.5", "0", 35.71654f)]
    [InlineData("2000.5", "0", 2000.693f)]
    [InlineData("10000", "0", 10000.06f)]
    [InlineData("100000", "0", 100000.1f)]
    [InlineData("120000", "0", 120000.2f)]
    [InlineData("150000", "0", 149999.8f)]
    [InlineData("180000", "0", 180000)]
    [InlineData("200000", "0", 200000.1f)]
    [InlineData("245745", "0", 245745.1f)]
    [InlineData("245760", "380", 1440)]
    [InlineData("250000", "380", 1440)]
    [InlineData("300000", "380", 1440)]
    [InlineData("327675", "380", 1440)]
    [InlineData("400000", "380", 1440)]
    [InlineData("491490", "380", 1440)]
    [InlineData("491505", "380", 1440)]
    [InlineData("491520", "380", 1440)]
    [InlineData("1E+6", "380", 1440)]
    [InlineData("1E+7", "380", 1440)]
    [InlineData("1E+39", "6", 1440)]
    public async Task Length_conversion(string value, string error, float readback)
    {
        var result = await Run($"""
            p.Width = {value}
            Debug.Print Err.Number, p.Width
            Err.Clear
            p.Width = 1440
            p.MinWidth = {value}
            Debug.Print Err.Number, p.MinWidth, p.Width
            """);
        result[0].Should().Be(error);
        result[2].Should().Be(error);
        foreach (var index in new[] { 1, 3, 4 })
        {
            // The oracle output is printed at seven significant digits.
            float.Parse(result[index], CultureInfo.InvariantCulture).ToString("G7", CultureInfo.InvariantCulture)
                .Should().Be(readback.ToString("G7", CultureInfo.InvariantCulture));
        }
    }
}
