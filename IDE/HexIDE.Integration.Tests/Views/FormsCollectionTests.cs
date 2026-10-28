using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The global <c>Forms</c> (#2496), in a program of several forms run as F5 runs it. Every expectation was measured on vb6.exe,
/// native and p-code agreeing: the oracle's "The Forms collection". The forms and the module are the probe's, logging with
/// Debug.Print where the probe wrote a file.
/// </summary>
/// <remarks>The probe's rows for a second instance made with <c>New Form2</c> are left out: HexIDE makes no form with New yet
/// (#822).</remarks>
public class FormsCollectionTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static string Header(string name) =>
        "VERSION 5.00\r\n"
      + $"Begin VB.Form {name} \r\n"
      + $"   Caption         =   \"{name}\"\r\n"
      + "   ClientHeight    =   3000\r\n"
      + "   ClientWidth     =   4000\r\n"
      + "   Begin VB.CommandButton Command1 \r\n"
      + "      Caption         =   \"Command1\"\r\n"
      + "      Height          =   495\r\n"
      + "      Left            =   120\r\n"
      + "      Top             =   120\r\n"
      + "      Width           =   1215\r\n"
      + "   End\r\n"
      + "End\r\n"
      + $"Attribute VB_Name = \"{name}\"\r\n";

    // The probe's module: Forms' contents in order, a hidden form marked (h); whether a form is among them; and which form a
    // loop is on, found by Is, which neither loads nor makes anything.
    private const string Helpers = """
        Public Function Names() As String
            Dim i As Integer, s As String
            For i = 0 To Forms.Count - 1
                s = s & IIf(i > 0, ",", "") & Forms(i).Caption & IIf(Forms(i).Visible, "", "(h)")
            Next
            Names = Forms.Count & " [" & s & "]"
        End Function
        Public Function InForms(x As Object) As String
            Dim f As Object
            For Each f In Forms
                If f Is x Then InForms = "yes": Exit Function
            Next
            InForms = "no"
        End Function
        Public Function Who(x As Object) As String
            If x Is Form1 Then Who = "Form1": Exit Function
            If x Is Form2 Then Who = "Form2": Exit Function
            If x Is Form3 Then Who = "Form3": Exit Function
            Who = "?"
        End Function
        Public Sub Reset3()
            Dim i As Integer
            For i = Forms.Count - 1 To 0 Step -1
                Unload Forms(i)
            Next
            Load Form1
            Load Form2
            Load Form3
            Debug.Print "reset: " & Names()
        End Sub

        """;

    private static string Lifecycle(string name) => $$"""
        Public pub As Integer
        Public cancelNext As Boolean
        Private Sub Form_Initialize()
            Debug.Print "{{name}} initialize: " & Names() & ", in Forms " & InForms(Me)
        End Sub
        Private Sub Form_Load()
            Debug.Print Caption & " load: " & Names()
        End Sub
        Private Sub Form_QueryUnload(Cancel As Integer, UnloadMode As Integer)
            Debug.Print Caption & " queryunload " & UnloadMode & ": " & Names() & ", in Forms " & InForms(Me)
            If cancelNext Then Cancel = True: cancelNext = False: Debug.Print Caption & " cancelled"
        End Sub
        Private Sub Form_Unload(Cancel As Integer)
            Debug.Print Caption & " unload: " & Names() & ", in Forms " & InForms(Me)
        End Sub

        """;

    // The idioms' forms log only their unloads, and a cancelled QueryUnload.
    private static string Unloads(string name) => """
        Public pub As Integer
        Public cancelNext As Boolean
        Private Sub Form_QueryUnload(Cancel As Integer, UnloadMode As Integer)
            If cancelNext Then Cancel = True: cancelNext = False: Debug.Print Caption & " cancelled"
        End Sub
        Private Sub Form_Unload(Cancel As Integer)
            Debug.Print Caption & " unload"
        End Sub

        """;

    private sealed class Run : IDisposable
    {
        public required ProjectRunnerService Runner { get; init; }
        public required List<string> Printed { get; init; }
        public required List<string> Failed { get; init; }
        public required Dictionary<string, VBFormRuntime> Opened { get; init; }
        public required Action Detach { get; init; }

        public void Click(string form) => Opened[form].GetLogicalDescendants().OfType<VBCommandButton>().Single()
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        public void Dispose()
        {
            Runner.EndProject();
            Pump();
            Detach();
        }
    }

    /// <summary>Run a project of the given forms, Form1 first, and a module: from its Sub Main when it has one.</summary>
    private static Run Start(string moduleCode, IReadOnlyList<(string Name, string Code)> forms)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        foreach (var (name, code) in forms)
            project.AddForm(new FormDeserializer().Deserialize(project, (Header(name) + code).ReplaceLineEndings("\r\n"), new NullSink())!);
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode(moduleCode);
        project.AddModule(module);
        project.StartsAtSubMain = moduleCode.Contains("Sub Main()", StringComparison.Ordinal);

        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() + ": {0}");
        var runner = new ProjectRunnerService(
            Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), Substitute.For<IProjectManager>(),
            localization, new DebugController(), Substitute.For<IBreakpointService>(), new WatchService(), new RunScope());

        var printed = new List<string>();
        var failed = new List<string>();
        var opened = new Dictionary<string, VBFormRuntime>();
        Action<string> print = printed.Add;
        VBDebugConsole.Output += print;
        runner.StartFailed += failed.Add;
        runner.RunFailed += failed.Add;
        var watch = Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) => opened[w.Title ?? ""] = w);
        var wasSingleView = HexIDE.Static.SingleView;
        HexIDE.Static.SingleView = false;
        try
        {
            runner.RunProject(project);
        }
        finally
        {
            HexIDE.Static.SingleView = wasSingleView;
        }
        Pump();
        return new Run
        {
            Runner = runner,
            Printed = printed,
            Failed = failed,
            Opened = opened,
            Detach = () =>
            {
                watch.Dispose();
                VBDebugConsole.Output -= print;
            },
        };
    }

    private static Run StartMain(string main, Func<string, string> formCode, params string[] names) =>
        Start(Helpers + main, names.Select(n => (n, formCode(n))).ToList());

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Forms_holds_the_loaded_forms_in_the_order_they_loaded_and_a_form_from_its_own_Load_to_the_end_of_its_Unload()
    {
        using var run = StartMain("""
            Sub Main()
                Debug.Print "start: " & Names() & ", " & TypeName(Forms) & ", " & TypeName(Forms.Count)
                Load Form2
                Load Form1
                Form3.Show
                Debug.Print "shown: " & Names()
                Form4.pub = 1
                Debug.Print "made, not loaded: " & Names()
                Form1.Show
                Form1.Hide
                Debug.Print "shown and hidden: " & Names()
                Unload Form2
                Debug.Print "unloaded: " & Names()
                Load Form2
                Debug.Print "loaded again: " & Names() & ", " & TypeName(Forms(0))
            End Sub

            """, Lifecycle, "Form1", "Form2", "Form3", "Form4");

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal(
            "start: 0 [], Object, Integer",
            "Form2 initialize: 0 [], in Forms no",
            "Form2 load: 1 [Form2(h)]",
            "Form1 initialize: 1 [Form2(h)], in Forms no",
            "Form1 load: 2 [Form2(h),Form1(h)]",
            "Form3 initialize: 2 [Form2(h),Form1(h)], in Forms no",
            "Form3 load: 3 [Form2(h),Form1(h),Form3(h)]",
            "shown: 3 [Form2(h),Form1(h),Form3]",
            "Form4 initialize: 3 [Form2(h),Form1(h),Form3], in Forms no",
            "made, not loaded: 3 [Form2(h),Form1(h),Form3]",
            "shown and hidden: 3 [Form2(h),Form1(h),Form3]",
            "Form2 queryunload 1: 3 [Form2(h),Form1(h),Form3], in Forms yes",
            "Form2 unload: 3 [Form2(h),Form1(h),Form3], in Forms yes",
            "unloaded: 2 [Form1(h),Form3]",
            "Form2 load: 3 [Form1(h),Form3,Form2(h)]",
            "loaded again: 3 [Form1(h),Form3,Form2(h)], Form1");
    }

    [AvaloniaFact]
    public void An_index_converts_as_CInt_converts_and_there_are_no_keys()
    {
        using var run = StartMain("""
            Sub Main()
                Dim v As Variant, o As Object
                On Error Resume Next
                Load Form1: Load Form2: Load Form3
                Err.Clear: v = Forms(Forms.Count).Caption: Debug.Print "Forms(Count) " & Err.Number
                Err.Clear: v = Forms(-1).Caption: Debug.Print "Forms(-1) " & Err.Number
                Err.Clear: v = "": v = Forms(1.5).Caption: Debug.Print "Forms(1.5) " & Err.Number & " " & v
                Err.Clear: v = "": v = Forms(2.5).Caption: Debug.Print "Forms(2.5) " & Err.Number & " " & v
                Err.Clear: v = "": v = Forms("1").Caption: Debug.Print "Forms(""1"") " & Err.Number & " " & v
                Err.Clear: v = Forms("Form1").Caption: Debug.Print "Forms(""Form1"") " & Err.Number
                Err.Clear: v = Forms("").Caption: Debug.Print "Forms(empty) " & Err.Number
                Err.Clear: v = Forms(Null).Caption: Debug.Print "Forms(Null) " & Err.Number
                Err.Clear: v = Forms(70000).Caption: Debug.Print "Forms(70000) " & Err.Number
                Err.Clear: v = "": v = Forms.Item(0).Caption: Debug.Print "Forms.Item(0) " & Err.Number & " " & v
                Err.Clear: v = Forms!Form1.Caption: Debug.Print "Forms!Form1 " & Err.Number
                Err.Clear: Set o = Forms: Debug.Print "Set o = Forms " & Err.Number & " " & TypeName(o) & " " & o.Count
                Err.Clear: v = "": v = o(0).Caption: Debug.Print "o(0) " & Err.Number & " " & v
                Err.Clear: o.Add "x": Debug.Print "o.Add " & Err.Number
                Err.Clear: o.Remove 0: Debug.Print "o.Remove " & Err.Number
                Err.Clear: v = Empty: v = Forms: Debug.Print "v = Forms " & Err.Number & " " & TypeName(v)
                Err.Clear: v = Forms.Count: Debug.Print "v = Forms.Count " & Err.Number & " " & TypeName(v) & " " & v
                Debug.Print "TypeOf " & (TypeOf Forms Is Object)
            End Sub

            """, _ => "", "Form1", "Form2", "Form3");

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal(
            "Forms(Count) 9",
            "Forms(-1) 9",
            "Forms(1.5) 0 Form3",
            "Forms(2.5) 0 Form3",
            "Forms(\"1\") 0 Form2",
            "Forms(\"Form1\") 13",
            "Forms(empty) 13",
            "Forms(Null) 13",
            "Forms(70000) 6",
            "Forms.Item(0) 0 Form1",
            "Forms!Form1 13",
            "Set o = Forms 0 Object 3",
            "o(0) 0 Form1",
            "o.Add 424",
            "o.Remove 438",
            "v = Forms 450 Empty",
            "v = Forms.Count 0 Integer 3",
            "TypeOf True");
    }

    [AvaloniaFact]
    public void Unloading_by_index_from_0_leaves_every_other_form_and_then_raises_9()
    {
        using var run = StartMain("""
            Sub Main()
                Dim i As Integer, s As String
                On Error Resume Next
                Reset3
                For i = 0 To Forms.Count - 1
                    Err.Clear
                    s = "?": s = Forms(i).Caption
                    Unload Forms(i)
                    Debug.Print "i " & i & " (" & s & "): " & Err.Number & " -> " & Names()
                Next
            End Sub

            """, Unloads, "Form1", "Form2", "Form3");

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal(
            "reset: 3 [Form1(h),Form2(h),Form3(h)]",
            "Form1 unload",
            "i 0 (Form1): 0 -> 2 [Form2(h),Form3(h)]",
            "Form3 unload",
            "i 1 (Form3): 0 -> 1 [Form2(h)]",
            "i 2 (?): 9 -> 1 [Form2(h)]");
    }

    [AvaloniaFact]
    public void Unloading_by_index_from_the_end_unloads_every_form()
    {
        using var run = StartMain("""
            Sub Main()
                Dim i As Integer
                Reset3
                For i = Forms.Count - 1 To 0 Step -1
                    Unload Forms(i)
                Next
                Debug.Print "after: " & Names()
            End Sub

            """, Unloads, "Form1", "Form2", "Form3");

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal(
            "reset: 3 [Form1(h),Form2(h),Form3(h)]", "Form3 unload", "Form2 unload", "Form1 unload", "after: 0 []");
    }

    [AvaloniaFact]
    public void For_Each_unloads_every_form_in_order_and_leaves_its_variable_Nothing()
    {
        using var run = StartMain("""
            Sub Main()
                Dim f As Form
                Reset3
                For Each f In Forms
                    Debug.Print "visit " & Who(f)
                    Unload f
                Next
                Debug.Print "after: " & Names() & ", Nothing " & (f Is Nothing)
            End Sub

            """, Unloads, "Form1", "Form2", "Form3");

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal(
            "reset: 3 [Form1(h),Form2(h),Form3(h)]",
            "visit Form1", "Form1 unload", "visit Form2", "Form2 unload", "visit Form3", "Form3 unload",
            "after: 0 [], Nothing True");
    }

    [AvaloniaFact]
    public void For_Each_walks_the_forms_held_when_it_began()
    {
        using var run = StartMain("""
            Sub Main()
                Dim f As Form, i As Integer
                Reset3
                For Each f In Forms
                    Debug.Print "visit " & Who(f)
                    If i = 0 Then Unload Forms(Forms.Count - 1): Debug.Print "the last unloaded: " & Names()
                    i = i + 1
                Next
                Reset3
                i = 0
                For Each f In Forms
                    Debug.Print "visit " & Who(f)
                    If i = 0 Then Load Form4: Debug.Print "Form4 loaded: " & Names()
                    i = i + 1
                Next
                For Each f In Forms
                    Exit For
                Next
                Debug.Print "Exit For leaves " & Who(f)
            End Sub

            """, Unloads, "Form1", "Form2", "Form3", "Form4");

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal(
            "reset: 3 [Form1(h),Form2(h),Form3(h)]",
            "visit Form1",
            "Form3 unload",
            "the last unloaded: 2 [Form1(h),Form2(h)]",
            "visit Form2",
            // Handed out though unloaded, where a form's Controls passes one over (measured).
            "visit Form3",
            "Form2 unload",
            "Form1 unload",
            "reset: 3 [Form1(h),Form2(h),Form3(h)]",
            "visit Form1",
            "Form4 loaded: 4 [Form1(h),Form2(h),Form3(h),Form4(h)]",
            "visit Form2",
            "visit Form3",
            "Exit For leaves Form1");
    }

    [AvaloniaFact]
    public void A_cancelled_unload_in_a_For_Each_leaves_the_form_held_with_no_error()
    {
        using var run = StartMain("""
            Sub Main()
                Dim f As Form
                On Error Resume Next
                Reset3
                Form2.cancelNext = True
                For Each f In Forms
                    Err.Clear
                    Unload f
                    Debug.Print Who(f) & ": " & Err.Number & " -> " & Names()
                Next
            End Sub

            """, Unloads, "Form1", "Form2", "Form3");

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal(
            "reset: 3 [Form1(h),Form2(h),Form3(h)]",
            "Form1 unload",
            "Form1: 0 -> 2 [Form2(h),Form3(h)]",
            "Form2 cancelled",
            "Form2: 0 -> 2 [Form2(h),Form3(h)]",
            "Form3 unload",
            "Form3: 0 -> 1 [Form2(h)]");
    }

    // The close-down idiom: Form1, the startup form, shows Form2 and loads Form3, and its own Unload unloads every form.
    private const string Closer = """
        Private Sub Form_Load()
            Form2.Show
            Load Form3
            Debug.Print "Form1 load done: " & Names()
        End Sub
        Private Sub Command1_Click()
            Unload Me
            Debug.Print "after Unload Me: " & Names()
        End Sub
        Private Sub Form_Unload(Cancel As Integer)
            Dim f As Form
            Debug.Print "Form1 unload: " & Names()
            For Each f In Forms
                Debug.Print "visit " & Who(f)
                %UNLOAD%
            Next
            Debug.Print "Form1 unload done: " & Names()
        End Sub

        """;

    [AvaloniaTheory]
    [InlineData("If Not f Is Me Then Unload f")]
    [InlineData("Unload f")]
    public void A_main_forms_Unload_walking_Forms_unloads_every_form_and_ends_the_run(string unload)
    {
        using var run = Start(Helpers, [
            ("Form1", Closer.Replace("%UNLOAD%", unload)),
            ("Form2", Lifecycle("Form2")),
            ("Form3", Lifecycle("Form3")),
        ]);
        run.Failed.Should().BeEmpty();
        run.Printed.Clear();

        run.Click("Form1");
        Pump();

        run.Failed.Should().BeEmpty();
        // An Unload that reaches Form1 itself, inside its own Unload, does nothing: the two loops log the same (measured).
        run.Printed.Should().Equal(
            "Form1 unload: 3 [Form1,Form2,Form3(h)]",
            "visit Form1",
            "visit Form2",
            "Form2 queryunload 1: 3 [Form1,Form2,Form3(h)], in Forms yes",
            "Form2 unload: 3 [Form1,Form2,Form3(h)], in Forms yes",
            "visit Form3",
            "Form3 queryunload 1: 2 [Form1,Form3(h)], in Forms yes",
            "Form3 unload: 2 [Form1,Form3(h)], in Forms yes",
            "Form1 unload done: 1 [Form1]",
            "after Unload Me: 0 []");
        run.Runner.IsRunning.Should().BeFalse();
    }

    [AvaloniaFact]
    public void A_name_the_program_declares_claims_Forms()
    {
        using var run = StartMain("""
            Function Forms(ByVal i As Integer) As String
                Forms = "mine " & i
            End Function
            Sub Main()
                Load Form1
                Debug.Print Forms(0)
            End Sub

            """, _ => "", "Form1");

        run.Failed.Should().BeEmpty();
        run.Printed.Should().Equal("mine 0");
    }
}
