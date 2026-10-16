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
/// <c>demo/minesweeper-forms</c> (#1123), read from the repository and run as a made program: a field of Labels loaded at
/// run time, a right-click that flags a cell, a left click that digs it and floods the empty region round it, and a face
/// button that starts again. A change that stops it running fails here rather than in front of whoever opens the demo.
/// </summary>
public sealed class MinesweeperFormsDemoTests
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
        return Path.Combine(dir!.FullName, "demo", "minesweeper-forms", "MinesweeperForms.vbp");
    }

    private static void Pump()
    {
        for (var i = 0; i < 20; i++)
            Dispatcher.UIThread.RunJobs();
    }

    private static async Task Click(Control target, string? value = null)
    {
        var press = await UiAutomationDriver.FinishAsync(await UiAutomationDriver.InteractAsync(target, "click", value));
        press.Success.Should().BeTrue(press.Error);
        Pump();
    }

    [AvaloniaFact]
    public async Task The_demo_flags_digs_and_starts_again_on_a_field_it_loads_at_run_time()
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

            errors.Should().BeEmpty();
            var form = opened.Should().ContainKey("Minesweeper").WhoseValue;
            var labels = form.GetLogicalDescendants().OfType<VBLabel>().ToList();
            var minesLeft = labels.Single(l => l.Text == "010");
            var clock = labels.Single(l => l.Text == "000");
            var cells = labels.Where(l => !ReferenceEquals(l, minesLeft) && !ReferenceEquals(l, clock)).ToList();
            var face = form.GetLogicalDescendants().OfType<VBCommandButton>().Single();

            // Form_Load loaded 80 more cells beside lblCell(0) and laid them out 9 x 9, 300 twips (20 pixels) apart, from
            // 120 twips in and 600 down.
            cells.Should().HaveCount(81);
            VBLabel Cell(int row, int col) =>
                cells.Single(l => Canvas.GetLeft(l) == 8 + 20 * col && Canvas.GetTop(l) == 40 + 20 * row);
            for (var row = 0; row < 9; row++)
                for (var col = 0; col < 9; col++)
                    Cell(row, col).IsVisible.Should().BeTrue();

            // A right-click flags a cell through MouseDown's Button, and counts it off; a second takes the flag back.
            await Click(Cell(0, 0), "right");
            Cell(0, 0).Text.Should().Be("F");
            minesLeft.Text.Should().Be("009");
            await Click(Cell(0, 0), "right");
            Cell(0, 0).Text.Should().BeEmpty();
            minesLeft.Text.Should().Be("010");

            // The first dig lays the mines round it, so it is never one. It is dug, and if no mine touches it, so is the
            // region the flood fill reaches from it; every dug cell shows its count of neighbouring mines, or nothing.
            await Click(Cell(4, 4));
            errors.Should().BeEmpty();
            Cell(4, 4).BackColor.R.Should().Be(0xE0);
            face.Content.Should().Be(":)");
            var dug = cells.Where(c => c.BackColor.R == 0xE0).ToList();
            dug.Should().NotBeEmpty();
            dug.Should().OnlyContain(c => c.Text == "" || (c.Text!.Length == 1 && c.Text[0] >= '1' && c.Text[0] <= '8'));
            if (Cell(4, 4).Text == "")
                dug.Count.Should().BeGreaterThan(1, "a cell with no mine round it digs its neighbours too");

            // The face starts a new game: every cell covered and blank again.
            face.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            errors.Should().BeEmpty();
            cells.Should().OnlyContain(c => c.BackColor.R == 0xC0 && c.Text == "");
            minesLeft.Text.Should().Be("010");
            clock.Text.Should().Be("000");
            run.IsCompleted.Should().BeFalse("the form is still loaded");
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
