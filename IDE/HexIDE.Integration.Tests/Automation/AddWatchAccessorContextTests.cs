using HexIDE.Debugging;
using HexIDE.Desktop.Server;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// The add_watch tool, paused inside a Property accessor. #4401 made each accessor its own watch context, as VB6's is, so
/// the tool must store the frame as <c>Class1.X(Get)</c>: the bare <c>Class1.X</c> it stored before matches no accessor,
/// and the watch read <c>&lt;Out of context&gt;</c> in its own frame.
/// </summary>
public class AddWatchAccessorContextTests
{
    [AvaloniaFact]
    public async Task Add_watch_paused_in_a_property_accessor_scopes_the_watch_to_that_accessor()
    {
        await using var owned = new RealComposition();
        var composition = owned.Composition;
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        project.AddForm(form);
        form.UpdateCode(form.Code + "\r\nPrivate Sub Form_Load()\r\nDim c As New Class1\r\nc.X = 5\r\nDebug.Print c.X\r\nEnd Sub\r\n");
        var cls = new ModuleDefinition(project, "Class1", ModuleKind.ClassModule);
        cls.UpdateCode("Private m As Long\r\nPublic Property Get X() As Long\r\nStop\r\nX = m\r\nEnd Property\r\n"
            + "Public Property Let X(ByVal v As Long)\r\nm = v\r\nEnd Property\r\n");
        project.AddModule(cls);
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        projects.StartupProject.Returns(project);
        var controller = new DebugController();
        var marks = new BreakpointService();
        var runner = TestRunners.WithInterpreter(new EventBus(), Substitute.For<IWindowManager>(), projects,
            composition.LocalizationService, controller, marks, new WatchService(), new RunScope());
        using var context = new IdeContext(projects, Substitute.For<IDocumentDockService>(), Substitute.For<ILspClient>(),
            null!, runner, null!, null!, null!, marks, controller, composition.Root, null!, null!, null!, null!,
            null!, null!, composition.LocalizationService, null!);
        var tools = new HexIdeTools(context);
        try
        {
            var paused = await tools.RunProjectAsync(TestContext.Current.CancellationToken);
            paused.Success.Should().BeTrue(paused.Error);
            controller.GetLocals()!.Context.Should().Be("Class1.X", "the program stopped in the Property Get");
            var added = await tools.AddWatchAsync("m", ct: TestContext.Current.CancellationToken);
            added.Success.Should().BeTrue(added.Error);
            var watch = added.Watches.Single();
            watch.Context.Should().Be("Class1.X(Get)");
            watch.Value.Should().Be("5", "the watch evaluates in the frame it was added in");
            watch.Type.Should().Be("Long");
        }
        finally
        {
            runner.EndProject();
        }
    }
}
