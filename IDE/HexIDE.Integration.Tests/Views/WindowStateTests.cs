using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A form's WindowState (#1152), against what vb6.exe gave in the guest's console session, native and p-code alike (the
/// oracle's "A form's WindowState").
/// </summary>
public class WindowStateTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Form =
        "VERSION 5.00\r\n"
        + "Begin VB.Form Form1\r\n"
        + "   Caption         =   \"p\"\r\n"
        + "   ClientHeight    =   3000\r\n"
        + "   ClientLeft      =   60\r\n"
        + "   ClientTop       =   450\r\n"
        + "   ClientWidth     =   4500\r\n"
        + "   ScaleHeight     =   3000\r\n"
        + "   ScaleWidth      =   4500\r\n"
        + "   WindowState     =   2  'Maximized\r\n"
        + "End\r\n"
        + "Attribute VB_Name = \"Form1\"\r\n"
        + "Private Sub Form_Load()\r\n"
        + "    On Error Resume Next\r\n"
        + "    Debug.Print \"load \" & WindowState & \" \" & TypeName(WindowState)\r\n"
        + "    WindowState = 1\r\n"
        + "    Debug.Print \"load-set1 \" & WindowState\r\n"
        + "    WindowState = 2\r\n"
        + "End Sub\r\n"
        + "Public Sub Probe()\r\n"
        + "    On Error Resume Next\r\n"
        + "    Debug.Print \"shown \" & WindowState\r\n"
        + "    WindowState = 0\r\n"
        + "    Debug.Print \"set0 \" & WindowState\r\n"
        + "    WindowState = 3\r\n"
        + "    Debug.Print \"set3 \" & WindowState & \" \" & Err.Number: Err.Clear\r\n"
        + "    WindowState = -1\r\n"
        + "    Debug.Print \"set-1 \" & WindowState & \" \" & Err.Number: Err.Clear\r\n"
        + "    WindowState = 1.6\r\n"
        + "    Debug.Print \"set1.6 \" & WindowState & \" \" & Err.Number: Err.Clear\r\n"
        + "    WindowState = \"0\"\r\n"
        + "    Debug.Print \"setstr \" & WindowState & \" \" & Err.Number: Err.Clear\r\n"
        + "    WindowState = Null\r\n"
        + "    Debug.Print \"setnull \" & WindowState & \" \" & Err.Number: Err.Clear\r\n"
        + "End Sub\r\n"
        + "\r\n";

    [AvaloniaFact]
    public void It_reads_0_until_shown_takes_the_file_s_state_and_refuses_what_vb6_exe_refused()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddForm(new FormDeserializer().Deserialize(project, Form, new NullSink())!);
        var printed = new List<string>();
        Action<string> print = printed.Add;
        VBDebugConsole.Output += print;
        var cancel = new CancellationTokenSource();
        try
        {
            _ = VBLoader.RunForm(project.StartupForm!, cancel.Token, out var window);
            for (var i = 0; i < 20; i++)
                Dispatcher.UIThread.RunJobs();
            window.ExecuteSub("Probe");
            for (var i = 0; i < 20; i++)
                Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            cancel.Cancel();
            Dispatcher.UIThread.RunJobs();
            VBDebugConsole.Output -= print;
        }

        // Hidden, it reads 0 whatever was set; shown, the state Load left it in, 2. Then each value as vb6.exe took it.
        printed.Should().Equal(
            "load 0 Integer",
            "load-set1 0",
            "shown 2",
            "set0 0",
            "set3 0 380",
            "set-1 0 380",
            "set1.6 2 0",
            "setstr 0 0",
            "setnull 0 94");
    }
}
