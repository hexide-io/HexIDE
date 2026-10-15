using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// <c>demo/battleship-forms</c> (#1071), read from the repository and run as a made program. It is the program a VB6
/// developer would write: Sub Main shows a setup dialog modally, the board loads a Label grid at run time, and a press
/// on a cell fires through a class that raises events to the form. A change that stops it running fails here rather
/// than in front of whoever opens the demo.
/// </summary>
public sealed class BattleshipFormsDemoTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static string DemoProject()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                                && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "demo", "battleship-forms", "BattleshipForms.vbp");
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task The_demo_runs_from_its_setup_dialog_to_a_board_that_takes_shots()
    {
        var project = (await new ProjectLoader().LoadAsync(DemoProject(), new NullSink())).Project;
        var errors = new List<Exception>();
        var opened = new Dictionary<string, VBFormRuntime>();
        void OnFaulted(VBWindowContext _, Exception e) => errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => errors.Add(e);
        void OnCompileError(VBWindowContext _, VBCompileErrorException e) => errors.Add(e);
        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        VBWindowContext.CompileError += OnCompileError;
        using var watch = Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) => opened[w.Title ?? ""] = w);
        using var cancel = new CancellationTokenSource();
        try
        {
            var run = VBLoader.RunMadeProgram(project, cancel.Token, out _);
            Pump();

            // Sub Main has shown the setup dialog modally, and waits in the Show.
            errors.Should().BeEmpty();
            var setup = opened.Should().ContainKey("New game").WhoseValue;
            setup.GetLogicalDescendants().OfType<VBTextBox>().Single().Text = "Ada";
            setup.GetLogicalDescendants().OfType<VBCommandButton>().Single(b => Equals(b.Content, "OK"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();

            // OK hid the dialog, Main read it, unloaded it and showed the board, a 5 x 5 grid by default.
            errors.Should().BeEmpty();
            setup.IsVisible.Should().BeFalse();
            var board = opened.Should().ContainKey("Battleship - Ada").WhoseValue;
            var labels = board.GetLogicalDescendants().OfType<VBLabel>().ToList();
            var status = labels.Single(l => l.Text?.Contains("shots left") == true);
            var cells = labels.Where(l => !ReferenceEquals(l, status)).ToList();
            cells.Should().HaveCount(25);
            status.Text.Should().Be("12 shots left, 2 ships afloat");
            // In twips on the form, 15 to the pixel: the cell in row 1, column 1 is at 120 + 360 twips.
            cells.Should().Contain(l => Canvas.GetLeft(l) == 32 && Canvas.GetTop(l) == 32);
            run.IsCompleted.Should().BeFalse("the board is loaded, though Main has returned");

            // A real press on a cell fires at it through MouseDown, and the game's Result event paints it.
            var first = cells.Single(l => Canvas.GetLeft(l) == 8 && Canvas.GetTop(l) == 8);
            var press = await UiAutomationDriver.FinishAsync(await UiAutomationDriver.InteractAsync(first, "click", null));
            press.Success.Should().BeTrue(press.Error);
            Pump();

            errors.Should().BeEmpty();
            status.Text.Should().StartWith("11 shots left, ");
            // A second press on the same cell costs nothing.
            press = await UiAutomationDriver.FinishAsync(await UiAutomationDriver.InteractAsync(first, "click", null));
            press.Success.Should().BeTrue(press.Error);
            Pump();
            status.Text.Should().StartWith("11 shots left, ");
        }
        finally
        {
            cancel.Cancel();
            Pump();
            foreach (var w in opened.Values)
                w.Close();
            VBWindowContext.Faulted -= OnFaulted;
            VBWindowContext.RunTimeError -= OnRunTimeError;
            VBWindowContext.CompileError -= OnCompileError;
        }
    }
}
