using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Threading;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

// SP6 native/p-code measurements: https://github.com/hexide-io/HexIDE/issues/1474 (2026-11-18 comment,
// measuring PR #4717). The oracle addition is not yet on main; do not duplicate its measurements here.
public sealed class NamedControlArgumentsTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           ClientHeight = 3000
           ClientWidth = 6000
           StartUpPosition = 0
           Begin VB.ListBox List1
              Left = 0
              Top = 0
              Width = 1200
              Height = 600
           End
           Begin VB.ComboBox Combo1
              Left = 1200
              Top = 0
              Width = 1200
              Height = 315
           End
           Begin VB.TextBox Text1
              Left = 120
              Top = 600
              Width = 1200
              Height = 300
           End
           Begin VB.PictureBox Picture1
              Left = 0
              Top = 1200
              Width = 1200
              Height = 600
           End
        End
        Attribute VB_Name = "Form1"

        """;

    private sealed record Run(List<string> Printed, List<Exception> Errors);

    private static Run RunLoad(string code)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, Form + "Private Sub Form_Load()\n"
            + code + "\nEnd Sub\n"
            + "Private Function F(ByVal v As Variant) As Variant\nDebug.Print CStr(v)\nF = v\nEnd Function\n", new NullSink())!;
        project.AddForm(form);
        var run = new Run([], []);
        void Error(VBWindowContext _, Exception e) => run.Errors.Add(e);
        void RuntimeError(VBWindowContext _, VBRunTimeException e) => run.Errors.Add(e);
        void CompileError(VBWindowContext _, VBCompileErrorException e) => run.Errors.Add(e);
        VBDebugConsole.Output += run.Printed.Add;
        VBWindowContext.Faulted += Error;
        VBWindowContext.RunTimeError += RuntimeError;
        VBWindowContext.CompileError += CompileError;
        try
        {
            VBLoader.RunForm(form, CancellationToken.None, out var window);
            try { Dispatcher.UIThread.RunJobs(); }
            finally { window.Close(); }
        }
        finally
        {
            VBDebugConsole.Output -= run.Printed.Add;
            VBWindowContext.Faulted -= Error;
            VBWindowContext.RunTimeError -= RuntimeError;
            VBWindowContext.CompileError -= CompileError;
        }
        return run;
    }

    private static void Check(string code, params string[] expected)
    {
        var run = RunLoad(code);
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal(expected);
    }

    [AvaloniaTheory]
    [InlineData("List1")]
    [InlineData("Combo1")]
    public void AddItem_evaluates_in_parameter_order(string list) => Check($"""
        {list}.AddItem "a"
        {list}.AddItem "b"
        {list}.AddItem Index:=F(1), Item:=F("c")
        Debug.Print {list}.List(0) & {list}.List(1) & {list}.List(2)
        Debug.Print CStr(Err.Number)
        """, "c", "1", "acb", "0");

    [AvaloniaTheory]
    [InlineData("List1")]
    [InlineData("Combo1")]
    public void AddItem_mixes_positional_and_named_arguments(string list) => Check($"""
        {list}.AddItem "a"
        {list}.AddItem "d", Index:=0
        Debug.Print {list}.List(0) & {list}.List(1)
        Debug.Print CStr(Err.Number)
        """, "da", "0");

    [AvaloniaTheory]
    [InlineData("List1.RemoveItem Index:=0", "List1")]
    [InlineData("Call Combo1.RemoveItem(Index:=0)", "Combo1")]
    public void RemoveItem_binds_Index(string statement, string list) => Check($"""
        {list}.AddItem "a"
        {list}.AddItem "b"
        {statement}
        Debug.Print {list}.List(0)
        Debug.Print CStr({list}.ListCount) & "|" & CStr(Err.Number)
        """, "b", "1|0");

    [AvaloniaTheory]
    [InlineData("Me.")]
    [InlineData("Form1.")]
    [InlineData("")]
    public void Form_Move_evaluates_in_parameter_order(string receiver) => Check($"""
        {receiver}Move Height:=F(3600), Width:=F(5200), Top:=F(400), Left:=F(300)
        Debug.Print Me.Left & "|" & Me.Top & "|" & Me.Width & "|" & Me.Height & "|" & Err.Number
        """, "300", "400", "5200", "3600", "300|400|5200|3600|0");

    [AvaloniaTheory]
    [InlineData("Text1.Move 200, Top:=400", "200|400|1200|300|0")]
    [InlineData("Text1.Move 220, 2600, 1100, Height:=360", "220|2600|1100|360|0")]
    [InlineData("Call Text1.Move(Height:=360, Width:=1100, Top:=2600, Left:=220)", "220|2600|1100|360|0")]
    public void TextBox_Move_without_a_gap_works(string statement, string expected) => Check(statement
        + "\nDebug.Print Text1.Left & \"|\" & Text1.Top & \"|\" & Text1.Width & \"|\" & Text1.Height & \"|\" & Err.Number", expected);

    [AvaloniaTheory]
    [InlineData("Text1.Move 200, Height:=400")]
    [InlineData("Text1.Move 200, Width:=1500")]
    [InlineData("Text1.Move Left:=240, Height:=380")]
    [InlineData("Text1.Move 230, , , 370")]
    public void TextBox_Move_with_a_gap_raises_448_without_moving(string statement) => Check("On Error Resume Next\n" + statement
        + "\nDebug.Print CStr(Err.Number) & \"|\" & Err.Description"
        + "\nDebug.Print Text1.Left & \"|\" & Text1.Top & \"|\" & Text1.Width & \"|\" & Text1.Height",
        "448|Named argument not found", "120|600|1200|300");

    [AvaloniaFact]
    public void Form_Move_with_a_gap_raises_448_without_moving() => Check("""
        Me.Move 1200, 1200, 6000, 4500
        On Error Resume Next
        Me.Move 300, Width:=5200
        Debug.Print CStr(Err.Number) & "|" & Err.Description
        Debug.Print Me.Left & "|" & Me.Top & "|" & Me.Width & "|" & Me.Height
        """, "448|Named argument not found", "1200|1200|6000|4500");

    [AvaloniaTheory]
    [InlineData("Picture1.ScaleX(1440, ToScale:=vbPixels)")]
    [InlineData("Picture1.ScaleX(1440, , vbPixels)")]
    public void ScaleX_with_a_gap_raises_448(string expression) => Check("On Error Resume Next\nDim v\nv = " + expression
        + "\nDebug.Print CStr(Err.Number) & \"|\" & Err.Description\nDebug.Print IsEmpty(v)", "448|Named argument not found", "True");

    // Conversion value from docs/vb6-fidelity-oracle/the-coordinate-system-scalemode-scale-scalex-and-scaley.md.
    [AvaloniaFact]
    public void ScaleX_binds_all_three_names() => Check("""
        Debug.Print CStr(Picture1.ScaleX(ToScale:=vbTwips, FromScale:=vbInches, Width:=1))
        Debug.Print CStr(Err.Number)
        """, "1440", "0");

    // ZOrder's effect is measured in docs/vb6-fidelity-oracle/what-zorder-does-at-run-time.md.
    [AvaloniaFact]
    public void ZOrder_binds_Position() => Check("""
        On Error Resume Next
        Text1.ZOrder Position:=2
        Debug.Print CStr(Err.Number)
        """, "5");

    [AvaloniaTheory]
    [InlineData("List1.AddItem Itemx:=1", "Named argument not found")]
    [InlineData("Dim w\nw = Me.TextWidth(Text:=\"a\")", "Named argument not found")]
    [InlineData("List1.AddItem Item:=\"a\", Item:=\"b\"", "Named argument already specified")]
    [InlineData("Me.Move Top:=100", "Argument not optional")]
    [InlineData("Dim c As New Collection\nc.Add Itemx:=1", "Named argument not found")]
    [InlineData("List1.AddItem Index:=0, \"x\"", "Syntax error")]
    public void Early_bound_invalid_names_are_compile_errors(string statement, string message)
    {
        var run = RunLoad("On Error Resume Next\n" + statement + "\nDebug.Print \"continued\"");
        run.Errors.Should().ContainSingle().Which.Should().BeOfType<VBCompileErrorException>()
            .Which.Message.Should().Contain(message);
        run.Printed.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData("o.AddItem Item:=\"late\"", "List1")]
    [InlineData("o.AddItem Index:=0, Item:=\"x\"", "List1")]
    [InlineData("o.AddItem Itemx:=1", "List1")]
    [InlineData("o.AddItem \"p\", Index:=0", "List1")]
    [InlineData("o.AddItem Item:=\"a\", Item:=\"b\"", "List1")]
    [InlineData("Call o.AddItem(Item:=\"late\")", "Combo1")]
    [InlineData("o.Move Left:=200, Top:=400", "Text1")]
    [InlineData("o.Move Left:=200, Top:=400", "Me")]
    [InlineData("Call o.Move(Left:=200, Top:=400)", "Form1")]
    public void Object_and_Variant_control_calls_refuse_every_name(string statement, string target)
    {
        foreach (var declaration in new[] { "As Object", "As Variant" })
            Check($"""
                Me.Move 1200, 1200, 6000, 4500
                Dim o {declaration}
                Set o = {target}
                On Error Resume Next
                {statement}
                Debug.Print CStr(Err.Number) & "|" & Err.Description
                Debug.Print CStr(List1.ListCount) & "|" & CStr(Combo1.ListCount)
                Debug.Print Text1.Left & "|" & Text1.Top
                Debug.Print Me.Left & "|" & Me.Top
                """, "448|Named argument not found", "0|0", "120|600", "1200|1200");
    }

    // SP6 native/p-code: https://github.com/hexide-io/HexIDE/issues/4737 (measuring PR #4753).
    [AvaloniaTheory]
    [InlineData("Control", "List1", "c.AddItem Item:=\"viaControl\"")]
    [InlineData("VB.Control", "List1", "c.AddItem Item:=\"viaControl\"")]
    [InlineData("Control", "Text1", "c.Move Left:=300, Top:=2600")]
    public void Control_variables_refuse_names_without_changing_the_target(string type, string target, string statement) => Check($"""
        Dim c As {type}
        Set c = {target}
        On Error Resume Next
        {statement}
        Debug.Print CStr(Err.Number) & "|" & Err.Description
        Debug.Print CStr(List1.ListCount)
        Debug.Print Text1.Left & "|" & Text1.Top
        """, "448|Named argument not found", "0", "120|600");

    // Preserve the existing non-named paths when recording the new view (#4737).
    [AvaloniaTheory]
    [InlineData("Control")]
    [InlineData("VB.Control")]
    public void Control_view_preserves_Set_properties_positional_calls_and_TypeOf(string type) => Check($"""
        Dim c As {type}
        Set c = List1
        c.AddItem "posControl"
        Debug.Print CStr(c.ListCount) & "|" & c.List(0)
        Debug.Print TypeOf c Is ListBox
        Set c = Text1
        Debug.Print c.Text
        Debug.Print CStr(c.Left)
        Debug.Print CStr(Err.Number)
        """, "1|posControl", "True", "", "120", "0");

    // Measured in review (2026-11-18, SP6 native and p-code): a variable declared as the control's own class binds names.
    [AvaloniaTheory]
    [InlineData("As ListBox", "List1", "o.AddItem Item:=\"typed\"", "1|0|120|600|0|0")]
    [InlineData("As ComboBox", "Combo1", "o.AddItem Item:=\"typed\"", "0|1|120|600|0|0")]
    [InlineData("As TextBox", "Text1", "o.Move Left:=400, Top:=2700", "0|0|400|2700|0|0")]
    public void Typed_control_variables_bind_names(string declaration, string target, string statement, string expected) => Check($"""
        Dim o {declaration}
        Set o = {target}
        On Error Resume Next
        {statement}
        Debug.Print CStr(List1.ListCount) & "|" & CStr(Combo1.ListCount) & "|" & Text1.Left & "|" & Text1.Top & "|" & CStr(Err.Number) & "|" & CStr(Erl)
        """, expected);

    [AvaloniaTheory]
    [InlineData("As Form")]
    [InlineData("As Form1")]
    public void Typed_form_variables_bind_names(string declaration) => Check($"""
        Dim f {declaration}
        Set f = Me
        On Error Resume Next
        f.Move Left:=700, Top:=800
        Debug.Print CStr(Err.Number) & "|" & Me.Left & "|" & Me.Top
        """, "0|700|800");

    [AvaloniaTheory]
    [InlineData("Me.TextWidth(Str:=F(\"a\"))", "a")]
    [InlineData("TextWidth(Str:=F(\"a\"))", "a")]
    public void TextWidth_binds_Str_but_keeps_its_unimplemented_execution(string expression, string argument)
    {
        var run = RunLoad("Dim w\nw = " + expression);
        run.Printed.Should().Equal(argument);
        run.Errors.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>()
            .Which.Message.Should().Contain("TextWidth").And.NotContain("named argument");
    }

    [AvaloniaTheory]
    [InlineData("Me.Show Modal:=0")]
    [InlineData("Me.PopupMenu Menu:=1")]
    [InlineData("Me.PaintPicture Picture:=1, X1:=0, Y1:=0")]
    [InlineData("Picture1.ScaleY Height:=1")]
    public void Unmeasured_method_names_keep_the_1474_refusal(string statement)
    {
        var run = RunLoad(statement);
        run.Errors.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>()
            .Which.Message.Should().Contain("#1474");
        run.Printed.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData("As New Collection", "")]
    [InlineData("As Object", "Set c = New Collection")]
    [InlineData("As Variant", "Set c = New Collection")]
    public void Collection_keeps_its_named_statement_and_expression_calls(string declaration, string setup) => Check($"""
        Dim c {declaration}
        {setup}
        c.Add "a", "ka"
        c.Add "b", "kb", Before:=1
        c.Add Item:="c", Key:="kc"
        Debug.Print c.Item(Index:=1) & c.Item(Index:=2) & c.Item(Index:=3)
        Debug.Print CStr(Err.Number)
        """, "bac", "0");

    [AvaloniaTheory]
    [InlineData("As Object")]
    [InlineData("As Variant")]
    public void Late_bound_Collection_unknown_name_remains_448(string declaration) => Check($"""
        Dim c {declaration}
        Set c = New Collection
        On Error Resume Next
        c.Add Itemx:=1
        Debug.Print CStr(Err.Number)
        Debug.Print CStr(c.Count)
        """, "448", "0");
}
