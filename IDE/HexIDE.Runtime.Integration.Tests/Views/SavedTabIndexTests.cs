using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

// Form_Load readings, native and p-code agreeing: #4843, oracle PR #4873.
public class SavedTabIndexTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    [AvaloniaTheory]
    [InlineData("MSComctlLib.Toolbar")]
    [InlineData("MSComCtl2.MonthView")]
    public void A_dense_carried_control_keeps_its_index_when_read_for_runtime(string type)
    {
        var source = "VERSION 5.00\nBegin VB.Form Form1\nClientWidth = 6000\nClientHeight = 4000\n"
            + UserControlKeyTests.Block("VB.TextBox", "Text1", 0)
            + UserControlKeyTests.Block(type, "Carried1", 1)
            + UserControlKeyTests.Block("VB.TextBox", "Text2", 2)
            + "End\nAttribute VB_Name = \"Form1\"\n";
        var (_, canvas, _, _) = HexIDE.Integration.Tests.Views.ContainerRuntimeHarness.Spawn(source);
        Avalonia.Input.KeyboardNavigation.GetTabIndex(
            HexIDE.Integration.Tests.Views.ContainerRuntimeHarness.Child(canvas, "Carried1")).Should().Be(1);
    }

    [AvaloniaFact]
    public void A_raw_ListView_index_reaches_the_running_control()
    {
        var source = "VERSION 5.00\nBegin VB.Form Form1\nClientWidth = 6000\nClientHeight = 4000\n"
            + UserControlKeyTests.Block("VB.TextBox", "Text1", 1)
            + UserControlKeyTests.Block("MSComctlLib.ListView", "ListView1", 1)
            + UserControlKeyTests.Block("VB.TextBox", "Text2", 0)
            + "End\nAttribute VB_Name = \"Form1\"\n";
        var (_, canvas, _, _) = HexIDE.Integration.Tests.Views.ContainerRuntimeHarness.Spawn(source);
        Avalonia.Input.KeyboardNavigation.GetTabIndex(
            HexIDE.Integration.Tests.Views.ContainerRuntimeHarness.Child(canvas, "ListView1")).Should().Be(2);
    }

    [AvaloniaFact]
    public void A_UserControl_extender_reads_the_normalized_raw_index_in_Form_Load()
    {
        using var run = new UserControlKeyTests.Run("", blocks:
            UserControlKeyTests.Block("VB.TextBox", "Text1", 1)
            + UserControlKeyTests.Block("P.Dial", "uc1", 1)
            + UserControlKeyTests.Block("VB.CommandButton", "Command1", 0),
            formCode: "Private Sub Form_Load()\nDebug.Print CStr(Text1.TabIndex) & \" \" & CStr(uc1.TabIndex) & \" \" & CStr(Command1.TabIndex)\nEnd Sub");
        run.Faults.Should().BeEmpty();
        run.RunTimeErrors.Should().BeEmpty();
        run.Lib.Printed.Select(v => v.Value).Should().Equal("1 2 0");
    }

    [AvaloniaTheory]
    [InlineData("2,0,1", "2 0 1")]
    [InlineData("1,1,0", "1 2 0")]
    [InlineData("0,5,1", "0 2 1")]
    [InlineData("0,0,1", "0 2 1")]
    [InlineData("9,7,0", "1 2 0")]
    [InlineData("2,2,0,9", "2 1 0 3")]
    [InlineData("1,1,3,9", "1 0 3 2")]
    public void Form_Load_reads_the_normalized_indices(string saved, string expected)
    {
        var values = saved.Split(',');
        var controls = string.Concat(values.Select((v, i) =>
            $"   Begin VB.TextBox Text{i + 1}\r\n      Height = 375\r\n      Width = 1200\r\n      TabIndex = {v}\r\n   End\r\n"));
        var print = string.Join(" & \" \" & ", values.Select((_, i) => $"CStr(Text{i + 1}.TabIndex)"));
        var source = "VERSION 5.00\r\nBegin VB.Form Form1\r\n   ClientHeight = 3600\r\n   ClientWidth = 4800\r\n"
            + controls + "End\r\nAttribute VB_Name = \"Form1\"\r\nPrivate Sub Form_Load()\r\nDebug.Print "
            + print + "\r\nEnd Sub\r\n";
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source, new NullSink())!;
        project.AddForm(form);
        var printed = new List<string>();
        var errors = new List<Exception>();
        void Fault(VBWindowContext _, Exception error) => errors.Add(error);
        void RuntimeError(VBWindowContext _, VBRunTimeException error) => errors.Add(error);
        void CompileError(VBWindowContext _, VBCompileErrorException error) => errors.Add(error);
        VBDebugConsole.Output += printed.Add;
        VBWindowContext.Faulted += Fault;
        VBWindowContext.RunTimeError += RuntimeError;
        VBWindowContext.CompileError += CompileError;
        try
        {
            VBLoader.RunForm(form, CancellationToken.None, out var window);
            try { Dispatcher.UIThread.RunJobs(); }
            finally { window.Close(); }
        }
        finally
        {
            VBDebugConsole.Output -= printed.Add;
            VBWindowContext.Faulted -= Fault;
            VBWindowContext.RunTimeError -= RuntimeError;
            VBWindowContext.CompileError -= CompileError;
        }
        errors.Should().BeEmpty();
        printed.Select(s => s.Trim()).Should().Equal(expected);
    }
}
