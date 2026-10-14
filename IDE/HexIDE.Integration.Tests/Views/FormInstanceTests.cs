using System.Collections.Generic;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A running form is an instance of its module, with a real <c>Me</c> (#822). Every expectation here was measured on
/// vb6.exe, native and p-code agreeing: the oracle's <i>A form as an object: its names, its lifecycle and its predeclared
/// instance</i>, and for the WithEvents rows <i>A form-based game's runtime surface</i>. Run from
/// <see cref="VBLoader.RunForm"/>, where F5 starts.
/// </summary>
/// <remarks>
/// A bare name for one of the form's built-in members (<c>Caption = "x"</c>, implicit Me) is #995, so those rows are
/// pinned through <c>Me</c> here: the same lookup, reached qualified.
/// </remarks>
public class FormInstanceTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string FormHeader =
        "VERSION 5.00\r\n"
      + "Begin VB.Form Form1 \r\n"
      + "   Caption         =   \"Form1\"\r\n"
      + "   ClientHeight    =   3000\r\n"
      + "   ClientWidth     =   4000\r\n"
      + "   Begin VB.TextBox Text1 \r\n"
      + "      Height          =   495\r\n"
      + "      Left            =   120\r\n"
      + "      TabIndex        =   0\r\n"
      + "      Text            =   \"t1\"\r\n"
      + "      Top             =   120\r\n"
      + "      Width           =   1215\r\n"
      + "   End\r\n"
      + "End\r\n"
      + "Attribute VB_Name = \"Form1\"\r\n";

    private sealed record Run(List<string> Printed, List<Exception> Errors);

    /// <summary>Run Form1 with <paramref name="formCode"/>, and the named modules, as F5 does, then close it.</summary>
    private static Run RunForm(string formCode, params (string Name, ModuleKind Kind, string Code)[] modules)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, (FormHeader + formCode).ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form);
        foreach (var (name, kind, code) in modules)
        {
            var module = new ModuleDefinition(project, name, kind);
            module.UpdateCode(code.ReplaceLineEndings("\r\n"));
            project.AddModule(module);
        }

        var run = new Run([], []);
        void OnFaulted(VBWindowContext _, Exception e) => run.Errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => run.Errors.Add(e);
        void OnCompileError(VBWindowContext _, VBCompileErrorException e) => run.Errors.Add(e);
        VBDebugConsole.Output += run.Printed.Add;
        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        VBWindowContext.CompileError += OnCompileError;
        try
        {
            VBLoader.RunForm(form, CancellationToken.None, out var window);
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }
        finally
        {
            VBDebugConsole.Output -= run.Printed.Add;
            VBWindowContext.Faulted -= OnFaulted;
            VBWindowContext.RunTimeError -= OnRunTimeError;
            VBWindowContext.CompileError -= OnCompileError;
        }
        return run;
    }

    private static Run RunFormLoad(string body, params (string Name, ModuleKind Kind, string Code)[] modules) =>
        RunForm($"Private Sub Form_Load()\n{body}\nEnd Sub\n", modules);

    // ── Names inside a form ─────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Me_is_the_form_and_its_name_is_the_same_instance()
    {
        // Measured: Form1, Form1, True. It used to be the window, whose .NET type name is FormRuntime.
        var run = RunFormLoad("    Debug.Print TypeName(Me) & \" \" & TypeName(Form1) & \" \" & (Me Is Form1)");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Form1 Form1 True");
    }

    [AvaloniaFact]
    public void The_forms_Caption_is_its_own_where_a_module_declares_one()
    {
        // Measured unqualified (`Caption = "set by form"`); the bare name is #995, so this goes through Me.
        var run = RunFormLoad("    Me.Caption = \"set by form\"\n    Debug.Print Me.Caption & \" [\" & Module1.Caption & \"]\"",
            ("Module1", ModuleKind.StandardModule, "Public Caption As String\n"));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("set by form []");
    }

    [AvaloniaFact]
    public void The_forms_Tag_is_its_own_where_a_module_has_a_Function_of_the_name()
    {
        var run = RunFormLoad("    Debug.Print \"[\" & Me.Tag & \"] \" & Module1.Tag()",
            ("Module1", ModuleKind.StandardModule, "Public Function Tag() As String\n    Tag = \"module tag\"\nEnd Function\n"));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("[] module tag");
    }

    [AvaloniaFact]
    public void A_control_is_the_forms_where_a_module_declares_a_Public_of_its_name()
    {
        var run = RunFormLoad("    Debug.Print TypeName(Text1) & \" \" & TypeName(Me.Text1)",
            ("Module1", ModuleKind.StandardModule, "Public Text1 As String\n"));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("TextBox TextBox");
    }

    [AvaloniaFact]
    public void A_forms_Public_is_its_own_where_a_module_declares_one()
    {
        var run = RunForm("Public Score As Long\nPrivate Sub Form_Load()\n    Score = 5\n    Debug.Print Me.Score & \" \" & Module1.Score\nEnd Sub\n",
            ("Module1", ModuleKind.StandardModule, "Public Score As Long\n"));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("5 0");
    }

    [AvaloniaFact]
    public void A_form_procedure_calling_another_runs_on_the_same_instance()
    {
        var run = RunForm("Private Score As Long\nPrivate Sub Bump()\n    Score = Score + 1\nEnd Sub\n"
            + "Private Sub Form_Load()\n    Bump\n    Bump\n    Debug.Print Score & \" \" & Me.Text1.Text\nEnd Sub\n");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("2 t1");
    }

    // ── The form from a standard module ─────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void A_module_reaches_the_form_through_its_name()
    {
        // Measured: cap, 5, t1. The form's name was bound in its own module only, so a module could not say Form1.
        var run = RunForm("Public Score As Long\nPrivate Sub Form_Load()\n    Me.Caption = \"cap\"\n    Score = 5\n    FromModule\nEnd Sub\n",
            ("Module1", ModuleKind.StandardModule,
                "Sub FromModule()\n    Debug.Print Form1.Caption & \" \" & Form1.Score & \" \" & Form1.Text1.Text\nEnd Sub\n"));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("cap 5 t1");
    }

    [AvaloniaFact]
    public void A_forms_Public_variable_is_not_a_global()
    {
        // Measured: Score=[], a new variable in Module1, there being no Option Explicit.
        var run = RunForm("Public Score As Long\nPrivate Sub Form_Load()\n    Score = 5\n    FromModule\nEnd Sub\n",
            ("Module1", ModuleKind.StandardModule, "Sub FromModule()\n    Debug.Print \"Score=[\" & Score & \"]\"\nEnd Sub\n"));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Score=[]");
    }

    [AvaloniaFact]
    public void A_forms_Public_Sub_is_not_a_global()
    {
        // Measured: "Sub or Function not defined", at compile time; here as the call runs. The form was resolved as a
        // standard module, so the call reached it.
        var run = RunForm("Public Sub Hello()\n    Debug.Print \"hello\"\nEnd Sub\nPrivate Sub Form_Load()\n    CallIt\nEnd Sub\n",
            ("Module1", ModuleKind.StandardModule, "Sub CallIt()\n    Hello\nEnd Sub\n"));

        run.Printed.Should().BeEmpty();
        run.Errors.Should().ContainSingle().Which.Should().BeOfType<VBCompileErrorException>()
            .Which.Message.Should().Contain("Sub or Function not defined");
    }

    [AvaloniaFact]
    public void A_modules_Sub_is_found_where_the_form_has_one_of_the_same_name()
    {
        // Measured: "module2 ping". With the form resolved as a standard module, Module1's call had two Public Pings to
        // choose from, and was "Ambiguous name detected".
        var run = RunForm("Public Sub Ping()\n    Debug.Print \"form ping\"\nEnd Sub\nPrivate Sub Form_Load()\n    CallPing\nEnd Sub\n",
            ("Module1", ModuleKind.StandardModule, "Sub CallPing()\n    Ping\nEnd Sub\n"),
            ("Module2", ModuleKind.StandardModule, "Public Sub Ping()\n    Debug.Print \"module2 ping\"\nEnd Sub\n"));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("module2 ping");
    }

    // ── The lifecycle ───────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void The_forms_declarations_are_in_place_at_Initialize_which_comes_before_Load_and_Resize()
    {
        // Measured: `initialize: UBound(a)=5 n=0`, `load`, then `resize`. Before, Initialize was never raised, a form's
        // declarations never ran (#256), and Resize came before Load (#994).
        var run = RunForm("Const K = 5\nDim a(1 To K) As Integer\nDim n As Integer\n"
            + "Private Sub Form_Initialize()\n    Debug.Print \"initialize: UBound(a)=\" & UBound(a) & \" n=\" & n\nEnd Sub\n"
            + "Private Sub Form_Load()\n    Debug.Print \"load\"\nEnd Sub\n"
            + "Private Sub Form_Resize()\n    Debug.Print \"resize\"\nEnd Sub\n");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().StartWith(["initialize: UBound(a)=5 n=0", "load"]);
        run.Printed.Skip(2).Should().NotBeEmpty("showing the window resizes it").And.OnlyContain(line => line == "resize");
    }

    [AvaloniaFact]
    public void A_modules_Public_Const_is_read_from_Form_Load()
    {
        // #256: a form's run never ran the modules' declarations, so a qualified Public Const read Empty.
        var run = RunFormLoad("    Helpers.Announce\n    Debug.Print \"constQ=[\" & Helpers.Greeting & \"] varQ=[\" & Helpers.CallCount & \"]\"",
            ("Helpers", ModuleKind.StandardModule,
                "Public Const Greeting As String = \"const-ok\"\nPublic CallCount As Long\nPublic Sub Announce()\n    CallCount = CallCount + 1\nEnd Sub\n"));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("constQ=[const-ok] varQ=[1]");
    }

    [AvaloniaTheory]
    [InlineData("Text1.Text = \"x\"", "Text1")]
    [InlineData("Me.Caption = \"x\"", "Form1.Caption")]
    public void A_control_or_built_in_member_used_in_Form_Initialize_is_refused(string statement, string named)
    {
        // VB6 loads the form there and then, inside Initialize (measured). HexIDE can't yet, and running Load afterwards
        // would leave the wrong value, so the statement stops with "not implemented" (#1009). Load still runs after.
        var run = RunForm($"Private Sub Form_Initialize()\n    {statement}\n    Debug.Print \"initialize end\"\nEnd Sub\n"
            + "Private Sub Form_Load()\n    Debug.Print \"load\"\nEnd Sub\n");

        run.Errors.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>()
            .Which.Message.Should().Contain(named).And.Contain("#1009");
        run.Printed.Should().Equal("load");
    }

    // ── The debugger ────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Locals_shows_a_forms_Me_with_its_own_properties_then_its_variables_and_controls()
    {
        // Me was the window, and Locals listed the window's VB6 properties under it, then the form's variables and
        // controls. Me is now the form's instance, and Locals shows the same tree.
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var code = "Private mCount As Integer\nPrivate Sub Form_Load()\n    mCount = 7\n    Stop\nEnd Sub\n";
        var form = new FormDeserializer().Deserialize(project, (FormHeader + code).ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form);
        var dbg = new HexIDE.Runtime.Debugging.DebugController();
        dbg.Reset();

        var run = VBLoader.RunForm(form, CancellationToken.None, out var window, dbg);
        try
        {
            Dispatcher.UIThread.RunJobs();   // the walk reaches Stop, and parks there
            dbg.State.Should().Be(HexIDE.Runtime.Debugging.DebugState.Paused);
            var me = dbg.GetLocals()!.Locals.Single(n => n.Name == "Me");
            me.TypeName.Should().Be("Form1");
            var children = me.Expand();
            var names = children.Select(n => n.Name).ToList();
            names.Should().Contain(["Caption", "mCount", "Text1"]);
            names.IndexOf("Caption").Should().BeLessThan(names.IndexOf("mCount"), "the form's own properties come first");
            children.Single(n => n.Name == "mCount").Value.Should().Be("7");
        }
        finally
        {
            dbg.Stop();
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }
        run.IsCompleted.Should().BeTrue();
    }

    // ── WithEvents on a form (#972) ─────────────────────────────────────────────────────────────────────

    private const string Source =
        "Public Event ResultV(ByVal r As Integer)\nPublic Event ResultR(r As Integer)\n"
      + "Public Function FireV() As Integer\n    Dim x As Integer\n    x = 3\n    RaiseEvent ResultV(x)\n    FireV = x\nEnd Function\n"
      + "Public Function FireR() As Integer\n    Dim x As Integer\n    x = 3\n    RaiseEvent ResultR(x)\n    FireR = x\nEnd Function\n";

    [AvaloniaTheory]
    [InlineData("FireV", "ResultV", "ByVal r As Integer", "x 3")]   // measured: the handler got 3; the raiser's x is still 3
    [InlineData("FireR", "ResultR", "r As Integer", "x 99")]        // measured: the handler got 3; the raiser's x is 99
    public void A_WithEvents_variable_in_a_form_binds(string fire, string evt, string parameter, string raiserSees)
    {
        // A form had no object of its own to be the sink, so the handler never ran (#972).
        var run = RunForm("Private WithEvents mSrc As Source\n"
            + $"Private Sub mSrc_{evt}({parameter})\n    Debug.Print \"got \" & r\n    r = 99\nEnd Sub\n"
            + $"Private Sub Form_Load()\n    Set mSrc = New Source\n    Debug.Print \"x \" & mSrc.{fire}()\nEnd Sub\n",
            ("Source", ModuleKind.ClassModule, Source));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("got 3", raiserSees);
    }
}
