using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A form holding a control HexIDE cannot create is refused, rather than run with the control left out (#857). Left out,
/// the form was a different program: code naming the control read Empty, was told it was not defined, or skipped a call
/// with Err.Number 0, and a form whose code never names it ran as if it had never held it.
/// </summary>
public class UncreatableControlTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private const string Data1 =
        "   Begin VB.Data Data1 \r\n"
      + "      Caption         =   \"Data1\"\r\n"
      + "      Height          =   345\r\n"
      + "      Left            =   120\r\n"
      + "      Top             =   120\r\n"
      + "      Width           =   2000\r\n"
      + "   End\r\n";

    // A container HexIDE cannot create, holding a control it can, and a font block, whose BeginProperty is not a control.
    private const string SSTab1 =
        "   Begin TabDlg.SSTab SSTab1 \r\n"
      + "      Height          =   2000\r\n"
      + "      Left            =   120\r\n"
      + "      Top             =   600\r\n"
      + "      Width           =   3000\r\n"
      + "      BeginProperty Font {0BE35203-8F91-11CE-9DE3-00AA004BB851} \r\n"
      + "         Name            =   \"MS Sans Serif\"\r\n"
      + "      EndProperty\r\n"
      + "      Begin VB.TextBox Text2 \r\n"
      + "         Height          =   375\r\n"
      + "         Left            =   120\r\n"
      + "         Top             =   480\r\n"
      + "         Width           =   1215\r\n"
      + "      End\r\n"
      + "   End\r\n";

    private static readonly string Command1 =
        "   Begin VB.CommandButton Command1 \r\n"
      + "      Caption         =   \"Command1\"\r\n"
      + "      Height          =   495\r\n"
      + "      Left            =   120\r\n"
      + "      Top             =   2700\r\n"
      + "      Width           =   1215\r\n"
      + "   End\r\n";

    private static string Form(string name, string controls, string code = "") =>
        ("VERSION 5.00\r\n"
       + $"Begin VB.Form {name} \r\n"
       + $"   Caption         =   \"{name}\"\r\n"
       + "   ClientHeight    =   3300\r\n"
       + "   ClientWidth     =   4000\r\n"
       + controls
       + "End\r\n"
       + $"Attribute VB_Name = \"{name}\"\r\n"
       + code).ReplaceLineEndings("\r\n");

    private static ProjectDefinition Project(params string[] forms)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        foreach (var form in forms)
            project.AddForm(new FormDeserializer().Deserialize(project, form, NullSink.Instance)!);
        return project;
    }

    private static (ProjectRunnerService Runner, DebugController Controller) Runner()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.ProjectRunner.StartupFormFailedToLoad").Returns("Form '{0}' failed: {1}");
        localization.GetString("Str.ProjectRunner.NotImplementedYet").Returns("Not implemented yet: {0}");
        var controller = new DebugController();
        var runner = new ProjectRunnerService(
            Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), Substitute.For<IProjectManager>(),
            localization, controller, Substitute.For<IBreakpointService>(), new WatchService(), new RunScope());
        return (runner, controller);
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void A_startup_form_holding_one_does_not_start_and_says_which_control()
    {
        var printed = new List<string>();
        Action<string> print = printed.Add;
        VBDebugConsole.Output += print;
        try
        {
            var (runner, controller) = Runner();
            var reported = new List<string>();
            runner.StartFailed += reported.Add;

            runner.RunProject(Project(Form("Form1", Data1 + Command1,
                "Private Sub Form_Load()\r\n    Debug.Print \"load\"\r\nEnd Sub\r\n")));

            reported.Should().ContainSingle()
                .Which.Should().Be("Form 'Form1' failed: Not implemented yet: HexIDE cannot create Data1 (VB.Data).");
            printed.Should().BeEmpty("no code runs in a program that never started");
            runner.IsRunning.Should().BeFalse();
            controller.IsSessionActive.Should().BeFalse();
        }
        finally
        {
            VBDebugConsole.Output -= print;
        }
    }

    [AvaloniaFact]
    public void Every_control_a_block_holds_is_named_even_one_HexIDE_models()
    {
        // Text2 is a TextBox, which HexIDE creates anywhere else. Inside a control it cannot create, nothing is read.
        Action act = () => _ = VBLoader.RunForm(Project(Form("Form1", Data1 + SSTab1)).StartupForm!, CancellationToken.None, out _);

        act.Should().Throw<NotImplementedException>().WithMessage(
            "HexIDE cannot create Data1 (VB.Data).\n"
          + "HexIDE cannot create SSTab1 (TabDlg.SSTab).\n"
          + "HexIDE cannot create Text2, inside SSTab1 (TabDlg.SSTab).");
    }

    [AvaloniaFact]
    public void A_control_array_is_named_once()
    {
        var element = (int index) => Data1.Replace("      Caption", $"      Index           =   {index}\r\n      Caption");

        Action act = () => _ = VBLoader.RunForm(Project(Form("Form1", element(0) + element(1))).StartupForm!, CancellationToken.None, out _);

        act.Should().Throw<NotImplementedException>().WithMessage("HexIDE cannot create Data1 (VB.Data).");
    }

    [AvaloniaFact]
    public void Another_form_holding_one_stops_the_run_when_it_loads_and_On_Error_cannot_trap_it()
    {
        var project = Project(
            Form("Form1", Command1,
                "Private Sub Command1_Click()\r\n    On Error Resume Next\r\n    Load Form2\r\n    Debug.Print \"after \" & Err.Number\r\nEnd Sub\r\n"),
            Form("Form2", Data1));
        var printed = new List<string>();
        var faults = new List<Exception>();
        Action<string> print = printed.Add;
        void OnFaulted(VBWindowContext _, Exception e) => faults.Add(e);
        VBDebugConsole.Output += print;
        VBWindowContext.Faulted += OnFaulted;
        var cancel = new CancellationTokenSource();
        try
        {
            _ = VBLoader.RunForm(project.StartupForm!, cancel.Token, out var window);
            Pump();

            window.ExecuteSub("Command1_Click");
            Pump();

            faults.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>()
                .Which.Message.Should().Be("HexIDE cannot create Data1 (VB.Data).");
            printed.Should().NotContain(p => p.StartsWith("after"));
        }
        finally
        {
            cancel.Cancel();
            Pump();
            VBDebugConsole.Output -= print;
            VBWindowContext.Faulted -= OnFaulted;
        }
    }

    [AvaloniaFact]
    public void The_designer_still_draws_the_form()
    {
        // Only running it is refused. The designer shows what the form holds, and #157 is what it shows for the control.
        var act = () => VBLoader.SpawnComponentsForDesigner(Project(Form("Form1", Data1 + Command1)).StartupForm!);

        act.Should().NotThrow();
    }
}
