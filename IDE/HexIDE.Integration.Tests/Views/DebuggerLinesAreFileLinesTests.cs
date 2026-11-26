using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A run gives the interpreter each document's whole file, header included, so a line number means the same
/// thing to the debugger as to the code window (hexide-io/HexIDE#273 task 3.6).
/// </summary>
/// <remarks>
/// <para>
/// Since task 3.2 the code window holds the whole file, and breakpoints are counted from its top. The
/// interpreter used to be given the code section alone, so every line it reported was short by the header's
/// length: a breakpoint on a form's first statement never fired, and a syntax error was reported by its line
/// in the code section. Nothing converted between the two, and the design rules out converting: five places
/// exchange line numbers with the interpreter, and one that forgot would stop on the wrong line.
/// </para>
/// <para>
/// These start where F5 starts: <see cref="ProjectRunnerService"/> and <see cref="VBLoader.RunForm"/>, over a
/// form read from text the way a project load reads it, so its designer half is on the model.
/// </para>
/// </remarks>
public class DebuggerLinesAreFileLinesTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    // Line 8 is the Sub; line 9 its first statement. Counted from the top of the file, as the gutter counts.
    private static string FormWith(string statement) =>
        "VERSION 5.00\r\n"                              // 1
      + "Begin VB.Form Form1 \r\n"                      // 2
      + "   Caption         =   \"Form1\"\r\n"          // 3
      + "   ClientHeight    =   3000\r\n"               // 4
      + "   ClientWidth     =   4000\r\n"               // 5
      + "End\r\n"                                       // 6
      + "Attribute VB_Name = \"Form1\"\r\n"             // 7
      + "Private Sub Form_Load()\r\n"                   // 8
      + "    " + statement + "\r\n"                     // 9
      + "End Sub\r\n";                                  // 10

    private static FormDefinition AForm(string statement)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, FormWith(statement), NullSink.Instance)!;
        project.AddForm(form);
        return form;
    }

    [AvaloniaFact]
    public void A_syntax_error_in_the_startup_form_is_reported_on_its_file_line()
    {
        var form = AForm("x = ");
        var windowManager = Substitute.For<IWindowManager>();
        var runner = TestRunners.WithInterpreter(
            Substitute.For<IEventBus>(), windowManager, Substitute.For<IProjectManager>(),
            Substitute.For<ILocalizationService>(), new DebugController(), Substitute.For<IBreakpointService>(),
            new WatchService(), new RunScope());
        // A syntax error at start is a start that never became a run, reported through StartFailed (#759).
        var reported = new List<string>();
        runner.StartFailed += reported.Add;

        runner.RunProject(form.Owner);

        reported.Should().ContainSingle().Which.Should().Contain("line 9:", "the incomplete assignment is on line 9 "
            + "of the file; given the code section alone the parser called it line 3");
    }

    [AvaloniaFact]
    public Task A_standard_module_breakpoint_stops_through_the_project_runner() =>
        ModuleBreakpoint(ModuleKind.StandardModule);

    [AvaloniaFact]
    public Task A_class_module_breakpoint_stops_through_the_project_runner() =>
        ModuleBreakpoint(ModuleKind.ClassModule);

    private static async Task ModuleBreakpoint(ModuleKind kind)
    {
        var form = AForm(kind == ModuleKind.StandardModule
            ? "Helpers.Work"
            : "Dim worker As New Helpers\r\n    worker.Work");
        var module = new ModuleDefinition(form.Owner, "Helpers", kind);
        module.RecordOriginalHeader(kind == ModuleKind.StandardModule
            ? "Attribute VB_Name = \"Helpers\"\r\n"
            : "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1\r\nEND\r\nAttribute VB_Name = \"Helpers\"\r\n");
        module.UpdateCode("Public Sub Work()\r\n    Debug.Print 1\r\nEnd Sub\r\n");
        form.Owner.AddModule(module);
        var line = kind == ModuleKind.StandardModule ? 3 : 7;
        var breakpoints = new BreakpointService();
        breakpoints.Toggle(DocumentIdentity.For(module), line);
        var debugger = new DebugController();
        var stopped = new TaskCompletionSource<StoppedInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        debugger.Stopped += info => stopped.TrySetResult(info);
        var runner = TestRunners.WithInterpreter(
            Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), Substitute.For<IProjectManager>(),
            Substitute.For<ILocalizationService>(), debugger, breakpoints, new WatchService(), new RunScope());
        var failures = new List<string>();
        runner.StartFailed += failures.Add;
        runner.RunFailed += failures.Add;
        try
        {
            runner.RunProject(form.Owner);

            var winner = await Task.WhenAny(stopped.Task, Task.Delay(5000));
            failures.Should().BeEmpty();
            winner.Should().BeSameAs(stopped.Task, "F5 must apply the stored module breakpoint and execute its code");
            var info = await stopped.Task;
            info.Reason.Should().Be(StopReason.Breakpoint);
            info.Module.Should().Be("Helpers");
            info.Line.Should().Be(line);
        }
        finally
        {
            runner.EndProject();
        }
    }
}
