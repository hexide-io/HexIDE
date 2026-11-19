using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.IDE;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

// VB6 SP6 console measurements, IDE/p-code/native agreeing: https://github.com/hexide-io/HexIDE/issues/4829.
public class ControlVisibleTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static string Form(string controls) => $$"""
        VERSION 5.00
        Object = "{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCTL.OCX"
        Begin VB.Form Form1
           ClientHeight = 3000
           ClientWidth = 4000
        {{controls}}
        End
        Attribute VB_Name = "Form1"
        Public Sub LoadOther()
            Load Form2
        End Sub
        Public Sub ShowOther()
            Form2.Show
            DoEvents
        End Sub
        Public Sub HideOther()
            Form2.Hide
            DoEvents
        End Sub
        Public Sub Report()
            Debug.Print Form2.Command1.Visible
            Debug.Print Form2.LineP.Visible
            Debug.Print Form2.Visible
        End Sub
        Public Sub ReportProgress()
            Debug.Print Form2.ProgressBar1.Visible
        End Sub
        Public Sub ReportKinds()
            Debug.Print Form2.Label1.Visible
            Debug.Print Form2.Image1.Visible
            Debug.Print Form2.Shape1.Visible
            Debug.Print Form2.mnuGame.Visible
            Debug.Print Form2.mnuNew.Visible
            Debug.Print Form2.UCW1.Visible
            Debug.Print Form2.UCN1.Visible
        End Sub
        Public Sub LoadElement()
            Form2.LoadOwnElement
        End Sub
        Public Sub LoadOwnElement()
            Load Command1(5)
            Debug.Print Command1(5).Visible
        End Sub
        Public Sub ShowElement()
            Form2.ShowOwnElement
        End Sub
        Public Sub ShowOwnElement()
            Command1(5).Visible = True
            Debug.Print Command1(5).Visible
        End Sub
        """;

    private const string Button = """
           Begin VB.CommandButton Command1
              Height = 315
              Width = 1200
           End
           Begin VB.Line LineP
              X1 = 0
              X2 = 100
              Y1 = 0
              Y2 = 100
           End
        """;

    private sealed class Run : IDisposable
    {
        public required CancellationTokenSource Cancel { get; init; }
        public required List<string> Printed { get; init; }
        public required List<Exception> Errors { get; init; }
        public required Window Window { get; init; }
        public required Action Detach { get; init; }

        public List<string> Call(string sub)
        {
            var before = Printed.Count;
            ((VBFormRuntime)Window).ExecuteSub(sub);
            Pump();
            return Printed.Skip(before).ToList();
        }

        public void Dispose()
        {
            Cancel.Cancel();
            Pump();
            Window.Close();
            Detach();
        }
    }

    private const string Kinds = """
           Begin VB.Label Label1
              Caption = "Lbl"
              Height = 255
              Width = 1200
           End
           Begin VB.Image Image1
              Height = 480
              Width = 480
           End
           Begin VB.Shape Shape1
              Height = 400
              Width = 600
           End
           Begin P.UCW UCW1
              Height = 600
              Width = 600
           End
           Begin P.UCN UCN1
              Height = 600
              Width = 600
           End
           Begin VB.Menu mnuGame
              Caption = "&Game"
              Begin VB.Menu mnuNew
                 Caption = "&New"
              End
           End
        """;

    // The project's own UserControls the Kinds form places: UCW windowless, UCN with a window.
    private static Dictionary<string, ComponentBaseClass> UserControls(ProjectDefinition project)
    {
        var registry = new Dictionary<string, ComponentBaseClass>();
        foreach (var (name, settings) in new[] { ("UCW", "Windowless = -1\n"), ("UCN", "") })
        {
            var definition = new FormDeserializer().Deserialize(project,
                $"VERSION 5.00\nBegin VB.UserControl {name}\nClientWidth = 600\n{settings}End\nAttribute VB_Name = \"{name}\"\n"
                    .ReplaceLineEndings("\r\n"), new NullSink())!;
            var module = new ModuleDefinition(project, name, ModuleKind.UserControl);
            module.UpdateCode($"Attribute VB_Name = \"{name}\"\n");
            module.UpdateFormPart(definition);
            project.AddModule(module);
            registry[$"P.{name}"] = UserControlComponentClass.ForModule($"P.{name}", definition, registry);
        }
        return registry;
    }

    private static Run Start(string controls)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form1 = new FormDeserializer().Deserialize(project, Form("").ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form1);
        project.AddForm(new FormDeserializer().Deserialize(project,
            Form(controls).Replace("Form1", "Form2").ReplaceLineEndings("\r\n"), new NullSink(),
            extraComponents: controls.Contains("P.UC") ? UserControls(project) : null)!);

        var printed = new List<string>();
        var errors = new List<Exception>();
        Action<string> print = text => printed.AddRange(text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
        void OnFaulted(VBWindowContext _, Exception e) => errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => errors.Add(e);
        VBDebugConsole.Output += print;
        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        var cancel = new CancellationTokenSource();
        _ = VBLoader.RunForm(form1, cancel.Token, out var window);
        Pump();
        return new Run
        {
            Cancel = cancel,
            Printed = printed,
            Errors = errors,
            Window = window,
            Detach = () =>
            {
                VBDebugConsole.Output -= print;
                VBWindowContext.Faulted -= OnFaulted;
                VBWindowContext.RunTimeError -= OnRunTimeError;
            },
        };
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hosted_controls_read_the_forms_IsShown(bool shown)
    {
        var hosted = Substitute.For<IHostedForm>();
        hosted.Window.Returns(new ContentControl());
        hosted.IsShown.Returns(shown);
        var root = new HostedFormRoot(hosted, Substitute.For<IBasicStandardLibrary>());
        var button = new VBCommandButton();
        root.SetContent(button);
        VBVisibility.Get(button).Should().Be(shown);
        button.IsVisible.Should().BeTrue();
    }

    [AvaloniaTheory]
    [InlineData(0, "False", "False")]
    [InlineData(1, "True", "True")]
    [InlineData(2, "False", "False")]
    public void Button_and_Line_follow_the_measured_load_show_hide_rows(int step, string button, string form)
    {
        using var run = Start(Button);
        run.Call("LoadOther");
        if (step >= 1) run.Call("ShowOther");
        if (step >= 2) run.Call("HideOther");
        run.Call("Report").Should().Equal(button, "True", form);
        run.Errors.Should().BeEmpty();
    }

    // Label, Image, Shape and menus keep their own value; a UserControl follows its form, windowless or not. VB6 SP6 on the
    // console, p-code, native and the IDE agreeing: docs/vb6-fidelity-oracle/visible-before-a-form-is-shown.md (#4829).
    [AvaloniaTheory]
    [InlineData(0, "False")]
    [InlineData(1, "True")]
    [InlineData(2, "False")]
    public void Lightweight_controls_and_menus_keep_their_own_and_UserControls_follow_the_form(int step, string userControls)
    {
        using var run = Start(Kinds);
        run.Call("LoadOther");
        if (step >= 1) run.Call("ShowOther");
        if (step >= 2) run.Call("HideOther");
        run.Call("ReportKinds").Should().Equal("True", "True", "True", "True", "True", userControls, userControls);
        run.Errors.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(false, "False")]
    [InlineData(true, "True")]
    public void ProgressBar_follows_the_measured_load_show_rows(bool shown, string expected)
    {
        using var run = Start("""
               Begin MSComctlLib.ProgressBar ProgressBar1
                  Height = 375
                  Width = 2655
               End
            """);
        run.Call("LoadOther");
        if (shown) run.Call("ShowOther");
        run.Call("ReportProgress").Should().Equal(expected);
        run.Errors.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void A_loaded_array_element_starts_hidden_and_can_be_shown_on_a_shown_form()
    {
        using var run = Start(Button.Replace("Height = 315", "Index = 0\n      Height = 315"));
        run.Call("ShowOther");
        run.Call("LoadElement").Should().Equal("False");
        run.Call("ShowElement").Should().Equal("True");
        run.Errors.Should().BeEmpty();
    }
}
