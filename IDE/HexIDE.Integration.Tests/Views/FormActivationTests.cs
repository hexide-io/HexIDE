using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Automation;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// When a running form raises Resize, Activate and Deactivate (#994). Every expectation was measured on vb6.exe in the
/// console, native and p-code agreeing line for line: the oracle's <i>A form's Resize, Activate and Deactivate</i>. Run from
/// <see cref="VBLoader.RunForm"/>, where F5 starts on the desktop.
/// </summary>
/// <remarks>
/// Headless, a window is activated when it is shown and when the program activates it, as on the desktop. Nothing else
/// activates one: the window that was active is not told, and when the active window hides or closes no other is
/// activated, which on the desktop Windows does. So the rows where VB6 activates the next form after a Hide or an Unload
/// are the desktop's, and are checked there.
/// </remarks>
public class FormActivationTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static string Header(string name, bool controls = true) =>
        "VERSION 5.00\r\n"
      + $"Begin VB.Form {name} \r\n"
      + $"   Caption         =   \"{name}\"\r\n"
      + "   ClientHeight    =   3000\r\n"
      + "   ClientWidth     =   4000\r\n"
      + (controls ? "   Begin VB.CommandButton Command1 \r\n"
      + "      Caption         =   \"Command1\"\r\n"
      + "      Height          =   495\r\n"
      + "      Left            =   120\r\n"
      + "      TabIndex        =   0\r\n"
      + "      Top             =   120\r\n"
      + "      Width           =   1215\r\n"
      + "   End\r\n" : "")
      + "End\r\n"
      + $"Attribute VB_Name = \"{name}\"\r\n";

    // Every form logs the events this is about, prefixed f1 or f2.
    private static string Logged(string prefix) => $"""
        Private Sub Form_Initialize()
            Debug.Print "{prefix} initialize"
        End Sub
        Private Sub Form_Load()
            Debug.Print "{prefix} load"
        End Sub
        Private Sub Form_Resize()
            Debug.Print "{prefix} resize"
        End Sub
        Private Sub Form_Activate()
            Debug.Print "{prefix} activate"
        End Sub
        Private Sub Form_Deactivate()
            Debug.Print "{prefix} deactivate"
        End Sub
        Private Sub Form_GotFocus()
            Debug.Print "{prefix} gotfocus"
        End Sub
        Private Sub Form_LostFocus()
            Debug.Print "{prefix} lostfocus"
        End Sub
        Private Sub Form_Unload(Cancel As Integer)
            Debug.Print "{prefix} unload"
        End Sub

        """;

    // Form2's button unloads it.
    private static readonly string Form2Code = Logged("f2") + "Private Sub Command1_Click()\n    Unload Me\nEnd Sub\n";

    private sealed class Run : IDisposable
    {
        public required CancellationTokenSource Cancel { get; init; }
        public required List<string> Printed { get; init; }
        public required List<Exception> Errors { get; init; }
        public required Dictionary<string, VBFormRuntime> Opened { get; init; }
        public required Action Detach { get; init; }

        /// <summary>What was printed since the start's own lines.</summary>
        public List<string> Since(int count) => Printed.Skip(count).ToList();

        public void Click(string form)
        {
            Opened[form].GetLogicalDescendants().OfType<VBCommandButton>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
        }

        public void Dispose()
        {
            Cancel.Cancel();
            Pump();
            Detach();
        }
    }

    private static FormDefinition Form(ProjectDefinition project, string name, string code, bool controls = true) =>
        new FormDeserializer().Deserialize(project, (Header(name, controls) + code).ReplaceLineEndings("\r\n"), new NullSink())!;

    /// <summary>Run Form1, with Form2 in the project, as F5 does, and let its start run.</summary>
    private static Run Start(string form1Code, bool logEvents = true, bool form2Controls = true)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form1 = Form(project, "Form1", (logEvents ? Logged("f1") : "") + form1Code);
        project.AddForm(form1);
        project.AddForm(Form(project, "Form2", Form2Code, form2Controls));

        var printed = new List<string>();
        var errors = new List<Exception>();
        var opened = new Dictionary<string, VBFormRuntime>();
        Action<string> print = printed.Add;
        void OnFaulted(VBWindowContext _, Exception e) => errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => errors.Add(e);
        void OnCompileError(VBWindowContext _, VBCompileErrorException e) => errors.Add(e);
        VBDebugConsole.Output += print;
        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        VBWindowContext.CompileError += OnCompileError;
        var watch = Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) => opened[w.Title ?? ""] = w);
        var cancel = new CancellationTokenSource();
        _ = VBLoader.RunForm(form1, cancel.Token, out _);
        Pump();
        return new Run
        {
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

    private static void NativeSize(VBFormRuntime window, double width, double height)
    {
        var platform = window.PlatformImpl!;
        // Resize() posts asynchronously headlessly; model a native notification with its backend size already updated.
        var size = new Avalonia.Size(width, height);
        platform.GetType().GetProperty("ClientSize")!.SetValue(platform, size);
        var resized = (Action<Avalonia.Size, WindowResizeReason>)platform.GetType().GetProperty("Resized")!.GetValue(platform)!;
        resized(size, WindowResizeReason.User);
    }

    [AvaloniaTheory]
    [InlineData(WindowState.Normal, WindowState.Maximized, true)]
    [InlineData(WindowState.Maximized, WindowState.Normal, false)]
    [InlineData(WindowState.Normal, WindowState.Minimized, false)]
    [InlineData(WindowState.Minimized, WindowState.Normal, true)]
    public async Task A_native_state_change_raises_one_Resize_then_Paint_only_when_the_client_grows(
        WindowState from, WindowState to, bool paint)
    {
        // The automation route uses the same state setter, then waits for the queued window work.
        foreach (var toolRoute in new[] { false, true })
        {
            using var run = Start("""
                Private Sub Form_Resize()
                    Debug.Print "resize " & Me.WindowState
                    If Me.WindowState <> 1 Then Debug.Print "client " & Me.ScaleWidth & "x" & Me.ScaleHeight
                End Sub
                Private Sub Form_Paint()
                    Debug.Print "paint"
                End Sub
                Private Sub Form_Activate()
                    Debug.Print "activate"
                End Sub
                Private Sub Form_Deactivate()
                    Debug.Print "deactivate"
                End Sub
                Private Sub Form_GotFocus()
                    Debug.Print "focus"
                End Sub
                Private Sub Form_LostFocus()
                    Debug.Print "blur"
                End Sub
                """, logEvents: false);
            var window = run.Opened["Form1"];
            window.WindowState = from;
            NativeSize(window, from == WindowState.Maximized ? 1024 : 300, from == WindowState.Maximized ? 705 : 200);
            Pump();
            run.Printed.Clear();

            var applied = toolRoute
                ? WindowChange.ApplyAsync(window, to, null, null, null, null, TestContext.Current.CancellationToken)
                : null;
            if (!toolRoute)
                window.WindowState = to;
            if (to != WindowState.Minimized)
            {
                // A native state change can report intermediate client sizes before it settles.
                NativeSize(window, 600, 400);
                NativeSize(window, to == WindowState.Maximized ? 1024 : 300, to == WindowState.Maximized ? 705 : 200);
            }
            if (from == WindowState.Minimized)
                window.Activate();
            if (applied is not null)
                (await applied).Should().BeTrue();
            Pump();

            run.Errors.Should().BeEmpty();
            var expected = new List<string> { $"resize {(int)to}" };
            if (to != WindowState.Minimized)
                expected.Add(to == WindowState.Maximized ? "client 15360x10575" : "client 4500x3000");
            if (paint)
                expected.Add("paint");
            run.Printed.Should().Equal(expected);
        }
    }
}
