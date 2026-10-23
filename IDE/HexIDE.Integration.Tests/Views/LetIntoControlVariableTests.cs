using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A Let into a variable holding a control, <c>t = "x"</c> with <c>t As TextBox</c> (#1493), assigns the control's default
/// member, its Text, in VB6. HexIDE has no default members for controls yet (#790), so the Let is refused by name, and the
/// control is left as it was rather than the variable being stored over.
/// </summary>
public class LetIntoControlVariableTests
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
           Begin VB.TextBox Text1
              Height          =   285
              Left            =   120
              TabIndex        =   0
              Text            =   "orig"
              Top             =   120
              Width           =   2000
           End
           Begin VB.CommandButton Command1
              Caption         =   "Go"
              Height          =   375
              Left            =   120
              TabIndex        =   1
              Top             =   600
              Width           =   1200
           End
        End
        Attribute VB_Name = "Form1"
        Attribute VB_GlobalNameSpace = False
        Attribute VB_Creatable = False
        Attribute VB_PredeclaredId = True
        Attribute VB_Exposed = False
        Option Explicit
        Private Sub S(t As TextBox)
            t = "x"
        End Sub
        Private Sub Command1_Click()
            Debug.Print "before " & Text1.Text
            S Text1
            Debug.Print "after " & Text1.Text
        End Sub

        """;

    [AvaloniaFact]
    public void A_Let_into_a_TextBox_variable_is_refused_naming_790_and_leaves_its_Text()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Probe");
        var form = new FormDeserializer().Deserialize(project, Form1.ReplaceLineEndings("\r\n"), new NullSink())!;
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
        VBFormRuntime? window = null;
        try
        {
            VBLoader.RunForm(form, CancellationToken.None, out window, null);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var text1 = window.GetVisualDescendants().OfType<VBTextBox>().Single(t => VBProps.GetName(t) == "Text1");
            var command1 = window.GetVisualDescendants().OfType<VBCommandButton>().Single(b => VBProps.GetName(b) == "Command1");
            var top = TopLevel.GetTopLevel(command1)!;
            var point = command1.TranslatePoint(new Point(command1.Bounds.Width / 2, command1.Bounds.Height / 2), top)!.Value;
            top.MouseDown(point, MouseButton.Left);
            top.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            printed.Should().Equal("before orig");
            errors.Should().ContainSingle()
                .Which.Should().BeOfType<NotImplementedException>()
                .Which.Message.Should().Contain("`t = …`").And.Contain("#790");
            text1.Text.Should().Be("orig");
        }
        finally
        {
            window?.Close();
            VBDebugConsole.Output -= print;
            VBWindowContext.Faulted -= OnFaulted;
            VBWindowContext.RunTimeError -= OnRunTimeError;
            VBWindowContext.CompileError -= OnCompileError;
        }
    }
}
