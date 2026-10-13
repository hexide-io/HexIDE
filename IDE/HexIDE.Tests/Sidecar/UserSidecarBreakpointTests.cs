using System.IO;
using System.Text.Json;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Runtime.Serialization;
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

    // Twelve lines each: breakpoints 1..12, bookmarks 0..11. A mark on a line the document does not have is
    // dropped at load (#574), so every mark these tests keep has to be on a real line.
    private static readonly string TwelveLines = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"' line {i}"));

    private ProjectDefinition MakeSavedProject()
    {
        Directory.CreateDirectory(_dir);
        var project = TestHelpers.CreateProjectWithForm("P", "Form1");
        project.AddModule(new ModuleDefinition(project, "Module1", ModuleKind.StandardModule));
        project.Forms[0].UpdateCode(TwelveLines);
        project.Modules[0].UpdateCode(TwelveLines);
        project.AbsolutePath = Path.Combine(_dir, "P.vbp");
        return project;
    }

    private static DocumentIdentity Form1(ProjectDefinition p) => DocumentIdentity.For(p.Forms[0]);
    private static DocumentIdentity Module1(ProjectDefinition p) => DocumentIdentity.For(p.Modules[0]);

    [Fact]
    public async Task Breakpoints_RoundTripThroughSidecar_WrittenBesideTheVbp()
    {
        // Save and Load are driven explicitly. Setting a breakpoint schedules a debounced save as well, and the
        // explicit save supersedes it, so nothing writes the file after this test has read it.
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
        json.RootElement.GetProperty("fileBreakpoints").EnumerateObject()
            .Select(p => p.Name).Should().Equal("Module1");
    }

    /// <summary>
    /// A sidecar written before documents had identities named them by the URI the IDE used internally.
    /// Those keys are still read, so an upgrade does not lose anybody's marks.
    /// </summary>
    /// <remarks>
    /// Such a sidecar also predates lines counted from the top of the file, so its lines are migrated as they are
    /// read: Form1 was created here and has no header, so its bookmark stays where it was, and Module1's code is
    /// now shown under its <c>Attribute VB_Name</c> line, so its breakpoints move down by one.
    /// </remarks>
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
        breakpoints.GetBreakpoints(Module1(project)).Should().Equal(3, 7);
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

        // As the save itself does: the form's file is written as its path is set. A path naming no file is a
        // document whose file is missing, whose marks the next load keeps in the sidecar rather than placing them.
        project.Forms[0].AbsolutePath = Path.Combine(_dir, "Form1.frm");
        await File.WriteAllTextAsync(project.Forms[0].AbsolutePath!, TwelveLines, TestContext.Current.CancellationToken);

        bookmarks.GetBookmarks(Form1(project)).Should().Equal(7);

        await sidecar.SaveAsync(project);
        var reloaded = new BookmarkService();
        await new UserSidecarService(reloaded, new BreakpointService(), projectManager).LoadAsync(project);
        reloaded.GetBookmarks(Form1(project)).Should().Equal(7);
    }

    /// <summary>
    /// A sidecar holding marks on lines the document does not have loads without them, and the next save
    /// writes it without them (#574). Such a sidecar was written before set_breakpoints and set_bookmarks
    /// refused those lines, and can still come from a file shortened outside HexIDE or a hand edit.
    /// </summary>
    [Fact]
    public async Task MarksOnLinesTheDocumentDoesNotHaveAreDroppedAtLoad_AndNotWrittenBack()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(new List<ProjectDefinition>());
        var project = MakeSavedProject();
        var sidecarPath = Path.Combine(_dir, "P.user.hexproj");

        // Form1 was created by HexIDE and has no designer block yet, so its file is its twelve lines of code.
        // Module1's file is those twelve under the Attribute VB_Name line the code window shows: thirteen. That
        // line is read-only, so the lowest breakpoint Module1 can keep is on line 2, its first line of code.
        await File.WriteAllTextAsync(sidecarPath,
            """
            {
              "version": 2,
              "fileBookmarks": { "Form1": [-1, 0, 11, 12, 99] },
              "fileBreakpoints": { "Module1": [-1, 0, 2, 13, 14, 99] }
            }
            """, TestContext.Current.CancellationToken);

        var bookmarks = new BookmarkService();
        var breakpoints = new BreakpointService();
        var sidecar = new UserSidecarService(bookmarks, breakpoints, projectManager);
        await sidecar.LoadAsync(project);

        bookmarks.GetBookmarks(Form1(project)).Should().Equal([0, 11], "bookmarks count from 0");
        breakpoints.GetBreakpoints(Module1(project)).Should().Equal([2, 13], "breakpoints count from 1, header included");

        await sidecar.SaveAsync(project);
        var json = JsonDocument.Parse(await File.ReadAllTextAsync(sidecarPath, TestContext.Current.CancellationToken));
        json.RootElement.GetProperty("fileBookmarks").GetProperty("Form1").EnumerateArray()
            .Select(e => e.GetInt32()).Should().Equal(0, 11);
        json.RootElement.GetProperty("fileBreakpoints").GetProperty("Module1").EnumerateArray()
            .Select(e => e.GetInt32()).Should().Equal(2, 13);
    }

    /// <summary>
    /// A sidecar written before lines counted from the top of the file has its lines checked in the numbering they
    /// were written in, over the document's code, before they move (#273 task 3.17).
    /// </summary>
    /// <remarks>
    /// Module1's code is twelve lines, so in the old numbering its breakpoints run 1..12. Line 0 and line 13 were
    /// never lines of it: they are dropped, rather than moved down by the header's line to land on line 1, inside
    /// the header, and on line 14, past the end. 1 and 12 are its first and last lines of code, now 2 and 13.
    /// </remarks>
    [Fact]
    public async Task AnEarlierSidecarsLinesAreCheckedInTheirOwnNumberingBeforeTheyMove()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(new List<ProjectDefinition>());
        var project = MakeSavedProject();
        var sidecarPath = Path.Combine(_dir, "P.user.hexproj");
        await File.WriteAllTextAsync(sidecarPath,
            """{ "version": 1, "breakpoints": { "Module1": [0, 1, 12, 13] } }""",
            TestContext.Current.CancellationToken);

        var breakpoints = new BreakpointService();
        using var log = new CapturedLog();
        await new UserSidecarService(new BookmarkService(), breakpoints, projectManager).LoadAsync(project);

        breakpoints.GetBreakpoints(Module1(project)).Should().Equal(2, 13);
        log.Messages.Should().ContainSingle(m => m.Contains("had 12 line(s) of code"))
            .Which.Should().Contain("[0, 13]");
        var json = JsonDocument.Parse(await File.ReadAllTextAsync(sidecarPath, TestContext.Current.CancellationToken));
        json.RootElement.GetProperty("fileBreakpoints").GetProperty("Module1").EnumerateArray()
            .Select(e => e.GetInt32()).Should().Equal(2, 13);
        json.RootElement.TryGetProperty("breakpoints", out _).Should().BeFalse();
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    /// <summary>
    /// A form's last lines of code are as far below the top of the file as its designer block is long, and a
    /// mark on one is kept at load (hexide-io/HexIDE#273 task 3.12).
    /// </summary>
    /// <remarks>
    /// The range used to be counted in the form's code section, which is shorter than the file by the designer
    /// block, so these marks were dropped as "not a line" and gone from the next save. This used to keep a mark on
    /// a header line as well, because a sidecar written before the code window held the whole file counted from
    /// the first line of code and that call was the migration's to make. The migration (#273 task 3.17) moves such
    /// lines before they are judged, so a mark on a header line is now dropped; that has a test of its own in
    /// <c>SidecarMigrationTests</c>, and this one keeps to the range.
    /// </remarks>
    [Fact]
    public async Task AMarkOnAFormsLastLineOfCodeIsKeptAtLoad()
    {
        const string frm =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form2 \r\n" +
            "   Caption         =   \"Orders\"\r\n" +
            "   Begin VB.CommandButton Command1 \r\n" +
            "      Caption         =   \"OK\"\r\n" +
            "   End\r\n" +
            "End\r\n" +
            "Attribute VB_Name = \"Form2\"\r\n" +
            "Option Explicit\r\n" +
            "Private Sub Command1_Click()\r\n" +
            "    Command1.Enabled = False\r\n" +
            "End Sub\r\n";
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(new List<ProjectDefinition>());
        var project = MakeSavedProject();
        var form = new FormDeserializer().Deserialize(project, frm, NullSink.Instance)!;
        project.AddForm(form);
        var lastOfCode = frm.Split("\r\n").Length - 1;
        lastOfCode.Should().BeGreaterThan(form.Code.Split("\r\n").Length, "the fixture must reach past the code section's count");

        await File.WriteAllTextAsync(Path.Combine(_dir, "P.user.hexproj"),
            $$"""
            {
              "version": 2,
              "fileBookmarks": { "Form2": [{{lastOfCode - 1}}] },
              "fileBreakpoints": { "Form2": [{{lastOfCode}}] }
            }
            """, TestContext.Current.CancellationToken);

        var bookmarks = new BookmarkService();
        var breakpoints = new BreakpointService();
        await new UserSidecarService(bookmarks, breakpoints, projectManager).LoadAsync(project);

        breakpoints.GetBreakpoints(DocumentIdentity.For(form)).Should().Equal(lastOfCode);
        bookmarks.GetBookmarks(DocumentIdentity.For(form)).Should().Equal(lastOfCode - 1);
    }
}
