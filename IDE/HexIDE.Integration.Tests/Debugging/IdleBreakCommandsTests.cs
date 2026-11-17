using HexIDE.Runtime.Debugging;
using HexIDE.IDE;
using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.ProjectElements;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HexIDE.Integration.Tests.Debugging;

public class IdleBreakCommandsTests
{
    [AvaloniaFact]
    public async Task Toolbar_break_while_idle_enters_break_mode_without_opening_code()
    {
        await using var owned = new RealComposition();
        var composition = owned.Composition;
        var root = composition.Root;
        composition.ProjectManager.StartupProject = new ProjectDefinition(VBProjectType.EXE, "P");
        var debug = (DebugController)composition.DebugController;
        var runner = (ProjectRunnerService)composition.ProjectRunnerService;
        var view = new MainView { DataContext = root };
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        try
        {
            window.Show();
            runner.RunHandle = Substitute.For<IDisposable>();
            debug.Reset();
            debug.NotifyDispatchIdle();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var button = view.GetVisualDescendants().OfType<Button>()
                .Single(b => AutomationProperties.GetAutomationId(b) == "Standard.Break");
            button.IsEffectivelyEnabled.Should().BeTrue();
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            debug.State.Should().Be(DebugState.Paused);
            debug.CurrentStop!.Value.Line.Should().Be(0);
            root.WindowTitle.Should().Contain("[break]");
            button.IsEffectivelyEnabled.Should().BeFalse();
            composition.DocumentDockService.ActiveDocument.Should().BeNull();
            root.ShowNextStatementCommand.CanExecute(null).Should().BeFalse();
            ApplicationCommands.OpenCallStackCommand.CanExecute(null, view).Should().BeFalse();
            debug.Continue();
            window.WindowState = WindowState.Minimized;
            await Dispatcher.UIThread.InvokeAsync(debug.Pause);
            window.WindowState.Should().Be(WindowState.Normal, "a Pause stop brings the IDE forward, even without a statement");
        }
        finally
        {
            runner.RunHandle = null;
            debug.Stop();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_stop_without_a_statement_disables_location_commands()
    {
        // The no-statement representation from #4261 (oracle PR #4270), through the real composition.
        await using var owned = new RealComposition();
        var composition = owned.Composition;
        var debug = (DebugController)composition.DebugController;
        var root = composition.Root;
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "M", ModuleKind.StandardModule);
        module.UpdateCode("Sub Main()\r\nEnd Sub\r\n");
        project.AddModule(module);
        composition.ProjectManager.AddProject(project);
        composition.EditorService.EditCode(module);
        using var editor = (CodeEditorViewModel)composition.DocumentDockService.ActiveDocument!;
        var view = new MainView { DataContext = root };
        var runner = (ProjectRunnerService)composition.ProjectRunnerService;
        runner.RunHandle = Substitute.For<IDisposable>();
        debug.Reset();
        debug.SetBreakpoints("", [0]);
        var pending = debug.OnStatementAsync(0, "", null).AsTask();
        try
        {
            root.ShowNextStatementCommand.CanExecute(null).Should().BeFalse();
            root.SetNextStatementCommand.CanExecute(null).Should().BeFalse();
            ApplicationCommands.OpenCallStackCommand.CanExecute(null, view).Should().BeFalse();
            runner.CanStepIntoProject.Should().BeTrue();
            runner.CanStepOverProject.Should().BeTrue();
            composition.ProjectRunnerService.CanStepOutProject.Should().BeFalse();
            composition.ProjectRunnerService.CanRunToCursor.Should().BeFalse();
            root.Locals.Roots.Should().BeEmpty();
            root.CallStack.Frames.Should().BeEmpty();
        }
        finally
        {
            runner.RunHandle = null;
            debug.Continue();
            await pending;
            debug.Stop();
            composition.ProjectManager.UnloadAllProjects();
            editor.Dispose();
            HexIDE.TestGuards.LspSessionLeakGuard.StillOpen()
                .Should().NotContain(s => s.Test == TestContext.Current.Test!.TestDisplayName);
        }
    }
}
