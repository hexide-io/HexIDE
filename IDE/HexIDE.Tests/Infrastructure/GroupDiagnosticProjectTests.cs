using System.IO;
using HexIDE.Conversations;
using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Tools.Diagnostics;

namespace HexIDE.Tests.Infrastructure;

public class GroupDiagnosticProjectTests
{
    private static (ProjectDefinition First, ProjectDefinition Second, string Shared) Projects()
    {
        var first = TestHelpers.CreateProject("First");
        var second = TestHelpers.CreateProject("Second");
        foreach (var project in new[] { first, second })
        {
            project.AddModule(new ModuleDefinition(project, project.Name + "Only", ModuleKind.StandardModule));
            var shared = new ModuleDefinition(project, "Shared", ModuleKind.StandardModule)
            {
                AbsolutePath = Path.Combine(Path.GetTempPath(), "HexIDE-261", "Shared.bas")
            };
            project.AddModule(shared);
        }
        return (first, second, DocumentWireName.For(first.Modules[1]));
    }

    private static Diagnostic Problem() => new(new HexIDE.Lsp.Messages.Range(new Position(0, 0), new Position(0, 1)), "problem");

    [AvaloniaFact]
    public async Task ToolNamesEachOwnerAndRecomputesSharedOwnerWithoutAnotherPublication()
    {
        var (first, second, shared) = Projects();
        var projects = new ProjectManager(Substitute.For<IEditorService>(), Substitute.For<IEventBus>());
        projects.AddProject(first);
        projects.AddProject(second);
        projects.StartupProject = second;
        var client = Substitute.For<ILspClient>();
        await using var capture = new ConversationLog();
        using var context = new IdeContext(projects, Substitute.For<IDocumentDockService>(), client,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            capture, null!, null!, null!, new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader());
        var uris = new[] { DocumentWireName.For(first.Modules[0]), DocumentWireName.For(second.Modules[0]), shared, "file:///outside/none.bas" };
        foreach (var uri in uris)
            client.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(client, new PublishDiagnosticsParams(uri, [Problem()]));
        var tools = new HexIdeTools(context);
        var reply = await tools.GetDiagnosticsAsync(TestContext.Current.CancellationToken);
        reply.Diagnostics.Single(d => d.Uri == uris[0]).Project.Should().Be("First");
        reply.Diagnostics.Single(d => d.Uri == uris[1]).Project.Should().Be("Second");
        reply.Diagnostics.Single(d => d.Uri == shared).Project.Should().Be("Second");
        reply.Diagnostics.Single(d => d.Uri == uris[3]).Project.Should().BeNull();
        projects.StartupProject = first;
        reply = await tools.GetDiagnosticsAsync(TestContext.Current.CancellationToken);
        reply.Diagnostics.Single(d => d.Uri == shared).Project.Should().Be("First");
        reply.Diagnostics.Single(d => d.Uri == shared).Document.Should().Be("Shared");
        projects.UnloadProject(second);
        reply = await tools.GetDiagnosticsAsync(TestContext.Current.CancellationToken);
        foreach (var item in reply.Diagnostics)
        {
            var previous = NamedDocuments.ForUri(projects.LoadedProjects, item.Uri);
            item.Project.Should().Be(previous.Project, "single-project replies retain their existing values");
            item.Document.Should().Be(previous.Name);
        }
    }

    [AvaloniaFact]
    public void PanelTracksGroupOwnersAndHidesThemForASingleProject()
    {
        var (first, second, shared) = Projects();
        var projects = new ProjectManager(Substitute.For<IEditorService>(), Substitute.For<IEventBus>());
        projects.AddProject(first);
        projects.AddProject(second);
        projects.StartupProject = second;
        var registry = Substitute.For<ILanguageConnectionRegistry>();
        var uris = new[] { DocumentWireName.For(first.Modules[0]), DocumentWireName.For(second.Modules[0]), shared, "file:///outside/none.bas" };
        registry.Diagnostics.Returns(uris.Select(uri => new OwnedDiagnostic(uri, "server", Problem())).ToArray());
        var vm = new DiagnosticsToolViewModel(registry, Substitute.For<ILspClient>(),
            Substitute.For<IDocumentDockService>(), Substitute.For<IEditorService>(), Substitute.For<ILocalizationService>(), projects);
        vm.ShowProjects.Should().BeTrue();
        vm.Rows.Select(r => r.Project).Should().Equal("First", "Second", "Second", null);
        projects.StartupProject = first;
        vm.Rows.Select(r => r.Project).Should().Equal("First", "Second", "First", null);
        projects.StartupProject = TestHelpers.CreateProject("NotLoaded");
        vm.Rows.Single(r => r.Uri == shared).Project.Should().Be("First", "without a loaded startup owner, load order wins");
        projects.UnloadProject(second);
        vm.ShowProjects.Should().BeFalse();
        vm.Rows.Should().OnlyContain(r => r.Project == null);
    }
}
