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
    // #3229: null makes Load finish inside RunForm; -1 makes every statement yield.
    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData(-1)]
    public async Task Load_output_reaches_the_run_reply_and_is_not_repeated(int? timeSliceMs)
    {
        var composition = new DISetup();
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        project.AddForm(form);
        form.UpdateCode(form.Code + "\r\nPrivate Sub Form_Load()\r\nDebug.Print \"load\"\r\nEnd Sub\r\n");
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        projects.StartupProject.Returns(project);
        var controller = new DebugController { TimeSliceMs = timeSliceMs };
        var marks = new BreakpointService();
        var runner = new ProjectRunnerService(new EventBus(), Substitute.For<IWindowManager>(), projects,
            composition.LocalizationService, controller, marks, new WatchService(), new RunScope());
        using var context = new IdeContext(projects, Substitute.For<IDocumentDockService>(), Substitute.For<ILspClient>(),
            null!, runner, null!, null!, null!, marks, controller, composition.Root, null!, null!, null!, null!,
            null!, null!, composition.LocalizationService, null!);
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
            await composition.LspClient.DisposeAsync();
        }
    }
}
