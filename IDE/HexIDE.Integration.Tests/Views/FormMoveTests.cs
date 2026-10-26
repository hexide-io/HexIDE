using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A form's Move (#1569). The program is the probe that ran on vb6.exe SP6, native and p-code alike, less the rows that are
/// the window system's rather than VB6's, and every expected line is what it logged: docs/vb6-fidelity-oracle/a-forms-move.md.
/// The form is loaded and not shown, as there: VB6 raises Resize for each change of a hidden form's size (#994).
/// </summary>
public sealed class FormMoveTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "hexide-formmove-" + Guid.NewGuid().ToString("N"));

    public FormMoveTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Form1 = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           ClientHeight    =   2400
           ClientLeft      =   1200
           ClientTop       =   1200
           ClientWidth     =   3600
           LinkTopic       =   "Form1"
           ScaleHeight     =   2400
           ScaleWidth      =   3600
           StartUpPosition =   0  'Manual
        End
        Attribute VB_Name = "Form1"
        Public Resizes As Long
        Public Quiet As Boolean
        Private Sub Form_Resize()
            If Quiet Then Exit Sub
            Resizes = Resizes + 1
            Debug.Print "  resize " & Me.Width & "x" & Me.Height & " ws" & Me.WindowState
        End Sub
        """;

    private const string Module1 = """"
        Attribute VB_Name = "Module1"
        Public Sub S(ByVal label As String)
            Debug.Print label & " -> " & Form1.Left & "," & Form1.Top & " " & Form1.Width & "x" & Form1.Height & " resizes " & Form1.Resizes & " err " & Err.Number
            Err.Clear: Form1.Resizes = 0
        End Sub
        Public Sub Base()
            Form1.Quiet = True
            Form1.Move 1200, 1200, 6000, 4500
            Form1.Quiet = False
            Err.Clear: Form1.Resizes = 0
        End Sub
        Sub Main()
            On Error Resume Next
            Load Form1
            Base
            Form1.Move 600
            S "Form1.Move 600"
            Base
            Form1.Move 600, 900
            S "Form1.Move 600, 900"
            Base
            Form1.Move 600, 900, 4800
            S "Form1.Move 600, 900, 4800"
            Base
            Form1.Move 600, 900, 4800, 3600
            S "Form1.Move 600, 900, 4800, 3600"
            Base
            Form1.Move 1200, 1200, 6000, 4500
            S "Form1.Move 1200, 1200, 6000, 4500"
            Base
            Form1.Move 1200, 1200, 6000, 3600
            S "Form1.Move 1200, 1200, 6000, 3600"
            Base
            Form1.Move 1200, 1200, 4800, 4500
            S "Form1.Move 1200, 1200, 4800, 4500"
            Base
            Form1.Move 1201, 1207, 4001, 3001
            S "Form1.Move 1201, 1207, 4001, 3001"
            Base
            Form1.Move 600.5, 900.5, 4800.5, 3600.5
            S "Form1.Move 600.5, 900.5, 4800.5, 3600.5"
            Base
            Form1.Move 607.5, 907.5
            S "Form1.Move 607.5, 907.5"
            Base
            Form1.Move -600, -900
            S "Form1.Move -600, -900"
            Base
            Form1.Move 600, 900, -10, 3600
            S "Form1.Move 600, 900, -10, 3600"
            Base
            Form1.Move 600, 900, 4800, -10
            S "Form1.Move 600, 900, 4800, -10"
            Base
            Form1.Move "600", "900"
            S "Form1.Move ""600"", ""900"""
            Base
            Form1.Move 3E+9
            S "Form1.Move 3E+9"
            Base
            Form1.Move 600, 900, 3E+9
            S "Form1.Move 600, 900, 3E+9"
            Base
            Form1.Move Null
            S "Form1.Move Null"
            Base
            Form1.Width = 4800: Form1.Height = 3600
            S "Form1.Width = 4800: Form1.Height = 3600"
            Unload Form1
        End Sub
        """";

    private async Task<(List<string> Printed, List<Exception> Errors)> RunAsync(string module, CancellationToken token)
    {
        File.WriteAllText(Path.Combine(dir, "Form1.frm"), Form1.ReplaceLineEndings("\r\n"), Encoding.ASCII);
        File.WriteAllText(Path.Combine(dir, "Module1.bas"), module.ReplaceLineEndings("\r\n"), Encoding.ASCII);
        File.WriteAllText(Path.Combine(dir, "Probe.vbp"),
            "Type=Exe\r\nForm=Form1.frm\r\nModule=Module1; Module1.bas\r\nStartup=\"Sub Main\"\r\nName=\"Probe\"\r\n", Encoding.ASCII);
        var project = (await new ProjectLoader().LoadAsync(Path.Combine(dir, "Probe.vbp"), new NullSink())).Project;
        var printed = new List<string>();
        var errors = new List<Exception>();
        Action<string> print = printed.Add;
        void OnFaulted(VBWindowContext _, Exception e) => errors.Add(e);
        VBDebugConsole.Output += print;
        VBWindowContext.Faulted += OnFaulted;
        try
        {
            // Sub Main runs to its end, and the run ends with its last form.
            var run = VBLoader.RunMadeProgram(project, token, out _);
            for (var i = 0; i < 1000 && !run.IsCompleted; i++)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }
            run.IsCompleted.Should().BeTrue();
            run.IsFaulted.Should().BeFalse(run.Exception?.ToString());
            return (printed, errors);
        }
        finally
        {
            VBDebugConsole.Output -= print;
            VBWindowContext.Faulted -= OnFaulted;
        }
    }

    [AvaloniaFact]
    public async Task A_forms_Move_answers_as_vb6_exe_did()
    {
        using var cancel = new CancellationTokenSource();
        var (printed, errors) = await RunAsync(Module1, cancel.Token);
        cancel.Cancel();

        errors.Should().BeEmpty();
        printed.Should().Equal(
            "Form1.Move 600 -> 600,1200 6000x4500 resizes 0 err 0",
            "Form1.Move 600, 900 -> 600,900 6000x4500 resizes 0 err 0",
            "  resize 4800x4500 ws0",
            "Form1.Move 600, 900, 4800 -> 600,900 4800x4500 resizes 1 err 0",
            "  resize 4800x3600 ws0",
            "Form1.Move 600, 900, 4800, 3600 -> 600,900 4800x3600 resizes 1 err 0",
            "Form1.Move 1200, 1200, 6000, 4500 -> 1200,1200 6000x4500 resizes 0 err 0",
            "  resize 6000x3600 ws0",
            "Form1.Move 1200, 1200, 6000, 3600 -> 1200,1200 6000x3600 resizes 1 err 0",
            "  resize 4800x4500 ws0",
            "Form1.Move 1200, 1200, 4800, 4500 -> 1200,1200 4800x4500 resizes 1 err 0",
            "  resize 4001x3001 ws0",
            "Form1.Move 1201, 1207, 4001, 3001 -> 1201,1207 4001x3001 resizes 1 err 0",
            "  resize 4800x3600 ws0",
            "Form1.Move 600.5, 900.5, 4800.5, 3600.5 -> 600,900 4800x3600 resizes 1 err 0",
            "Form1.Move 607.5, 907.5 -> 608,908 6000x4500 resizes 0 err 0",
            "Form1.Move -600, -900 -> -600,-900 6000x4500 resizes 0 err 0",
            "Form1.Move 600, 900, -10, 3600 -> 1200,1200 6000x4500 resizes 0 err 5",
            "Form1.Move 600, 900, 4800, -10 -> 1200,1200 6000x4500 resizes 0 err 5",
            "Form1.Move \"600\", \"900\" -> 600,900 6000x4500 resizes 0 err 0",
            "Form1.Move 3E+9 -> 1200,1200 6000x4500 resizes 0 err 6",
            "Form1.Move 600, 900, 3E+9 -> 1200,1200 6000x4500 resizes 0 err 6",
            "Form1.Move Null -> 1200,1200 6000x4500 resizes 0 err 94",
            "  resize 4800x4500 ws0",
            "  resize 4800x3600 ws0",
            "Form1.Width = 4800: Form1.Height = 3600 -> 1200,1200 4800x3600 resizes 2 err 0");
    }
}
