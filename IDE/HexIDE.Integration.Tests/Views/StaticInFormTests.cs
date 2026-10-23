using System;
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
/// A Static local in a form's procedure (#1491) belongs to the form's instance: it survives <c>Unload</c>, starts again
/// after <c>Set Form2 = Nothing</c>. (A <c>New Form2</c>, which
/// has its own, measured, is #822.) The forms are run as vb6.exe ran them, printing
/// where the probe wrote to a file, and every line expected is the one vb6.exe logged, native and p-code agreeing.
/// </summary>
public class StaticInFormTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Form1 = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "One"
           ClientHeight    =   2000
           ClientLeft      =   60
           ClientTop       =   400
           ClientWidth     =   4000
           LinkTopic       =   "Form1"
           ScaleHeight     =   2000
           ScaleWidth      =   4000
        End
        Attribute VB_Name = "Form1"
        Attribute VB_GlobalNameSpace = False
        Attribute VB_Creatable = False
        Attribute VB_PredeclaredId = True
        Attribute VB_Exposed = False
        Option Explicit
        Private Sub Form_Load()
            Debug.Print "form:"
            Load Form2
            Form2.Tick: Form2.Tick
            Unload Form2
            Debug.Print "  after Unload"
            Form2.Tick
            Set Form2 = Nothing
            Debug.Print "  after Set Form2 = Nothing"
            Form2.Tick
            Unload Form2
            Set Form2 = Nothing
            Debug.Print "end"
            Unload Me
        End Sub

        """;

    private const string Form2 = """
        VERSION 5.00
        Begin VB.Form Form2
           Caption         =   "Two"
           ClientHeight    =   2000
           ClientLeft      =   60
           ClientTop       =   400
           ClientWidth     =   4000
           LinkTopic       =   "Form2"
           ScaleHeight     =   2000
           ScaleWidth      =   4000
        End
        Attribute VB_Name = "Form2"
        Attribute VB_GlobalNameSpace = False
        Attribute VB_Creatable = False
        Attribute VB_PredeclaredId = True
        Attribute VB_Exposed = False
        Option Explicit
        Public Sub Tick()
            Static n As Long
            n = n + 1
            Debug.Print "  tick " & n
        End Sub
        Private Sub Form_Load()
            Debug.Print "  Form_Load"
        End Sub
        Private Sub Form_Unload(Cancel As Integer)
            Debug.Print "  Form_Unload"
        End Sub
        Private Sub Form_Terminate()
            Debug.Print "  Form_Terminate"
        End Sub

        """;

    private static (List<string> Printed, List<Exception> Errors) Run()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Probe");
        var form1 = new FormDeserializer().Deserialize(project, Form1.ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form1);
        project.AddForm(new FormDeserializer().Deserialize(project, Form2.ReplaceLineEndings("\r\n"), new NullSink())!);
        var printed = new List<string>();
        var errors = new List<Exception>();
        Action<string> print = printed.Add;
        void OnFaulted(VBWindowContext _, Exception e) => errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => errors.Add(e);
        void OnCompileError(VBWindowContext _, VBCompileErrorException e) => errors.Add(e);
        VBDebugConsole.Output += print;
        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        VBWindowContext.CompileError += OnCompileError;
        try
        {
            VBLoader.RunForm(form1, CancellationToken.None, out _, null);
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            VBDebugConsole.Output -= print;
            VBWindowContext.Faulted -= OnFaulted;
            VBWindowContext.RunTimeError -= OnRunTimeError;
            VBWindowContext.CompileError -= OnCompileError;
        }
        return (printed, errors);
    }

    [AvaloniaFact]
    public void A_forms_Static_is_its_instances_kept_over_Unload_and_new_after_Set_Nothing()
    {
        var (printed, errors) = Run();

        errors.Should().BeEmpty();
        printed.Should().Equal(
            "form:",
            "  Form_Load",
            "  tick 1",
            "  tick 2",
            "  Form_Unload",
            "  after Unload",
            "  tick 3",
            "  Form_Terminate",
            "  after Set Form2 = Nothing",
            "  tick 1",
            "  Form_Terminate",
            "end");
    }
}
