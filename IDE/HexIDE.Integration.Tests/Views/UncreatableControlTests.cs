using System;
using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime;
using HexIDE.Runtime.Debugging;
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
        var runner = TestRunners.WithInterpreter(
            Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), Substitute.For<IProjectManager>(),
            localization, controller, Substitute.For<IBreakpointService>(), new WatchService(), new RunScope());
        return (runner, controller);
    }

    private static string BoundText(string property = "DataSource", bool nested = false)
    {
        var text = "   Begin VB.TextBox Text1\r\n"
            + $"      {property} = \"Data1\"\r\n"
            + "      DataField = \"Author\"\r\n"
            + "      Text = \"Text1\"\r\n"
            + "      Height = 375\r\n      Width = 1215\r\n   End\r\n";
        return nested ? "   Begin VB.Frame Frame1\r\n      Height = 2000\r\n      Width = 3000\r\n"
            + text + "   End\r\n" : text;
    }

    [AvaloniaTheory]
    [InlineData("DataSource", false)]
    [InlineData("dAtAsOuRcE", false)]
    [InlineData("DataSource", true)]
    public void A_bound_control_refuses_the_start_before_Load(string property, bool nested)
    {
        var (runner, controller) = Runner();
        var reported = new List<string>();
        runner.StartFailed += reported.Add;
        try
        {
            runner.RunProject(Project(Form("Form1", BoundText(property, nested),
                "Private Sub Form_Load()\r\n    Err.Raise 5\r\nEnd Sub\r\n")));

            reported.Should().ContainSingle().Which.Should().Be(
                "Form 'Form1' failed: Not implemented yet: HexIDE does not implement data binding: Text1 has DataSource Data1.");
            runner.IsRunning.Should().BeFalse();
            runner.RunningProject.Should().BeNull();
            controller.IsSessionActive.Should().BeFalse();
        }
        finally
        {
            runner.EndProject();
        }
    }

    [AvaloniaFact]
    public void A_control_with_DataField_alone_still_runs()
    {
        var (runner, _) = Runner();
        var reported = new List<string>();
        runner.StartFailed += reported.Add;
        try
        {
            runner.RunProject(Project(Form("Form1", BoundText().Replace("      DataSource = \"Data1\"\r\n", ""))));

            reported.Should().BeEmpty();
            runner.IsRunning.Should().BeTrue();
        }
        finally
        {
            runner.EndProject();
        }
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
}
