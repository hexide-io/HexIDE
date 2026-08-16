using System.IO;
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
        breakpoints1.SetDocument("vb6://form/Form1", new[] { 4, 8 });
        breakpoints1.SetDocument("vb6://module/Module1", new[] { 2 });

        await sidecar1.SaveAsync(project);

        // The sidecar lands next to the project file, not in %APPDATA%.
        File.Exists(Path.Combine(_dir, "P.user.hexproj")).Should().BeTrue();

        // A fresh service instance reloads the same breakpoints from that file.
        var breakpoints2 = new BreakpointService();
        var sidecar2 = new UserSidecarService(new BookmarkService(), breakpoints2, projectManager);
        await sidecar2.LoadAsync(project);

        breakpoints2.GetBreakpoints("vb6://form/Form1").Should().Equal(4, 8);
        breakpoints2.GetBreakpoints("vb6://module/Module1").Should().Equal(2);
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
        bookmarks1.SetBookmarks("vb6://form/Form1", new[] { 1 });      // bookmarks are 0-based; value is opaque here
        breakpoints1.SetDocument("vb6://form/Form1", new[] { 5 });

        await sidecar1.SaveAsync(project);

        var bookmarks2 = new BookmarkService();
        var breakpoints2 = new BreakpointService();
        var sidecar2 = new UserSidecarService(bookmarks2, breakpoints2, projectManager);
        await sidecar2.LoadAsync(project);

        bookmarks2.GetBookmarks("vb6://form/Form1").Should().Equal(1);
        breakpoints2.GetBreakpoints("vb6://form/Form1").Should().Equal(5);
    }

    [Fact]
    public async Task UnloadingProject_ClearsItsBreakpointsFromMemory_ButLeavesTheSidecarIntact()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(new List<ProjectDefinition>());
        var project = MakeSavedProject();

        var breakpoints = new BreakpointService();
        var sidecar = new UserSidecarService(new BookmarkService(), breakpoints, projectManager);
        breakpoints.SetDocument("vb6://form/Form1", new[] { 5 });
        await sidecar.SaveAsync(project); // persist to the .user.hexproj

        // Unload the project — the shared singleton store must drop this project's entries so they can't bleed
        // into the next project opened under the same form name.
        projectManager.ProjectUnloaded += Raise.Event<Action<ProjectDefinition>>(project);

        breakpoints.GetBreakpoints("vb6://form/Form1").Should().BeEmpty(); // memory cleared, no bleed

        // ...but clearing memory must NOT erase the on-disk sidecar: a fresh load still gets the breakpoint back.
        var reloaded = new BreakpointService();
        await new UserSidecarService(new BookmarkService(), reloaded, projectManager).LoadAsync(project);
        reloaded.GetBreakpoints("vb6://form/Form1").Should().Equal(5);
    }
}
