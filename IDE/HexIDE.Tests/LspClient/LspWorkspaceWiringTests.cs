using HexIDE;
using HexIDE.Lsp;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// The language layer's view of "where is the workspace", resolved from the real dependency graph.
///
/// <para>
/// This file exists because of a defect no other test could see. <c>ProjectLspWorkspace</c> closes a
/// dependency cycle — <c>ProjectManager</c> needs an editor service, which builds code editors, which need
/// the language client, which needs the workspace, which needs the project manager. Pure.DI cannot order a
/// cycle and does not refuse one: it emits the singleton field for the back edge <b>unguarded</b>, so
/// whichever participant is built first receives <see langword="null"/>.
/// </para>
///
/// <para>
/// The cost was total. <c>Directory</c> threw on the first document opened, inside
/// <c>CodeEditorViewModel.Initialize</c>, which logs and swallows — so <c>OpenDocumentAsync</c> never
/// completed, no language server ever started, and the IDE ran with no language features at all and no
/// symptom but a line in the log. Every one of the 850-odd tests passed throughout, because every one of
/// them constructed this graph by hand.
/// </para>
///
/// <para>
/// So the assertion here is deliberately weak — it does not care <em>what</em> the answer is, only that
/// asking is safe. The bug was never a wrong directory; it was that the question could not be asked.
/// </para>
/// </summary>
public class LspWorkspaceWiringTests
{
    /// <summary>
    /// The composition as the application builds it: <c>Root</c> first, then the question.
    ///
    /// <para>
    /// The order is not incidental, it is the entire reproduction. Resolving the workspace root on its own
    /// passes even with the defect, because Pure.DI guards the project manager on <em>that</em> path;
    /// resolving <c>LspClient</c> alone passes too. Only building the application's own root reaches the
    /// path where the back edge is read unguarded — which is why a test asking a narrower question would
    /// have gone on being green while the IDE had no language features.
    /// </para>
    /// </summary>
    private static DISetup AsTheApplicationBuildsIt()
    {
        var composition = new DISetup();
        _ = composition.Root;
        return composition;
    }

    [Fact]
    public void AskingWhereTheWorkspaceIsDoesNotThrow()
    {
        // Deliberately not asserting an answer — the defect was never a wrong directory, it was that the
        // question could not be asked at all.
        var composition = AsTheApplicationBuildsIt();

        var directory = () => composition.LspWorkspace.Directory;

        directory.Should().NotThrow();
    }

    [Fact]
    public void WithNoProjectOpenThereIsNoWorkspace()
    {
        // The defined answer for "not yet", and what a lazily started server is told if it asks before the
        // user has opened anything. Null, not a guess at the IDE's own working directory — a server rooted
        // at wherever HexIDE happened to be launched from would index the wrong tree entirely.
        var composition = AsTheApplicationBuildsIt();

        composition.LspWorkspace.Directory.Should().BeNull();
    }

    // ── One folder per loaded project (#261) ──────────────────────────────────────────────────────

    private static ProjectDefinition ProjectIn(string directory, string name)
    {
        var project = TestHelpers.CreateProject(name);
        project.AbsolutePath = Path.Combine(directory, name + ".vbp");
        return project;
    }

    private static ProjectLspWorkspace WorkspaceOver(params ProjectDefinition[] projects)
    {
        var manager = Substitute.For<IProjectManager>();
        manager.LoadedProjects.Returns(projects);
        manager.StartupProject.Returns(projects.FirstOrDefault());
        return new ProjectLspWorkspace(() => manager);
    }

    [Fact]
    public void EachLoadedProjectContributesItsOwnFolder()
    {
        var one = Path.Combine(Path.GetTempPath(), "hexide-a");
        var two = Path.Combine(Path.GetTempPath(), "hexide-b");

        var folders = WorkspaceOver(ProjectIn(one, "Orders"), ProjectIn(two, "Shared")).Folders;

        folders.Select(f => f.Name).Should().BeEquivalentTo(["Orders", "Shared"]);
        folders.Select(f => f.Directory).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TwoProjectsInOneDirectoryCollapseToOneFolder()
    {
        // A server should not index the same tree twice because a group happens to keep two projects
        // side by side, which is an ordinary layout rather than an odd one.
        var shared = Path.Combine(Path.GetTempPath(), "hexide-shared");

        var folders = WorkspaceOver(ProjectIn(shared, "Client"), ProjectIn(shared, "Server")).Folders;

        folders.Should().HaveCount(1);
    }

    [Fact]
    public void WithNothingLoadedThereAreNoFolders()
    {
        // Empty, not null: "no folders" is a state the caller turns into the null the wire wants, and it
        // must not have to distinguish an empty list from a missing one.
        WorkspaceOver().Folders.Should().BeEmpty();
    }

    [Fact]
    public void TheStartupProjectStillAnswersDirectory()
    {
        // The single root stays, and is still the startup project's. clangd never parses workspaceFolders
        // at all, so this is that server's only channel — measured, not assumed.
        var one = Path.Combine(Path.GetTempPath(), "hexide-a");

        WorkspaceOver(ProjectIn(one, "Orders")).Directory.Should().NotBeNullOrWhiteSpace();
    }
}
