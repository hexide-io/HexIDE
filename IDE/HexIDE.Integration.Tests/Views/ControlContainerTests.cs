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

public class ControlContainerTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    [AvaloniaTheory]
    [InlineData("Container", "Form1", "Frame1", "Picture1")]
    [InlineData("Parent", "Form1", "Form1", "Form1")]
    public void Reads_the_container_or_form_as_an_object(string member, string outer, string frame, string picture)
    {
        var source = """
            VERSION 5.00
            Begin VB.Form Form1
               ClientHeight = 4000
               ClientWidth = 6000
               Begin VB.CommandButton Command1
                  Height = 375
                  Width = 1200
               End
               Begin VB.Frame Frame1
                  Height = 2000
                  Width = 3000
                  Begin VB.PictureBox Picture1
                     Height = 1000
                     Width = 2000
                     Begin VB.TextBox Text1
                        Height = 375
                        Width = 1200
                     End
                  End
               End
            End
            Attribute VB_Name = "Form1"
            Private Sub Form_Load()
                Dim c As Control, o As Object
                Debug.Print Command1.MEMBER.Name
                Debug.Print Picture1.MEMBER.Name
                Set c = Text1
                Set o = c.MEMBER
                Debug.Print o.Name
                Unload Me
            End Sub
            """.Replace("MEMBER", member);
        var project = new ProjectDefinition(VBProjectType.EXE, "Probe");
        var form = new FormDeserializer().Deserialize(project, source.ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form);
        var printed = new List<string>();
        var errors = new List<Exception>();
        void OnFaulted(VBWindowContext _, Exception e) => errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => errors.Add(e);
        void OnCompileError(VBWindowContext _, VBCompileErrorException e) => errors.Add(e);
        VBDebugConsole.Output += printed.Add;
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
            VBDebugConsole.Output -= printed.Add;
            VBWindowContext.Faulted -= OnFaulted;
            VBWindowContext.RunTimeError -= OnRunTimeError;
            VBWindowContext.CompileError -= OnCompileError;
        }
        errors.Should().BeEmpty();
        printed.Should().Equal(outer, frame, picture);
    }
}
