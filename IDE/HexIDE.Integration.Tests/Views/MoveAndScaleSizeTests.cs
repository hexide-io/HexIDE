using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A control's Move, and a form's ScaleWidth and ScaleHeight (#1071), which the Battleship demo's board needs to lay out
/// its grid and fit itself round it. Every expected line is what vb6.exe printed for the same statements: the probe and
/// its log are in docs/vb6-fidelity-oracle.md, "Move, ScaleWidth and ScaleHeight".
/// </summary>
public sealed class MoveAndScaleSizeTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "hexide-move-" + Guid.NewGuid().ToString("N"));

    public MoveAndScaleSizeTests() => Directory.CreateDirectory(dir);

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
           BorderStyle     =   1  'Fixed Single
           Caption         =   "Form1"
           ClientHeight    =   2520
           ClientLeft      =   45
           ClientTop       =   330
           ClientWidth     =   2520
           ScaleHeight     =   2520
           ScaleWidth      =   2520
           Begin VB.Label Label1
              BorderStyle     =   1  'Fixed Single
              Height          =   360
              Left            =   120
              TabIndex        =   0
              Top             =   120
              Width           =   360
           End
        End
        Attribute VB_Name = "Form1"
        Private Sub P()
            Debug.Print Label1.Left & " " & Label1.Top & " " & Label1.Width & " " & Label1.Height
        End Sub
        Private Sub Form_Load()
            Debug.Print ScaleWidth & " " & ScaleHeight & " " & TypeName(ScaleWidth)
            Label1.Move 100
            P
            Label1.Move 200, 300
            P
            Label1.Move 400, 500, 600, 700
            P
            Label1.Move 450.5, 500, 600, 700
            P
            On Error Resume Next
            Label1.Move 400, 500, -10, 700
            Debug.Print "negative " & Err.Number
            On Error GoTo 0
            P
            Width = (Width - ScaleWidth) + 3000
            Height = (Height - ScaleHeight) + 2000
            Debug.Print ScaleWidth & " " & ScaleHeight
        End Sub

        """;

    [AvaloniaFact]
    public async Task Move_and_ScaleWidth_answer_as_vb6_exe_did()
    {
        File.WriteAllText(Path.Combine(dir, "Form1.frm"), Form1.ReplaceLineEndings("\r\n"), Encoding.ASCII);
        File.WriteAllText(Path.Combine(dir, "Probe.vbp"), "Type=Exe\r\nForm=Form1.frm\r\nStartup=\"Form1\"\r\nName=\"Probe\"\r\n", Encoding.ASCII);
        var project = (await new ProjectLoader().LoadAsync(Path.Combine(dir, "Probe.vbp"), new NullSink())).Project;
        var printed = new List<string>();
        var errors = new List<Exception>();
        Action<string> print = printed.Add;
        void OnFaulted(VBWindowContext _, Exception e) => errors.Add(e);
        VBDebugConsole.Output += print;
        VBWindowContext.Faulted += OnFaulted;
        using var cancel = new CancellationTokenSource();
        Window? window = null;
        try
        {
            _ = VBLoader.RunMadeProgram(project, cancel.Token, out var startup);
            window = startup;
            for (var i = 0; i < 20; i++)
                Dispatcher.UIThread.RunJobs();

            errors.Should().BeEmpty();
            printed.Should().Equal(
                "2520 2520 Single",
                "100 120 360 360",
                "200 300 360 360",
                "400 500 600 700",
                "450 500 600 700",
                "negative 5",
                "450 500 600 700",
                "3000 1995");
        }
        finally
        {
            cancel.Cancel();
            window?.Close();
            VBDebugConsole.Output -= print;
            VBWindowContext.Faulted -= OnFaulted;
        }
    }
}
