using HexIDE.Runtime.Serialization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using HexIDE.Debugging;
using HexIDE.Desktop.Server;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Automation;

public class ProgramPrintedStartTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Hover_reports_MouseMove_output_once_and_says_when_nothing_was_printed(bool prints)
    {
        await using var owned = new RealComposition();
        var composition = owned.Composition;
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        project.AddForm(form);
        form.UpdateCode(form.Code + "\r\nPrivate Sub Form_MouseMove(Button As Integer, Shift As Integer, X As Single, Y As Single)\r\n"
            + (prints ? "Debug.Print \"move\"\r\n" : "' no output\r\n") + "End Sub\r\n");
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        projects.StartupProject.Returns(project);
        var controller = new DebugController { TimeSliceMs = -1 };
        var marks = new BreakpointService();
        var runner = TestRunners.WithInterpreter(new EventBus(), Substitute.For<IWindowManager>(), projects,
            composition.LocalizationService, controller, marks, new WatchService(), new RunScope());
        using var context = new IdeContext(projects, Substitute.For<IDocumentDockService>(), Substitute.For<ILspClient>(),
            null!, runner, null!, null!, null!, marks, controller, composition.Root, null!, null!, null!, null!,
            composition.Capture, null!, composition.LocalizationService, null!, new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader());
        var tools = new HexIdeTools(context);
        var app = Application.Current!;
        // Avalonia fixes ApplicationLifetime at initialization. This headless host has none; lend the tool a
        // desktop lifetime for its real window lookup, then restore it even when an assertion fails.
        var lifetime = typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(IApplicationLifetime));
        var previousLifetime = lifetime.GetValue(app);
        var main = new Window();
        var windows = new List<Window> { main };
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        desktop.MainWindow.Returns(main);
        desktop.Windows.Returns(windows);
        using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) =>
        {
            if (w != main) windows.Add(w);
        });
        try
        {
            lifetime.SetValue(app, desktop);
            ProgramPrinted.Shared.FollowRuns(runner);
            HexIDE.Runtime.VBDebugConsole.Written += ProgramPrinted.Shared.Append;
            main.Show();
            var started = await tools.RunProjectAsync(TestContext.Current.CancellationToken);
            started.Success.Should().BeTrue(started.Error);
            var reply = await tools.HoverAsync("Window", dwellMs: 0, window: "Form1",
                ct: TestContext.Current.CancellationToken);
            reply.Success.Should().BeTrue(reply.Error);
            reply.Printed.Should().Be(prints ? "move\n" : null);
            reply.Detail.Should().Contain(prints ? "Debug.Print wrote 1 line" : "Debug.Print wrote nothing");
            var stopped = await tools.StopProjectAsync(TestContext.Current.CancellationToken);
            stopped.Success.Should().BeTrue(stopped.Error);
            stopped.Printed.Should().BeNull("the hover reply already reported its output");
            var design = await tools.HoverAsync("Window", dwellMs: 0, window: "ide",
                ct: TestContext.Current.CancellationToken);
            design.Success.Should().BeTrue(design.Error);
            design.Printed.Should().BeNull();
            design.Detail.Should().NotContain("Debug.Print", "a design-mode hover keeps its previous reply");
        }
        finally
        {
            runner.EndProject();
            HexIDE.Runtime.VBDebugConsole.Written -= ProgramPrinted.Shared.Append;
            ProgramPrinted.Shared.Reset();
            main.Close();
            lifetime.SetValue(app, previousLifetime);
        }
    }

    // #3229: null makes Load finish inside RunForm; -1 makes every statement yield.
    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData(-1)]
    public async Task Load_output_reaches_the_run_reply_and_is_not_repeated(int? timeSliceMs)
    {
        await using var owned = new RealComposition();
        var composition = owned.Composition;
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        project.AddForm(form);
        form.UpdateCode(form.Code + "\r\nPrivate Sub Form_Load()\r\nDebug.Print \"load\"\r\nEnd Sub\r\n");
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        projects.StartupProject.Returns(project);
        var controller = new DebugController { TimeSliceMs = timeSliceMs };
        var marks = new BreakpointService();
        var runner = TestRunners.WithInterpreter(new EventBus(), Substitute.For<IWindowManager>(), projects,
            composition.LocalizationService, controller, marks, new WatchService(), new RunScope());
        using var context = new IdeContext(projects, Substitute.For<IDocumentDockService>(), Substitute.For<ILspClient>(),
            null!, runner, null!, null!, null!, marks, controller, composition.Root, null!, null!, null!, null!,
            null!, null!, composition.LocalizationService, null!, new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader());
        var tools = new HexIdeTools(context);
        ProgramPrinted.Shared.FollowRuns(runner);
        HexIDE.Runtime.VBDebugConsole.Written += ProgramPrinted.Shared.Append;
        try
        {
            var reply = await tools.RunProjectAsync(TestContext.Current.CancellationToken);
            reply.Success.Should().BeTrue(reply.Error);
            reply.Printed.Should().Be("load\n");
            reply.Note.Should().Contain("Debug.Print wrote 1 line");
            var stopped = await tools.StopProjectAsync(TestContext.Current.CancellationToken);
            stopped.Success.Should().BeTrue(stopped.Error);
            stopped.Printed.Should().BeNull();
            form.UpdateCode(form.Code.Replace("Debug.Print \"load\"", "' no output"));
            var second = await tools.RunProjectAsync(TestContext.Current.CancellationToken);
            second.Success.Should().BeTrue(second.Error);
            second.Printed.Should().BeNull();
        }
        finally
        {
            runner.EndProject();
            HexIDE.Runtime.VBDebugConsole.Written -= ProgramPrinted.Shared.Append;
            ProgramPrinted.Shared.Reset();
        }
    }
}
