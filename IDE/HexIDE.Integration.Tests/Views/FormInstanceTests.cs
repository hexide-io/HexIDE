using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
/// The names rows are pinned both ways: through <c>Me</c>, and bare, which is implicit <c>Me</c> (#995).
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

    private sealed record Run(List<string> Printed, List<Exception> Errors)
    {
        /// <summary>Whether the window was showing once the start had run.</summary>
        public bool Shown { get; set; }
    }

    /// <summary>Run Form1 with <paramref name="formCode"/>, and the named modules, as F5 does, then close it.</summary>
    private static Run RunForm(string formCode, params (string Name, ModuleKind Kind, string Code)[] modules) =>
        RunFormIn(new ProjectDefinition(VBProjectType.EXE, "P"), formCode, modules);

    /// <summary>The same, in a project the test has set up: another form, or a reference.</summary>
    private static Run RunFormIn(ProjectDefinition project, string formCode, params (string Name, ModuleKind Kind, string Code)[] modules)
    {
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
            run.Shown = window.IsVisible;
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
    [InlineData("Text1.Text = \"x\"", "Text1.Text")]
    [InlineData("Me.Caption = \"x\"", "Me.Caption")]
    [InlineData("Caption = \"x\"", "Caption")]
    public void A_control_or_built_in_member_used_in_Form_Initialize_loads_the_form_there(string statement, string readBack)
    {
        // VB6 loads the form there and then, inside Initialize, and Load isn't raised again after it (measured, #822). It
        // used to be refused (#1009), since running Load afterwards would have left the wrong value.
        var run = RunForm($"Private Sub Form_Initialize()\n    {statement}\n    Debug.Print \"initialize end \" & {readBack}\nEnd Sub\n"
            + "Private Sub Form_Load()\n    Debug.Print \"load\"\nEnd Sub\n");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("load", "initialize end x");
        run.Shown.Should().BeTrue();
    }

    [AvaloniaFact]
    public void A_fault_in_Form_Load_ends_the_start_before_the_window_is_shown()
    {
        // A form's Move is not implemented yet, so the statement faults, and a fault ends the run.
        var run = RunForm("Private Sub Form_Load()\n    Debug.Print \"load\"\n    Me.Move 0, 0\n    Debug.Print \"load end\"\nEnd Sub\n");

        run.Errors.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>();
        run.Printed.Should().Equal("load");
        run.Shown.Should().BeFalse();
    }

    [AvaloniaFact]
    public void A_run_time_error_in_Form_Initialize_is_reported_and_the_start_goes_on()
    {
        // An unhandled run-time error stops its event procedure and leaves the run going, as in any event today. VB6
        // halts the run there instead (debugger-vb6-divergences D23).
        var run = RunForm("Private Sub Form_Initialize()\n    Debug.Print 1 / 0\nEnd Sub\n"
            + "Private Sub Form_Load()\n    Debug.Print \"load\"\nEnd Sub\n");

        run.Errors.Should().ContainSingle().Which.Should().BeOfType<VBRunTimeException>();
        run.Printed.Should().Equal("load");
        run.Shown.Should().BeTrue();
    }

    [AvaloniaFact]
    public void A_control_event_raised_from_Form_Load_runs_inside_it()
    {
        // Measured: `load start`, `change x`, `load end`. Load runs before the show now, when the window's content is not
        // in the visual tree yet, and the event found no form through it (#1010 review).
        var run = RunForm("Private Sub Form_Load()\n    Debug.Print \"load start\"\n    Text1.Text = \"x\"\n    Debug.Print \"load end\"\nEnd Sub\nPrivate Sub Text1_Change()\n    Debug.Print \"change \" & Text1.Text\nEnd Sub\n");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("load start", "change x", "load end");
    }

    [AvaloniaFact]
    public void A_forms_declarations_of_VBs_own_types_and_of_other_forms_are_Nothing_until_Set()
    {
        // Measured, with Form2 in the project: every TypeName is Nothing but OLE_COLOR's, a Long, and each object is Nothing.
        // A form's declarations never ran before #822; once they did, each of these refused the start (#1010 review).
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddForm(new FormDeserializer().Deserialize(project, OtherForm, new NullSink())!);

        var run = RunFormIn(project, "Private mParent As Form\nPrivate mObj As Object\nPrivate mCol As Collection\nPrivate mFont As StdFont\nPrivate mColour As OLE_COLOR\nPrivate mText As TextBox\nPrivate mOther As Form2\nPrivate mSelf As Form1\nPrivate mPic As IPictureDisp\nPrivate mCtl As Control\nPrivate Sub Form_Load()\n    Debug.Print TypeName(mParent) & \" \" & TypeName(mObj) & \" \" & TypeName(mCol) & \" \" & TypeName(mFont) & \" \" & TypeName(mColour) & \" \" & TypeName(mText) & \" \" & TypeName(mOther) & \" \" & TypeName(mSelf) & \" \" & TypeName(mPic) & \" \" & TypeName(mCtl)\n    Debug.Print (mParent Is Nothing) & \" \" & (mObj Is Nothing) & \" \" & (mCol Is Nothing) & \" \" & (mOther Is Nothing) & \" \" & (mCtl Is Nothing)\nEnd Sub\n");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Nothing Nothing Nothing Nothing Long Nothing Nothing Nothing Nothing Nothing", "True True True True True");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_declaration_of_a_type_nothing_declares_ends_the_start_unless_a_library_may_declare_it(bool referencesALibrary)
    {
        // With only the in-box libraries, a name nobody declares is VB6's "User-defined type not defined", and the start
        // ends there: Load would run on declarations that never were (#1010 review). With another library referenced, the
        // name may be that library's, which HexIDE can't read, so it is an object slot, Nothing until Set.
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        if (referencesALibrary)
            project.AddReference(new VbReference("{00000201-0000-0010-8000-00AA006D2EA4}", "2.1", 0, null, "Microsoft ActiveX Data Objects 2.1 Library"));

        var run = RunFormIn(project, "Private mConn As Connection\nPrivate Sub Form_Load()\n    Debug.Print \"load \" & TypeName(mConn)\nEnd Sub\n");

        if (referencesALibrary)
        {
            run.Errors.Should().BeEmpty();
            run.Printed.Should().Equal("load Nothing");
            run.Shown.Should().BeTrue();
        }
        else
        {
            run.Errors.Should().ContainSingle().Which.Should().BeOfType<VBCompileErrorException>()
                .Which.Message.Should().Contain("User-defined type not defined");
            run.Printed.Should().BeEmpty();
            run.Shown.Should().BeFalse();
        }
    }

    [AvaloniaFact]
    public void A_qualified_type_from_a_library_HexIDE_cant_read_is_Nothing_until_Set()
    {
        var run = RunForm("Private mConn As ADODB.Connection\nPrivate Sub Form_Load()\n    Debug.Print TypeName(mConn)\nEnd Sub\n");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Nothing");
    }

    [AvaloniaFact]
    public void Setting_the_forms_name_to_Nothing_from_a_module_leaves_the_loaded_form_alive()
    {
        // Measured: `dropped`, then `after drop: 5 Form1`, and Terminate only after the unload. A loaded form is held by
        // more than its name; that Set used to terminate the instance under its own window (#1010 review).
        var run = RunForm("Public Score As Long\nPrivate WithEvents mSrc As Source\nPrivate Sub mSrc_ResultV(ByVal r As Integer)\n    Debug.Print \"got \" & r\nEnd Sub\nPrivate Sub Form_Load()\n    Score = 5\n    Set mSrc = New Source\n    Drop\n    Debug.Print \"after drop: \" & Me.Score & \" \" & TypeName(Me)\n    Debug.Print \"x \" & mSrc.FireV()\nEnd Sub\n",
            ("Module1", ModuleKind.StandardModule, "Sub Drop()\n    Set Form1 = Nothing\n    Debug.Print \"dropped\"\nEnd Sub\n"),
            ("Source", ModuleKind.ClassModule, Source));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("dropped", "after drop: 5 Form1", "got 3", "x 3");
    }

    private const string OtherForm =
        "VERSION 5.00\r\n"
      + "Begin VB.Form Form2 \r\n"
      + "   Caption         =   \"Form2\"\r\n"
      + "End\r\n"
      + "Attribute VB_Name = \"Form2\"\r\n";

    // ── Implicit Me: a bare name is the form's (#995) ───────────────────────────────────────────────────

    [AvaloniaFact]
    public void A_bare_Caption_is_the_forms_and_so_is_a_bare_Top()
    {
        // L1, measured: `set by form` twice, then True. A bare Caption used to make a new variable.
        var run = RunFormLoad("    Caption = \"set by form\"\n    Debug.Print Caption & \"|\" & Me.Caption & \"|\" & (Top = Me.Top)");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("set by form|set by form|True");
    }

    [AvaloniaFact]
    public void A_bare_Caption_is_the_forms_where_a_module_declares_a_Public_of_the_name()
    {
        // L2, measured: the form's member comes before the project's globals.
        var run = RunFormLoad("    Caption = \"set by form\"\n    Debug.Print Me.Caption & \" [\" & Module1.Caption & \"]\"",
            ("Module1", ModuleKind.StandardModule, "Public Caption As String\n"));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("set by form []");
    }

    [AvaloniaFact]
    public void A_forms_code_reads_and_writes_a_modules_Public_names_bare()
    {
        // Measured: `0 3`, then 4 (#880). A name that is not one of the form's members is the module's.
        var run = RunFormLoad("    Debug.Print G & \" \" & M\n    G = 4\n    Debug.Print \"\" & Module1.G",
            ("Module1", ModuleKind.StandardModule, "Public G As Long\nPublic Const M = 3\n"));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("0 3", "4");
    }

    [AvaloniaFact]
    public void A_bare_Tag_is_the_forms_where_a_module_has_a_Function_of_the_name()
    {
        // L3, measured.
        var run = RunFormLoad("    Debug.Print \"[\" & Tag & \"] \" & Module1.Tag()",
            ("Module1", ModuleKind.StandardModule, "Public Function Tag() As String\n    Tag = \"module tag\"\nEnd Function\n"));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("[] module tag");
    }

    [AvaloniaFact]
    public void A_bare_Name_and_Left_are_the_forms_and_Left_with_arguments_is_VBAs()
    {
        // L6, L7 and their combination, measured: `Form1 ab True`. A form property takes no arguments, so a call with
        // some reaches VBA.
        var run = RunFormLoad("    Debug.Print Name & \" \" & Left(\"abc\", 2) & \" \" & (Left = Me.Left)");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("Form1 ab True");
    }

    [AvaloniaFact]
    public void A_bare_Caption_is_the_forms_under_Option_Explicit()
    {
        // Measured: `set|set`. It used to be "Variable not defined".
        var run = RunForm("Option Explicit\nPrivate Sub Form_Load()\n    Caption = \"set\"\n    Debug.Print Caption & \"|\" & Me.Caption\nEnd Sub\n");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("set|set");
    }

    [AvaloniaFact]
    public void A_local_shadows_the_forms_member()
    {
        // Measured: `local|Form1`.
        var run = RunFormLoad("    Dim Caption As String\n    Caption = \"local\"\n    Debug.Print Caption & \"|\" & Me.Caption");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("local|Form1");
    }

    [AvaloniaFact]
    public void A_bare_name_the_Form_lacks_is_still_a_new_variable()
    {
        var run = RunFormLoad("    Score = 5\n    Debug.Print Score");

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("5");
    }

    [AvaloniaFact]
    public void A_modules_bare_Caption_is_its_own_variable_not_the_forms()
    {
        var run = RunFormLoad("    SetIt",
            ("Module1", ModuleKind.StandardModule, "Sub SetIt()\n    Caption = \"mod\"\n    Debug.Print Caption & \"|\" & Form1.Caption\nEnd Sub\n"));

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("mod|Form1");
    }

    [AvaloniaTheory]
    [InlineData("    hWnd = 5")]
    [InlineData("    Count = 1")]
    [InlineData("    Me.HasDC = True")]
    public void Assigning_to_a_read_only_member_of_the_form_is_VB6s_compile_error(string statement)
    {
        // Measured for hWnd, hDC, Count and HasDC, bare and through Me: this message, not a read-only-property one.
        var run = RunFormLoad(statement + "\n    Debug.Print \"ran\"");

        run.Errors.Should().ContainSingle().Which.Should().BeOfType<VBCompileErrorException>()
            .Which.Message.Should().Contain("Function or interface marked as restricted");
        run.Printed.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData("    CurrentX = 0", "Setting Form1's CurrentX")]
    [InlineData("    Debug.Print ScaleMode", "Reading Form1's ScaleMode")]
    [InlineData("    Debug.Print Me.ScaleMode", "Reading Form1's ScaleMode")]
    [InlineData("    Cls", "Cls method")]
    [InlineData("    Debug.Print TextWidth(\"abc\")", "TextWidth method")]
    [InlineData("    Debug.Print Controls(0).Name", "Form1's Controls with arguments")]
    public void A_member_VB6s_Form_has_and_HexIDE_lacks_is_refused_as_not_implemented(string statement, string named)
    {
        // VB6 has each (measured where it matters: Cls runs, TextWidth is a Single, Controls is an object). Quietly making a
        // variable of CurrentX, or saying "Sub or Function not defined" for Cls, would hide that HexIDE lacks it.
        var run = RunFormLoad(statement + "\n    Debug.Print \"ran\"");

        run.Errors.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>()
            .Which.Message.Should().Contain(named);
        run.Printed.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData("    Debug.Print Caption(1)")]
    [InlineData("    Debug.Print Left(5)")]
    [InlineData("    Debug.Print Left(\"abc\", 1, 2)")]
    public void A_property_of_the_form_called_with_arguments_VBA_cant_take_is_never_a_project_procedure(string statement)
    {
        // Measured: each is "Wrong number of arguments or invalid property assignment", the first though Module1 declares
        // `Function Caption(n)`. VBA's Left takes the call only with its two arguments. Running Module1's Caption would be a
        // wrong result; HexIDE refuses instead, as not implemented, since which error VB6 gives depends on the property's type.
        var run = RunFormLoad(statement + "\n    Debug.Print \"ran\"",
            ("Module1", ModuleKind.StandardModule, "Public Function Caption(ByVal n As Integer) As String\n    Caption = \"module \" & n\nEnd Function\n"));

        run.Errors.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>()
            .Which.Message.Should().Contain("with arguments");
        run.Printed.Should().BeEmpty();
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
            names.Should().NotContain("Form1", "the form's name is how it is reached, not one of its variables");
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

    [AvaloniaFact]
    public void Locals_in_a_modules_procedure_does_not_list_the_form()
    {
        // The form's name is seeded into every module, as Debug and Err are, so that Form1.X resolves anywhere. VB6 lists
        // neither under a module, and nor does Locals.
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project,
            (FormHeader + "Private Sub Form_Load()\n    Pause\nEnd Sub\n").ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form);
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Public G As Long\r\nSub Pause()\r\n    Dim local As Long\r\n    G = 1\r\n    Stop\r\nEnd Sub\r\n");
        project.AddModule(module);
        var dbg = new HexIDE.Runtime.Debugging.DebugController();
        dbg.Reset();

        var run = VBLoader.RunForm(form, CancellationToken.None, out var window, dbg);
        try
        {
            Dispatcher.UIThread.RunJobs();
            dbg.State.Should().Be(HexIDE.Runtime.Debugging.DebugState.Paused);
            var locals = dbg.GetLocals()!.Locals;
            locals.Select(n => n.Name).Should().Equal("Module1", "local");
            locals[0].Expand().Select(n => n.Name).Should().Equal("G");
        }
        finally
        {
            dbg.Stop();
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }
        run.IsCompleted.Should().BeTrue();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Step_Into_from_design_mode_breaks_on_the_first_statement_of_the_start(bool hasInitialize)
    {
        // VB6 breaks on the first executable line, never on a declaration. The start is one dispatch now: a step armed
        // before it reaches Initialize's first statement, or Load's when there is no Initialize, and a step off the end
        // of Initialize goes on into Load. Each piece was a dispatch of its own, and the first one's end disarmed the
        // step, so the run went straight through (#656, #1010 review).
        var code = "Private Score As Integer\nDim Grid(1 To 3) As Integer\n"
            + (hasInitialize ? "Private Sub Form_Initialize()\n    Score = 1\nEnd Sub\n" : "")
            + "Private Sub Form_Load()\n    Score = Score + 1\n    Debug.Print Score\nEnd Sub\n";
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, (FormHeader + code).ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form);
        // A stop is reported on its line counted from the top of the file, header included (#273), so each expected
        // line is found in the whole file rather than counted from the code.
        var whole = FormCodeText.WholeFile(form).ReplaceLineEndings("\n").Split('\n');
        int LineOf(string statement) => System.Array.IndexOf(whole, statement) + 1;
        var dbg = new HexIDE.Runtime.Debugging.DebugController { TimeSliceMs = null };
        var stops = new List<int>();
        dbg.Stopped += info => stops.Add(info.Line);
        dbg.Reset();
        dbg.StepInto();

        var run = VBLoader.RunForm(form, CancellationToken.None, out var window, dbg);
        try
        {
            Dispatcher.UIThread.RunJobs();
            if (hasInitialize)
            {
                stops.Should().Equal([LineOf("    Score = 1")], "Initialize's first statement, after the two declarations and its Sub line");
                dbg.StepInto();
                Dispatcher.UIThread.RunJobs();
                stops.Should().Equal([LineOf("    Score = 1"), LineOf("    Score = Score + 1")],
                    "off the end of Initialize, into Load's first statement");
            }
            else
                stops.Should().Equal([LineOf("    Score = Score + 1")], "Load's first statement: the declarations are never a stop");
        }
        finally
        {
            dbg.Stop();
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }
        run.IsCompleted.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task The_Immediate_window_reads_a_bare_member_as_the_forms()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var code = "Private Sub Form_Load()\n    Caption = \"paused\"\n    Stop\nEnd Sub\n";
        var form = new FormDeserializer().Deserialize(project, (FormHeader + code).ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form);
        var dbg = new HexIDE.Runtime.Debugging.DebugController { TimeSliceMs = null };
        dbg.Reset();

        var run = VBLoader.RunForm(form, CancellationToken.None, out var window, dbg);
        try
        {
            Dispatcher.UIThread.RunJobs();
            dbg.State.Should().Be(HexIDE.Runtime.Debugging.DebugState.Paused);
            (await dbg.EvaluateAsync("Caption")).Should().Be("paused");
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
