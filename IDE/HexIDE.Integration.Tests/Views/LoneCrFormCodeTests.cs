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
/// A form whose code section ends its lines with a lone CR, under a CRLF designer block, builds and runs in VB6 (measured,
/// the oracle's "A lone CR or LF in VB6 source", #1020). Its code reaches the interpreter as it was read, so F5 runs it
/// only if the grammar takes a lone CR as a line end (#1426).
/// </summary>
public class LoneCrFormCodeTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    [AvaloniaFact]
    public void A_form_whose_code_ends_its_lines_with_a_lone_CR_runs()
    {
        var designer = "VERSION 5.00\r\nBegin VB.Form Form1\r\n   Caption         =   \"Form1\"\r\n   ClientHeight    =   1000\r\n   ClientWidth     =   2000\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n";
        var code = "Private Sub Form_Load()\r    Dim x As Integer\r    x = 1\r    x = x + 10\r    Debug.Print x\rEnd Sub\r";
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, designer + code, new NullSink())!;
        project.AddForm(form);
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
            VBLoader.RunForm(form, CancellationToken.None, out var window, null);
            Dispatcher.UIThread.RunJobs();
            window.Close();
        }
        finally
        {
            VBDebugConsole.Output -= print;
            VBWindowContext.Faulted -= OnFaulted;
            VBWindowContext.RunTimeError -= OnRunTimeError;
            VBWindowContext.CompileError -= OnCompileError;
        }

        form.Code.Should().Contain("\r    x = 1\r", "the code section is kept as it was read");
        errors.Should().BeEmpty();
        printed.Should().Equal(" 11 ");
    }
}
