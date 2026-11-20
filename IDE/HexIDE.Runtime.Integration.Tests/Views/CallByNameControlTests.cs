using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

// VB6 SP6, native and p-code: https://github.com/hexide-io/HexIDE/issues/4520,
// supervisor's 2026-11-18 measurement (oracle PR #4759). Font identity: docs/vb6-fidelity-oracle/a-controls-font-object.md.
// Empty ListCount: docs/vb6-fidelity-oracle/a-lists-members-listcount-list.md.
public sealed class CallByNameControlTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "hexide-callbyname-" + Guid.NewGuid().ToString("N"));
    public CallByNameControlTests() => Directory.CreateDirectory(dir);
    public void Dispose() { try { Directory.Delete(dir, true); } catch (IOException) { } }
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }
    private const string FormHead = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption = "Form1"
           ClientHeight = 4500
           ClientWidth = 6000
           StartUpPosition = 0
           Begin VB.TextBox Text1
              Text = "Text1"
              Left = 2400
              Top = 120
              Width = 1200
              Height = 315
           End
           Begin VB.ListBox List1
              Left = 120
              Top = 120
              Width = 1200
              Height = 1200
           End
           Begin VB.ComboBox Combo1
              Left = 120
              Top = 1500
              Width = 1200
              Height = 315
           End
           Begin VB.PictureBox Picture1
              Left = 120
              Top = 2400
              Width = 1200
              Height = 1200
           End
        End
        Attribute VB_Name = "Form1"
        """;
    private async Task<(List<string> Printed, List<Exception> Errors, Window Window)> RunAsync(string load, CancellationToken token, string? formHead = null)
    {
        var frm = (formHead ?? FormHead) + "\r\n" + "Private Sub Form_Load()\r\n" + load + "\r\nEnd Sub\r\n";
        File.WriteAllText(Path.Combine(dir, "Form1.frm"), frm.ReplaceLineEndings("\r\n"), Encoding.ASCII);
        File.WriteAllText(Path.Combine(dir, "Probe.vbp"), "Type=Exe\r\nForm=Form1.frm\r\nStartup=\"Form1\"\r\nName=\"Probe\"\r\n", Encoding.ASCII);
        var project = (await new ProjectLoader().LoadAsync(Path.Combine(dir, "Probe.vbp"), new NullSink())).Project;
        var printed = new List<string>();
        var errors = new List<Exception>();
        Action<string> print = printed.Add;
        void OnFaulted(VBWindowContext _, Exception e) => errors.Add(e);
        VBDebugConsole.Output += print;
        VBWindowContext.Faulted += OnFaulted;
        try
        {
            _ = VBLoader.RunMadeProgram(project, token, out var startup);
            for (var i = 0; i < 20; i++)
                Dispatcher.UIThread.RunJobs();
            return (printed, errors, startup!);
        }
        finally
        {
            VBDebugConsole.Output -= print;
            VBWindowContext.Faulted -= OnFaulted;
        }
    }

    private static Control Named(Window window, string name) =>
        window.GetLogicalDescendants().OfType<Control>().First(c => VBProps.GetName(c) == name);

    private async Task Check(string load, Action<List<string>, Window> assert, string? formHead = null)
    {
        using var cancel = new CancellationTokenSource();
        Window? window = null;
        try
        {
            (var printed, var errors, window) = await RunAsync(load, cancel.Token, formHead);
            errors.Should().BeEmpty();
            assert(printed, window);
        }
        finally
        {
            cancel.Cancel();
            window?.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("v = CallByName(Text1, \"Text\", VbGet)", "String|Text1")]
    [InlineData("v = CallByName(Text1, \"text\", VbGet)", "String|Text1")]
    [InlineData("CallByName Text1, \"Text\", VbLet, \"abc\"\nv = Text1.Text", "String|abc")]
    [InlineData("v = CallByName(Text1, \"Left\", VbGet)", "Single|2400")]
    [InlineData("CallByName Text1, \"Left\", VbLet, 300\nv = Text1.Left", "Single|300")]
    [InlineData("CallByName Text1, \"Left\", VbLet, \"400\"\nv = Text1.Left", "Single|400")]
    [InlineData("v = CallByName(Text1, \"Name\", VbGet)", "String|Text1")]
    [InlineData("v = CallByName(Text1, \"Visible\", VbGet)\nDebug.Print TypeName(Text1.Visible) & \"|\" & Text1.Visible", "Boolean|False", "Boolean|False")]
    [InlineData("v = CallByName(Text1, \"\", VbGet)", "String|Text1")]
    [InlineData("v = CallByName(List1, \"ListCount\", VbGet)", "Integer|0")]
    [InlineData("v = CallByName(Combo1, \"ListCount\", VbGet)", "Integer|0")]
    [InlineData("List1.AddItem \"a\"\nList1.AddItem \"b\"\nv = CallByName(List1, \"List\", VbGet, 1)", "String|b")]
    [InlineData("v = CallByName(Me, \"Caption\", VbGet)", "String|Form1")]
    [InlineData("CallByName Me, \"Caption\", VbLet, \"NewCap\"\nv = Me.Caption", "String|NewCap")]
    [InlineData("v = CallByName(Text1, \"Font\", VbGet)", "String|MS Sans Serif")]
    [InlineData("Set o = CallByName(Text1, \"Font\", VbGet)\nDebug.Print TypeName(o)\nv = (o Is Text1.Font)", "Font", "Boolean|True")]
    [InlineData("v = CallByName(Me, \"Text1\", VbGet)", "String|Text1")]
    [InlineData("Set o = CallByName(Me, \"Text1\", VbGet)\nDebug.Print TypeName(o)\nv = (o Is Text1)", "TextBox", "Boolean|True")]
    [InlineData("Set v = Text1\nv = CallByName(v, \"Text\", VbGet)", "String|Text1")]
    [InlineData("CallByName Text1, \"Move\", VbMethod, 500, 2500\nv = Text1.Left & \"|\" & Text1.Top", "String|500|2500")]
    [InlineData("v = CallByName(Picture1, \"ScaleX\", VbMethod, 1440, vbTwips, vbPixels)", "Single|96")]
    [InlineData("CallByName Me, \"Move\", VbMethod, 800, 900\nv = Me.Left & \"|\" & Me.Top", "String|800|900")]
    [InlineData("v = CallByName(Text1, \"Text\", 3)", "String|Text1")]
    [InlineData("Set o = Picture1.Font\nCallByName Text1, \"Font\", VbSet, o\nv = (Text1.Font Is o)", "Boolean|True")]
    [InlineData("Set o = Picture1.Font\nCallByName Text1, \"Font\", VbLet, o\nv = Text1.FontName", "String|MS Sans Serif")]
    // callbyname.md: a New StdFont named Courier New, set and let through CallByName.
    [InlineData("Dim f As New StdFont\nf.Name = \"Courier New\"\nCallByName Text1, \"Font\", VbSet, f\nv = Text1.Font.Name", "String|Courier New")]
    [InlineData("Dim f As New StdFont\nf.Name = \"Courier New\"\nCallByName Text1, \"Font\", VbLet, f\nv = Err.Number", "Long|0")]
    public Task Measured_values(string code, params string[] expected) => Check(
        "Dim v As Variant\nDim o As Object\n" + code + "\nDebug.Print TypeName(v) & \"|\" & v",
        (printed, _) => printed.Should().Equal(expected));

    [AvaloniaTheory]
    [InlineData("CallByName List1, \"ListCount\", VbLet, 5", 383)]
    [InlineData("v = CallByName(List1, \"List\", VbGet)", 385)]
    [InlineData("v = CallByName(Text1, \"Container\", VbGet)", 450)]
    [InlineData("CallByName Text1, \"SetFocus\", VbMethod", 5)]
    [InlineData("CallByName List1, \"AddItem\", VbMethod", 450)]
    [InlineData("CallByName List1, \"AddItem\", VbMethod, \"c\", 0, 1", 450)]
    [InlineData("v = CallByName(Text1, \"Foo\", VbGet)", 438)]
    [InlineData("CallByName Text1, \"Foo\", VbLet, 1", 438)]
    [InlineData("CallByName Text1, \"Foo\", VbMethod", 438)]
    [InlineData("v = CallByName(Text1, \"Text\", VbMethod)", 438)]
    [InlineData("v = CallByName(Text1, \"Move\", VbGet, 1, 2)", 438)]
    [InlineData("v = CallByName(List1, \"Clear\", VbGet)", 438)]
    [InlineData("CallByName Text1, \"Text\", VbSet, \"x\"", 438)]
    public Task Measured_errors(string code, int expected) => Check(
        "Dim v As Variant\nList1.AddItem \"a\"\nOn Error Resume Next\n" + code + "\nDebug.Print CStr(Err.Number)",
        (printed, window) =>
        {
            printed.Should().Equal(expected.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ((ItemsControl)Named(window, "List1")).Items.Cast<string>().Should().Equal("a");
            Canvas.GetLeft(Named(window, "Text1")).Should().Be(2400d / 15);
            Canvas.GetTop(Named(window, "Text1")).Should().Be(120d / 15);
        });

    [AvaloniaTheory]
    [InlineData("CallByName List1, \"AddItem\", VbMethod, \"a\"\nCallByName List1, \"AddItem\", VbMethod, \"b\", 0", "b", "a")]
    [InlineData("List1.AddItem \"a\"\nList1.AddItem \"b\"\nCallByName List1, \"List\", VbLet, 0, \"zz\"", "zz", "b")]
    [InlineData("List1.AddItem \"a\"\nList1.AddItem \"b\"\nCallByName List1, \"RemoveItem\", VbMethod, 0", "b")]
    [InlineData("List1.AddItem \"a\"\nCallByName List1, \"Clear\", VbMethod")]
    public Task List_mutations(string code, params string[] expected) => Check(code,
        (_, window) => ((ItemsControl)Named(window, "List1")).Items.Cast<string>().Should().Equal(expected));

    // #4520's original Color rows, measured in oracle PR #4508.
    [AvaloniaTheory]
    [InlineData("v = CallByName(dlg, \"Color\", VbGet)\nDebug.Print VarType(v)", (short)19)]
    [InlineData("On Error Resume Next\nCallByName dlg, \"Color\", VbLet, -1\nDebug.Print Err.Number", 6L)]
    public async Task CommonDialog_uses_the_late_bound_property_path(string code, object expected)
    {
        var (_, _, context, env) = HexIDE.Integration.Tests.Views.ContainerRuntimeHarness.Spawn(FormHead);
        var host = Substitute.For<IBasicStandardLibrary>();
        context.AllocVariable(env, "dlg", new Vb6Value(new VBCommonDialog(host)));
        var printed = await HexIDE.Integration.Tests.Views.ContainerRuntimeHarness.Run(context, env, code);
        printed.Should().ContainSingle().Which.Value.Should().Be(expected);
    }

    [AvaloniaFact]
    public Task An_unmeasured_form_default_name_keeps_its_refusal() =>
        CheckRefusal("v = CallByName(Me, \"\", VbGet)", null);

    [AvaloniaFact]
    public Task A_user_defined_form_field_holding_a_control_keeps_its_refusal() =>
        CheckRefusal("Set held = Text1\nv = CallByName(Me, \"held\", VbGet)", null,
            FormHead + "\nPrivate held As Object");

    [AvaloniaTheory]
    [InlineData("Text1")]
    [InlineData("Me")]
    public Task Unmeasured_Font_Set_conversions_remain_refused(string receiver) =>
        CheckRefusal($"CallByName {receiver}, \"Font\", VbSet, \"Arial\"", null);

    [AvaloniaFact]
    public async Task An_OCX_with_different_late_bound_colours_keeps_its_refusal()
    {
        // in-box-controls-the-dtpicker.md: CallByName colours have VarType 19, unlike direct Long reads.
        var (_, _, context, env) = HexIDE.Integration.Tests.Views.ContainerRuntimeHarness.Spawn(FormHead);
        context.AllocVariable(env, "picker", new Vb6Value(new VBDTPicker()));
        var act = () => HexIDE.Integration.Tests.Views.ContainerRuntimeHarness.Run(context, env,
            "v = CallByName(picker, \"CalendarBackColor\", VbGet)");
        await act.Should().ThrowAsync<NotImplementedException>();
    }

    [AvaloniaFact]
    public Task Parse_tree_and_CallByName_read_the_same_written_member() => Check("""
        CallByName Text1, "Left", VbLet, 300
        Debug.Print Text1.Left = CallByName(Text1, "Left", VbGet)
        Text1.Left = 400
        Debug.Print Text1.Left = CallByName(Text1, "Left", VbGet)
        """, (printed, _) => printed.Should().Equal("True", "True"));

    [AvaloniaTheory]
    [InlineData("v = CallByName(Me, \"TextWidth\", VbMethod, \"abc\")", "TextWidth")]
    public Task Existing_member_gaps_stay_untrappable(string code, string member) => CheckRefusal(code, member);

    private async Task CheckRefusal(string code, string? member, string? formHead = null)
    {
        using var cancel = new CancellationTokenSource();
        var (_, errors, window) = await RunAsync("On Error Resume Next\n" + code, cancel.Token, formHead);
        try
        {
            var error = errors.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>().Which;
            if (member is not null) error.Message.Should().Contain(member);
        }
        finally
        {
            cancel.Cancel();
            window.Close();
        }
    }
}
