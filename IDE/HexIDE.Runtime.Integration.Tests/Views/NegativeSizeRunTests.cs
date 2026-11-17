using System.Collections.Generic;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

// #4179, VB6 SP6 IDE oracle PR #4350. This host uses Standalone's resources and loader.
public class NegativeSizeRunTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }
    private sealed record Run(List<string> Printed, List<System.Exception> Errors);

    /// <summary>Run Form1 as F5 does, with <paramref name="load"/> as its Load, then close it.</summary>
    private static Run RunLoad(string frm, string load)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var code = "Private Sub Form_Load()\r\n" + load.ReplaceLineEndings("\r\n") + "\r\nEnd Sub\r\n";
        var form = new FormDeserializer().Deserialize(project, frm + code, new NullSink())!;
        project.AddForm(form);

        var run = new Run([], []);
        void OnFaulted(VBWindowContext _, System.Exception e) => run.Errors.Add(e);
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


    [AvaloniaTheory]
    [InlineData("Width", -150)]
    [InlineData("Height", -150)]
    [InlineData("Height", 0)]
    public void A_form_runs_with_recovered_or_zero_sizes(string axis, int size)
    {
        var frm = $"VERSION 5.00\r\nBegin VB.Form Form1\r\nClientWidth = 6000\r\nClientHeight = 4000\r\nBegin VB.Label Label1\r\n{axis} = {size}\r\nEnd\r\nBegin VB.CommandButton Command2\r\n{axis} = {size}\r\nEnd\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n";
        var run = RunLoad(frm, $"Debug.Print CStr(Command2.{axis}) & \" \" & CStr(Label1.{axis})");
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Contain("0 0");
    }
}
