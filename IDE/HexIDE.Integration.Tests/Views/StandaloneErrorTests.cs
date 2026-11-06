using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.IDE;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Dialogs;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Standalone;

namespace HexIDE.Integration.Tests.Views;

public sealed class StandaloneErrorTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static ProjectDefinition Project(string code, bool main = false, string? source = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Made");
        var form = new FormDeserializer().Deserialize(project, ("""
            VERSION 5.00
            Begin VB.Form Form1
               Caption = "Form1"
               ClientHeight = 3000
               ClientWidth = 4000
               Begin VB.CommandButton Command1
                  Caption = "Command1"
                  Height = 495
                  Width = 1215
               End
               Begin VB.Timer Timer1
                  Enabled = 0
                  Interval = 1000
               End
            End
            Attribute VB_Name = "Form1"

            """ + code + """

            Private Sub Timer1_Timer()
                Debug.Print "tick"
            End Sub
            Private Sub Form_QueryUnload(Cancel As Integer, UnloadMode As Integer)
                Debug.Print "query"
            End Sub
            Private Sub Form_Unload(Cancel As Integer)
                Debug.Print "unload"
            End Sub
            Private Sub Form_Terminate()
                Debug.Print "terminate"
            End Sub
            """).ReplaceLineEndings("\r\n"), new Sink())!;
        project.AddForm(form);
        project.StartupForm = form;
        if (main)
        {
            var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
            module.UpdateCode("Sub Main()\r\n    Form1.Show\r\n    Debug.Print 1 \\ 0\r\nEnd Sub\r\n");
            project.AddModule(module);
            project.StartsAtSubMain = true;
        }
        if (source is not null)
        {
            var module = new ModuleDefinition(project, "Source", ModuleKind.ClassModule);
            module.UpdateCode(source.ReplaceLineEndings("\r\n"));
            project.AddModule(module);
        }
        return project;
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
    }

    private static void Check(ProjectDefinition project, bool click, string expected)
    {
        var printed = new List<string>();
        var dialogs = new List<Window>();
        var exits = 0;
        var answer = new TaskCompletionSource();
        var cancel = new CancellationTokenSource();
        using var host = new StandaloneRun(project, window =>
        {
            dialogs.Add(window);
            window.Closed += (_, _) => answer.TrySetResult();
            window.Show();
            return answer.Task;
        }, () => exits++, _ => "HexIDE does not implement this yet:\n\n{0}");
        VBFormRuntime? opened = null;
        using var watch = Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) => opened = w);
        VBDebugConsole.Output += printed.Add;
        try
        {
            var task = host.Run(cancel.Token);
            Pump();
            var form = opened!;
            if (click)
                form.GetLogicalDescendants().OfType<VBCommandButton>().Single()
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();

            dialogs.Should().ContainSingle();
            var dialog = dialogs.Single();
            dialog.Title.Should().Be("Made");
            var box = (MessageBox)dialog.Content!;
            box.Icon.Should().Be(MessageBoxIcon.Warning);
            box.Buttons.Should().Be(MessageBoxButtons.Ok);
            box.Text.Should().Contain(expected);
            exits.Should().Be(0);
            printed.Should().BeEmpty();
            form.ExecuteTimer("Timer1_Timer");
            form.GetLogicalDescendants().OfType<VBCommandButton>().Single()
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            dialogs.Should().ContainSingle();
            printed.Should().BeEmpty("no event runs behind the fatal dialog");

            box.GetLogicalDescendants().OfType<Button>().Single()
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            exits.Should().Be(1);
            printed.Should().BeEmpty("OK ends without QueryUnload, Unload or Terminate");
        }
        finally
        {
            cancel.Cancel();
            foreach (var dialog in dialogs)
                dialog.Close();
            Pump();
            VBDebugConsole.Output -= printed.Add;
        }
    }

    [AvaloniaFact]
    public void A_fatal_error_host_does_not_enable_debugging()
    {
        var project = Project("", main: true);
        project.Modules.Single().UpdateCode("""
            Sub Main()
                Debug.Assert G()
                Stop
                Debug.Print "after Stop"
            End Sub
            Private Function G() As Boolean
                Debug.Print "G called"
                G = False
            End Function
            """);
        var printed = new List<string>();
        var dialogs = new List<Window>();
        using var host = new StandaloneRun(project, window =>
        {
            dialogs.Add(window);
            return Task.CompletedTask;
        }, () => { }, _ => "{0}");
        VBDebugConsole.Output += printed.Add;
        try
        {
            var run = host.Run(CancellationToken.None);
            Pump();
            run.IsCompletedSuccessfully.Should().BeTrue();
            dialogs.Should().BeEmpty("a compiled Debug.Assert does not evaluate its argument");
            printed.Should().BeEmpty("standalone Stop still ends the walk");
        }
        finally
        {
            VBDebugConsole.Output -= printed.Add;
        }
    }

    [AvaloniaFact]
    public void An_engine_fault_is_visible_as_Internal_error()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Broken");
        // Deliberately malformed host input: the loader has no form component to instantiate.
        project.StartupForm = new FormDefinition(project, [], "");
        Window? dialog = null;
        var answer = new TaskCompletionSource();
        var exits = 0;
        using var host = new StandaloneRun(project, window =>
        {
            dialog = window;
            return answer.Task;
        }, () => exits++, _ => "{0}");

        var run = host.Run(CancellationToken.None);

        dialog.Should().NotBeNull();
        ((MessageBox)dialog!.Content!).Text.Should().Contain("Run-time error '51':")
            .And.Contain("Internal error").And.Contain("System.Exception: No form found");
        exits.Should().Be(0);
        answer.SetResult();
        Pump();
        run.IsCompletedSuccessfully.Should().BeTrue();
        exits.Should().Be(1);
    }

    [AvaloniaFact]
    public void An_event_error_halts_until_OK() => Check(Project("""
        Private Sub Command1_Click()
            Debug.Print 1 \ 0
        End Sub
        """), true, "Run-time error '11':\n\nDivision by zero");

    [AvaloniaFact]
    public void Sub_Main_halts_until_OK() => Check(Project("", main: true), false, "Run-time error '11':\n\nDivision by zero");

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_RaiseEvent_sink_halts_before_the_raisers_handler(bool handlerInFire)
    {
        var fire = handlerInFire ? """
            Public Sub Fire()
                On Error GoTo handled
                RaiseEvent Boom
                Exit Sub
            handled:
                Debug.Print "handled in Fire"
            End Sub
            """ : "Public Sub Fire()\n    RaiseEvent Boom\nEnd Sub";
        Check(Project("""
            Private WithEvents c As Source
            Private Sub Command1_Click()
                On Error GoTo handled
                Set c = New Source
                c.Fire
                Exit Sub
            handled:
                Debug.Print "handled in form"
            End Sub
            Private Sub c_Boom()
                Debug.Print 1 \ 0
            End Sub
            """, source: "Public Event Boom()\n" + fire), true, "Run-time error '11':\n\nDivision by zero");
    }

    [AvaloniaFact]
    public void A_compile_error_is_visible() => Check(Project("""
        Private Sub Command1_Click()
            MissingProcedure
        End Sub
        """), true, "Compile error:");

    [AvaloniaFact]
    public void An_unimplemented_construct_is_visible() => Check(Project("""
        Private Sub Command1_Click()
            GoSub there
        there:
            Return
        End Sub
        """), true, "HexIDE does not implement this yet:");
}
