using System.IO;
using System.Text.Json;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Sidecar;

/// <summary>
/// Breakpoints persist through <see cref="UserSidecarService"/> into the per-user <c>&lt;project&gt;.user.hexproj</c>
/// sidecar beside the .vbp (the same file + mechanism as bookmarks), so they survive across service instances and
/// the user can choose to commit or ignore that file.
/// </summary>
public sealed class UserSidecarBreakpointTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "HexIDE.Tests.Sidecar", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private ProjectDefinition MakeSavedProject()
    {
        Directory.CreateDirectory(_dir);
        var project = TestHelpers.CreateProjectWithForm("P", "Form1");
        project.AddModule(new ModuleDefinition(project, "Module1", ModuleKind.StandardModule));
        project.AbsolutePath = Path.Combine(_dir, "P.vbp");
        return project;
    }

    private static DocumentIdentity Form1(ProjectDefinition p) => DocumentIdentity.For(p.Forms[0]);
    private static DocumentIdentity Module1(ProjectDefinition p) => DocumentIdentity.For(p.Modules[0]);

    [Fact]
    public async Task Breakpoints_RoundTripThroughSidecar_WrittenBesideTheVbp()
    {
        // No project registered as "loaded" ⇒ the on-change debounced auto-save doesn't fire; we drive Save/Load
        // explicitly for determinism.
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(new List<ProjectDefinition>());
        var project = MakeSavedProject();

        var breakpoints1 = new BreakpointService();
        var sidecar1 = new UserSidecarService(new BookmarkService(), breakpoints1, projectManager);
        breakpoints1.SetDocument(Form1(project), new[] { 4, 8 });
        breakpoints1.SetDocument(Module1(project), new[] { 2 });

        await sidecar1.SaveAsync(project);

        // The sidecar lands next to the project file, not in %APPDATA%.
        File.Exists(Path.Combine(_dir, "P.user.hexproj")).Should().BeTrue();

        // A fresh service instance reloads the same breakpoints from that file.
        var breakpoints2 = new BreakpointService();
        var sidecar2 = new UserSidecarService(new BookmarkService(), breakpoints2, projectManager);
        await sidecar2.LoadAsync(project);

        breakpoints2.GetBreakpoints(Form1(project)).Should().Equal(4, 8);
        breakpoints2.GetBreakpoints(Module1(project)).Should().Equal(2);
    }

    /// <summary>
    /// What is actually in the file, which no round-trip through two service instances can show: a document
    /// is named by its own name, because the sidecar already belongs to exactly one project.
    /// </summary>
    [Fact]
    public async Task TheFileIsKeyedByDocumentName()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(new List<ProjectDefinition>());
        var project = MakeSavedProject();

        var breakpoints = new BreakpointService();
        var sidecar = new UserSidecarService(new BookmarkService(), breakpoints, projectManager);
        breakpoints.SetDocument(Module1(project), new[] { 2 });
        await sidecar.SaveAsync(project);

        var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_dir, "P.user.hexproj"), TestContext.Current.CancellationToken));
        json.RootElement.GetProperty("breakpoints").EnumerateObject()
            .Select(p => p.Name).Should().Equal("Module1");
    }

    /// <summary>
    /// A sidecar written before documents had identities named them by the URI the IDE used internally.
    /// Those keys are still read, so an upgrade does not lose anybody's marks.
    /// </summary>
    [Fact]
    public async Task ASidecarWrittenWithTheOldKeysIsStillRead()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(new List<ProjectDefinition>());
        var project = MakeSavedProject();

        await File.WriteAllTextAsync(Path.Combine(_dir, "P.user.hexproj"),
            """
            {
              "version": 1,
              "bookmarks": { "vb6://form/Form1": [1] },
              "breakpoints": { "vb6://module/Module1": [2, 6] }
            }
            """, TestContext.Current.CancellationToken);

        var bookmarks = new BookmarkService();
        var breakpoints = new BreakpointService();
        await new UserSidecarService(bookmarks, breakpoints, projectManager).LoadAsync(project);

        bookmarks.GetBookmarks(Form1(project)).Should().Equal(1);
        breakpoints.GetBreakpoints(Module1(project)).Should().Equal(2, 6);
    }

    [Fact]
    public async Task Bookmarks_AndBreakpoints_Coexist_InTheSameSidecar()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(new List<ProjectDefinition>());
        var project = MakeSavedProject();

        var bookmarks1 = new BookmarkService();
        var breakpoints1 = new BreakpointService();
        var sidecar1 = new UserSidecarService(bookmarks1, breakpoints1, projectManager);
        bookmarks1.SetBookmarks(Form1(project), new[] { 1 });   // bookmarks are 0-based; value is opaque here
        breakpoints1.SetDocument(Form1(project), new[] { 5 });

        await sidecar1.SaveAsync(project);

        var bookmarks2 = new BookmarkService();
        var breakpoints2 = new BreakpointService();
        var sidecar2 = new UserSidecarService(bookmarks2, breakpoints2, projectManager);
        await sidecar2.LoadAsync(project);

        bookmarks2.GetBookmarks(Form1(project)).Should().Equal(1);
        breakpoints2.GetBreakpoints(Form1(project)).Should().Equal(5);
    }

    [Fact]
    public async Task UnloadingProject_ClearsItsBreakpointsFromMemory_ButLeavesTheSidecarIntact()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(new List<ProjectDefinition>());
        var project = MakeSavedProject();

        var breakpoints = new BreakpointService();
        var sidecar = new UserSidecarService(new BookmarkService(), breakpoints, projectManager);
        breakpoints.SetDocument(Form1(project), new[] { 5 });
        await sidecar.SaveAsync(project); // persist to the .user.hexproj

        // Unload the project — the shared singleton store must drop this project's entries so they can't bleed
        // into the next project opened under the same form name.
        projectManager.ProjectUnloaded += Raise.Event<Action<ProjectDefinition>>(project);

        breakpoints.GetBreakpoints(Form1(project)).Should().BeEmpty(); // memory cleared, no bleed

        // ...but clearing memory must NOT erase the on-disk sidecar: a fresh load still gets the breakpoint back.
        var reloaded = new BreakpointService();
        await new UserSidecarService(new BookmarkService(), reloaded, projectManager).LoadAsync(project);
        reloaded.GetBreakpoints(Form1(project)).Should().Equal(5);
    }

    /// <summary>
    /// A first save gives a document a file; it does not give it a new identity, so the marks on it stay
    /// where they were and are written into the project's sidecar.
    /// </summary>
    [Fact]
    public async Task AFirstSaveKeepsTheMarksOnTheDocument()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(new List<ProjectDefinition>());
        var project = MakeSavedProject();

        var bookmarks = new BookmarkService();
        var sidecar = new UserSidecarService(bookmarks, new BreakpointService(), projectManager);
        bookmarks.SetBookmarks(Form1(project), new[] { 7 });

        project.Forms[0].AbsolutePath = Path.Combine(_dir, "Form1.frm");

        bookmarks.GetBookmarks(Form1(project)).Should().Equal(7);

        await sidecar.SaveAsync(project);
        var reloaded = new BookmarkService();
        await new UserSidecarService(reloaded, new BreakpointService(), projectManager).LoadAsync(project);
        reloaded.GetBookmarks(Form1(project)).Should().Equal(7);
    }
}
