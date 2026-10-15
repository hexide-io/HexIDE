using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A made program, as the Standalone host runs it (#1062): the project read from disk by the loader the IDE's open uses,
/// then started by <see cref="VBLoader.RunMadeProgram"/>. Before, the host read the forms alone into a list the run never
/// saw, so a module's procedure, a second form and Sub Main were all out of reach.
/// </summary>
public sealed class MadeProgramTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "hexide-made-" + Guid.NewGuid().ToString("N"));

    public MadeProgramTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private void Write(string name, string text) =>
        File.WriteAllText(Path.Combine(dir, name), text.ReplaceLineEndings("\r\n"), Encoding.ASCII);

    private static string Form(string name, string code) => $"""
        VERSION 5.00
        Begin VB.Form {name}
           Caption         =   "{name}"
           ClientHeight    =   3000
           ClientWidth     =   4000
           Begin VB.CommandButton Command1
              Caption         =   "Command1"
              Height          =   495
              Left            =   120
              Top             =   120
              Width           =   1215
           End
        End
        Attribute VB_Name = "{name}"

        """ + code;

    private const string Module1 = """
        Attribute VB_Name = "Module1"
        Public Function Twice(ByVal n As Integer) As Integer
            Twice = n * 2
        End Function

        """;

    private const string Class1 = """
        VERSION 1.0 CLASS
        BEGIN
          MultiUse = -1  'True
        END
        Attribute VB_Name = "Class1"
        Attribute VB_GlobalNameSpace = False
        Attribute VB_Creatable = True
        Attribute VB_PredeclaredId = False
        Attribute VB_Exposed = False
        Public Function Hello() As String
            Hello = "hello"
        End Function

        """;

    private const string Form2Code = """
        Private Sub Form_Load()
            Debug.Print "f2 load"
        End Sub

        """;

    private async Task<ProjectDefinition> Load(string startup, string form1Code, string? module1 = Module1)
    {
        Write("Form1.frm", Form("Form1", form1Code));
        Write("Form2.frm", Form("Form2", Form2Code));
        Write("Class1.cls", Class1);
        if (module1 is not null)
            Write("Module1.bas", module1);
        Write("Made.vbp", $"""
            Type=Exe
            Form=Form1.frm
            Form=Form2.frm
            Module=Module1; Module1.bas
            Class=Class1; Class1.cls
            Startup="{startup}"
            Name="Made"

            """);
        return (await new ProjectLoader().LoadAsync(Path.Combine(dir, "Made.vbp"), new NullSink())).Project;
    }

    private sealed class Run : IDisposable
    {
        public required Task Task { get; init; }
        public required CancellationTokenSource Cancel { get; init; }
        public required List<string> Printed { get; init; }
        public required List<Exception> Errors { get; init; }
        public required Dictionary<string, VBFormRuntime> Opened { get; init; }
        public required Action Detach { get; init; }

        public void Click(string form) => Opened[form].GetLogicalDescendants().OfType<VBCommandButton>().Single()
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        public void Dispose()
        {
            Cancel.Cancel();
            Pump();
            Detach();
        }
    }

    private static Run Start(ProjectDefinition project)
    {
        var printed = new List<string>();
        var errors = new List<Exception>();
        var opened = new Dictionary<string, VBFormRuntime>();
        Action<string> print = printed.Add;
        void OnFaulted(VBWindowContext _, Exception e) => errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => errors.Add(e);
        void OnCompileError(VBWindowContext _, VBCompileErrorException e) => errors.Add(e);
        VBDebugConsole.Output += print;
        VBWindowContext.CompileError += OnCompileError;
        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        var watch = Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) => opened[w.Title ?? ""] = w);
        var cancel = new CancellationTokenSource();
        var task = VBLoader.RunMadeProgram(project, cancel.Token, out _);
        Pump();
        return new Run
        {
            Task = task,
            Cancel = cancel,
            Printed = printed,
            Errors = errors,
            Opened = opened,
            Detach = () =>
            {
                watch.Dispose();
                VBDebugConsole.Output -= print;
                VBWindowContext.Faulted -= OnFaulted;
                VBWindowContext.RunTimeError -= OnRunTimeError;
                VBWindowContext.CompileError -= OnCompileError;
            },
        };
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task The_startup_form_reaches_a_modules_procedure_a_class_and_another_form()
    {
        var project = await Load("Form1", """
            Private Sub Form_Load()
                Dim c As Class1
                Set c = New Class1
                Debug.Print "twice " & Twice(21) & ", " & c.Hello()
            End Sub
            Private Sub Command1_Click()
                Form2.Show
            End Sub

            """);
        using var run = Start(project);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("twice 42, hello");
        run.Click("Form1");
        Pump();

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("twice 42, hello", "f2 load");
        run.Opened.Should().ContainKey("Form2");
    }

    [AvaloniaFact]
    public async Task A_program_that_starts_at_Sub_Main_runs_it_and_lasts_until_its_form_unloads()
    {
        var project = await Load("Sub Main", """
            Private Sub Command1_Click()
                Unload Me
            End Sub

            """, Module1 + """
            Sub Main()
                Debug.Print "main " & Twice(4)
                Form1.Show
                Debug.Print "main end"
            End Sub

            """);
        using var run = Start(project);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("main 8", "main end");
        run.Opened.Should().ContainKey("Form1");
        run.Task.IsCompleted.Should().BeFalse("Form1 is loaded, though Main has returned (measured)");

        run.Click("Form1");
        Pump();

        run.Task.IsCompleted.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task A_project_with_neither_a_startup_form_nor_Sub_Main_is_refused_before_anything_runs()
    {
        var project = await Load("Sub Main", "", module1: """
            Attribute VB_Name = "Module1"
            Public Sub NotMain()
            End Sub

            """);

        Action start = () => VBLoader.RunMadeProgram(project, CancellationToken.None, out _);

        start.Should().Throw<VBCompileErrorException>().Which.Message.Should().Contain(BasicInterpreter.NoStartupObject);
    }
}
