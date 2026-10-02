using System.IO;
using System.Text.Json.Nodes;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Sidecar;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace HexIDE.Tests.Sidecar;

/// <summary>
/// What a save writes into a project's sidecar, and when (hexide-io/HexIDE#466): it keeps what it does not
/// understand, creates nothing when there is nothing to record, and every project's last change reaches its own
/// file however the changes and the closing of projects interleave.
/// </summary>
/// <remarks>
/// The debounce is a <see cref="ManualDebounce"/>: a change waits until the test lets it through, so no test sleeps
/// and none can be overtaken by a save it did not ask for.
/// </remarks>
public sealed class UserSidecarSaveTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "HexIDE.Tests.Sidecar", Guid.NewGuid().ToString("N"));

    private readonly IProjectManager projectManager = Substitute.For<IProjectManager>();
    private readonly ManualDebounce debounce = new();

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static readonly string TwelveLines = string.Join("\n", Enumerable.Range(1, 12).Select(i => $"' line {i}"));

    /// <summary>
    /// A saved project called <paramref name="name"/> with a twelve-line form and module, alone in a directory of
    /// its own (<paramref name="folder"/>, or one named after it).
    /// </summary>
    private ProjectDefinition Project(string name = "P", string? folder = null)
    {
        var dir = Path.Combine(_root, folder ?? name);
        Directory.CreateDirectory(dir);
        var project = TestHelpers.CreateProjectWithForm(name, "Form1");
        project.AddModule(new ModuleDefinition(project, "Module1", ModuleKind.StandardModule));
        project.Forms[0].UpdateCode(TwelveLines);
        project.Modules[0].UpdateCode(TwelveLines);
        project.AbsolutePath = Path.Combine(dir, name + ".vbp");
        return project;
    }

    private static string SidecarOf(ProjectDefinition project) =>
        Path.Combine(Path.GetDirectoryName(project.AbsolutePath)!,
            Path.GetFileNameWithoutExtension(project.AbsolutePath) + ".user.hexproj");

    private string Elsewhere(ProjectDefinition project)
    {
        var dir = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Path.GetFileName(project.AbsolutePath)!);
    }

    private static DocumentIdentity Form1(ProjectDefinition p) => DocumentIdentity.For(p.Forms[0]);
    private static DocumentIdentity Module1(ProjectDefinition p) => DocumentIdentity.For(p.Modules[0]);

    private void Close(ProjectDefinition project) =>
        projectManager.ProjectUnloaded += Raise.Event<Action<ProjectDefinition>>(project);

    private UserSidecarService Service(BookmarkService bookmarks, BreakpointService? breakpoints = null,
        Func<string, Task<string>>? read = null) =>
        new(bookmarks, breakpoints ?? new BreakpointService(), projectManager)
        {
            Debounce = debounce.Wait,
            ReadText = read ?? (path => Task.FromResult(File.ReadAllText(path))),
        };

    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private static int[] Lines(JsonObject sidecar, string kind, string document) =>
        sidecar[kind]![document]!.AsArray().Select(n => n!.GetValue<int>()).ToArray();

    private static bool Same(JsonNode? actual, string expected) => JsonNode.DeepEquals(actual, JsonNode.Parse(expected));

    /// <summary>
    /// A debounce that elapses when the test says so, and is cancelled when the service cancels it.
    /// </summary>
    private sealed class ManualDebounce
    {
        private readonly List<TaskCompletionSource> waiting = [];

        public int Started { get; private set; }
        public int Cancelled { get; private set; }

        public Task Wait(CancellationToken token)
        {
            var delay = new TaskCompletionSource();
            Started++;
            token.Register(() =>
            {
                if (delay.TrySetCanceled(token)) Cancelled++;
            });
            waiting.Add(delay);
            return delay.Task;
        }

        /// <summary>Lets every waiting change through, then waits for the saves they start to finish.</summary>
        public async Task ElapseAsync(UserSidecarService service)
        {
            Elapse();
            await service.SettledAsync();
        }

        /// <summary>Lets every waiting change through, without waiting for anything they start.</summary>
        public void Elapse()
        {
            foreach (var delay in waiting) delay.TrySetResult();
            waiting.Clear();
        }
    }

    /// <summary>Collects what is logged while it is installed, and puts the previous logger back when disposed.</summary>
    private sealed class CapturedLog : ILogEventSink, IDisposable
    {
        private readonly ILogger previous = Log.Logger;
        public List<string> Messages { get; } = [];

        public CapturedLog() =>
            Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(this).CreateLogger();

        public void Emit(LogEvent logEvent) => Messages.Add(logEvent.RenderMessage());

        public void Dispose() => Log.Logger = previous;
    }

    // ── A. A save rewrites the sidecar; it does not rebuild it ───────────────────────────────────────────

    [Fact]
    public async Task ContentThisVersionDoesNotRecogniseSurvivesASave()
    {
        // The user-sidecar spec's scenario, exactly as #466 reports it: the watches key used to vanish.
        var project = Project();
        File.WriteAllText(SidecarOf(project),
            """{ "version": 1, "bookmarks": { "vb6://form/Form1": [1] }, "watches": ["x + 1"] }""");
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);
        await sidecar.LoadAsync(project);

        bookmarks.SetBookmarks(Form1(project), [1, 3]);
        await sidecar.SaveAsync(project);

        var written = Read(SidecarOf(project));
        Same(written["watches"], """["x + 1"]""").Should().BeTrue(
            "the unrecognised key is still there with its value, whatever escaping the writer chose");
        Lines(written, "bookmarks", "Form1").Should().Equal(1, 3);
    }

    [Fact]
    public async Task ASidecarFromANewerVersionKeepsItsVersionAndItsContent()
    {
        // Keeping the content and stamping it with this build's version would misdescribe it: the newer build
        // would read its own keys as written by this one.
        var project = Project();
        File.WriteAllText(SidecarOf(project),
            """{ "version": 2, "bookmarks": { "Form1": [1] }, "marks": { "Form1": { "bookmarks": [9] } } }""");
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);
        await sidecar.LoadAsync(project);

        bookmarks.SetBookmarks(Form1(project), [2]);
        await sidecar.SaveAsync(project);

        var written = Read(SidecarOf(project));
        written["version"]!.GetValue<int>().Should().Be(2);
        Same(written["marks"], """{"Form1":{"bookmarks":[9]}}""").Should().BeTrue();
        Lines(written, "bookmarks", "Form1").Should().Equal(2);
    }

    [Fact]
    public async Task ContentAnotherVersionWroteWhileTheProjectWasOpenSurvivesASave()
    {
        // What is kept is what the file holds when it is rewritten, not what it held when the project opened.
        var project = Project();
        File.WriteAllText(SidecarOf(project), """{ "version": 1, "bookmarks": { "Form1": [1] } }""");
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);
        await sidecar.LoadAsync(project);

        File.WriteAllText(SidecarOf(project),
            """{ "version": 2, "bookmarks": { "Form1": [1] }, "marks": { "Form1": { "bookmarks": [9] } } }""");
        bookmarks.SetBookmarks(Form1(project), [1, 4]);
        await sidecar.SaveAsync(project);

        var written = Read(SidecarOf(project));
        written["version"]!.GetValue<int>().Should().Be(2);
        Same(written["marks"], """{"Form1":{"bookmarks":[9]}}""").Should().BeTrue();
        Lines(written, "bookmarks", "Form1").Should().Equal([1, 4], "for this build's own keys, this session wins");
    }

    [Fact]
    public async Task ASidecarThatAppearsWhileTheProjectIsOpenIsKeptAsTheProjectsOwn()
    {
        // Opened with no sidecar; one arrives, from a pull or another machine. It is this project's file.
        var project = Project();
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);
        await sidecar.LoadAsync(project);

        File.WriteAllText(SidecarOf(project), """{ "version": 1, "watches": ["x"] }""");
        bookmarks.SetBookmarks(Form1(project), [2]);
        await sidecar.SaveAsync(project);

        var written = Read(SidecarOf(project));
        Same(written["watches"], """["x"]""").Should().BeTrue();
        Lines(written, "bookmarks", "Form1").Should().Equal(2);
    }

    [Fact]
    public async Task ASidecarWithNoVersionIsWrittenAsThisVersion()
    {
        var project = Project();
        File.WriteAllText(SidecarOf(project), """{ "bookmarks": { "Form1": [1] } }""");
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);
        await sidecar.LoadAsync(project);

        bookmarks.SetBookmarks(Form1(project), [1, 2]);
        await sidecar.SaveAsync(project);

        Read(SidecarOf(project))["version"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task SavingAProjectWithNothingToRecordCreatesNoSidecar()
    {
        var project = Project();
        var sidecar = Service(new BookmarkService());
        await sidecar.LoadAsync(project);

        await sidecar.SaveAsync(project);

        File.Exists(SidecarOf(project)).Should().BeFalse("the sidecar is created only when there is state to record");
    }

    [Fact]
    public async Task RemovingTheLastMarkRewritesTheSidecarRatherThanDeletingIt()
    {
        // It may hold what this build does not understand, and it is a file the user may have committed. An empty
        // kind is left out rather than written as null.
        var project = Project();
        File.WriteAllText(SidecarOf(project),
            """{ "version": 1, "bookmarks": { "Form1": [1] }, "watches": ["x"] }""");
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);
        await sidecar.LoadAsync(project);

        bookmarks.SetBookmarks(Form1(project), []);
        await sidecar.SaveAsync(project);

        Read(SidecarOf(project)).Select(p => p.Key).Should().BeEquivalentTo("version", "watches");
    }

    [Fact]
    public async Task RemovingTheLastMarkFromASidecarHoldingNothingElseKeepsTheFile()
    {
        var project = Project();
        File.WriteAllText(SidecarOf(project), """{ "version": 1, "bookmarks": { "Form1": [1] } }""");
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);
        await sidecar.LoadAsync(project);

        bookmarks.SetBookmarks(Form1(project), []);
        await sidecar.SaveAsync(project);

        Read(SidecarOf(project)).Select(p => p.Key).Should().BeEquivalentTo("version");
    }

    [Fact]
    public async Task ASidecarThatCannotBeReadIsNotRewritten_AndTheLogSaysSo()
    {
        // Everything in it is content this build does not understand, so rewriting it would lose all of it. The
        // paths that run in the IDE are the debounced save and the save on closing, so those are what is driven.
        var project = Project();
        const string damaged = """{ "version": 1, "bookmarks": { "Form1": [1] """;
        File.WriteAllText(SidecarOf(project), damaged);
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);
        using var log = new CapturedLog();
        await sidecar.LoadAsync(project);

        bookmarks.SetBookmarks(Form1(project), [4]);
        await debounce.ElapseAsync(sidecar);
        bookmarks.SetBookmarks(Form1(project), [4, 5]);
        Close(project);

        File.ReadAllText(SidecarOf(project)).Should().Be(damaged);
        log.Messages.Count(m => m.Contains("is not rewritten")).Should().Be(1, "the refusal is logged once, not per change");
    }

    [Fact]
    public async Task ASidecarRepairedWhileTheProjectIsOpenIsStillNotRewrittenUntilItIsReopened()
    {
        // Its marks never reached the stores, so a save now would write the project's documents without them. The
        // refusal holds until the project is opened again and reads them.
        var project = Project();
        File.WriteAllText(SidecarOf(project), "not json");
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);
        await sidecar.LoadAsync(project);

        const string repaired = """{ "version": 1, "breakpoints": { "Module1": [3] } }""";
        File.WriteAllText(SidecarOf(project), repaired);
        bookmarks.SetBookmarks(Form1(project), [2]);
        await debounce.ElapseAsync(sidecar);

        File.ReadAllText(SidecarOf(project)).Should().Be(repaired);
    }

    [Fact]
    public async Task ASidecarWhoseMarksCannotBeAppliedIsNotRewritten()
    {
        // It parses, but a document's lines are null: some marks may have reached the stores and some not, so a
        // rewrite would keep only those that did.
        var project = Project();
        const string odd = """{ "version": 1, "bookmarks": { "Form1": null }, "watches": ["x"] }""";
        File.WriteAllText(SidecarOf(project), odd);
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);
        await sidecar.LoadAsync(project);

        bookmarks.SetBookmarks(Module1(project), [2]);
        await debounce.ElapseAsync(sidecar);

        File.ReadAllText(SidecarOf(project)).Should().Be(odd);
    }

    [Fact]
    public async Task ASidecarThatStopsBeingReadableWhileTheProjectIsOpenIsNotRewritten()
    {
        // A pull that leaves conflict markers in it, say. It is read again at the save, and refused then.
        var project = Project();
        File.WriteAllText(SidecarOf(project), """{ "version": 1, "bookmarks": { "Form1": [1] } }""");
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);
        await sidecar.LoadAsync(project);

        const string conflicted = "<<<<<<< HEAD\n{ \"version\": 1 }\n=======\n{ \"version\": 2 }\n>>>>>>> theirs\n";
        File.WriteAllText(SidecarOf(project), conflicted);
        bookmarks.SetBookmarks(Form1(project), [1, 2]);
        await debounce.ElapseAsync(sidecar);

        File.ReadAllText(SidecarOf(project)).Should().Be(conflicted);
    }

    [Fact]
    public async Task ASidecarThatCanNoLongerBeReadIsReportedOnce()
    {
        var project = Project();
        File.WriteAllText(SidecarOf(project), """{ "version": 1, "bookmarks": { "Form1": [1] } }""");
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);
        await sidecar.LoadAsync(project);
        File.WriteAllText(SidecarOf(project), "not json any more");
        using var log = new CapturedLog();

        bookmarks.SetBookmarks(Form1(project), [1, 2]);
        await debounce.ElapseAsync(sidecar);
        bookmarks.SetBookmarks(Form1(project), [1, 2, 3]);
        await debounce.ElapseAsync(sidecar);
        Close(project);

        log.Messages.Count(m => m.Contains("can no longer be read")).Should().Be(1);
    }

    [Fact]
    public async Task ASidecarDeletedWhileTheProjectIsOpenIsNotRecreatedWithWhatItHeld()
    {
        // Deleting it is how a developer discards what it held. The marks are this session's, so they are
        // written again; nothing else of it is.
        var project = Project();
        File.WriteAllText(SidecarOf(project),
            """{ "version": 2, "bookmarks": { "Form1": [1] }, "marks": { "Form1": { "bookmarks": [9] } } }""");
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);
        await sidecar.LoadAsync(project);

        File.Delete(SidecarOf(project));
        bookmarks.SetBookmarks(Form1(project), [1, 2]);
        await sidecar.SaveAsync(project);

        var written = Read(SidecarOf(project));
        written.Select(p => p.Key).Should().BeEquivalentTo("version", "bookmarks");
        written["version"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task TwoKeysNamingOneDocumentAreUnited()
    {
        // Both spellings, as a merge of two sidecars can leave them. Setting each in turn kept only the last.
        var project = Project();
        File.WriteAllText(SidecarOf(project),
            """{ "version": 1, "breakpoints": { "vb6://form/Form1": [3], "Form1": [5] } }""");
        var breakpoints = new BreakpointService();
        var sidecar = Service(new BookmarkService(), breakpoints);

        await sidecar.LoadAsync(project);

        breakpoints.GetBreakpoints(Form1(project)).Should().BeEquivalentTo([3, 5]);
    }

    // ── A project saved elsewhere ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASidecarsContentMovesWithAProjectSavedElsewhere()
    {
        var project = Project();
        File.WriteAllText(SidecarOf(project), """{ "version": 1, "bookmarks": { "Form1": [1] }, "watches": ["x"] }""");
        var sidecar = Service(new BookmarkService());
        await sidecar.LoadAsync(project);

        project.AbsolutePath = Elsewhere(project);
        await sidecar.SaveAsync(project);

        var written = Read(SidecarOf(project));
        Same(written["watches"], """["x"]""").Should().BeTrue();
        Lines(written, "bookmarks", "Form1").Should().Equal(1);
    }

    [Fact]
    public async Task ASidecarHoldingOnlyContentThisVersionDoesNotUnderstandMovesWithTheProjectToo()
    {
        // A newer build's marks under keys this one does not know count as something to record: the project has
        // no marks this build can see, and still takes them with it.
        var project = Project();
        File.WriteAllText(SidecarOf(project), """{ "version": 2, "marks": { "Form1": { "bookmarks": [9] } } }""");
        var sidecar = Service(new BookmarkService());
        await sidecar.LoadAsync(project);

        project.AbsolutePath = Elsewhere(project);
        await sidecar.SaveAsync(project);

        var written = Read(SidecarOf(project));
        written["version"]!.GetValue<int>().Should().Be(2);
        Same(written["marks"], """{"Form1":{"bookmarks":[9]}}""").Should().BeTrue();
    }

    [Fact]
    public async Task ASidecarAlreadyWhereAProjectIsSavedIsReplacedByTheProjectsOwn()
    {
        // As the project file beside it is. Its content belongs to whatever project was there before.
        var project = Project();
        File.WriteAllText(SidecarOf(project), """{ "version": 1, "bookmarks": { "Form1": [1] } }""");
        var sidecar = Service(new BookmarkService());
        await sidecar.LoadAsync(project);

        var destination = Elsewhere(project);
        File.WriteAllText(Path.ChangeExtension(destination, null) + ".user.hexproj",
            """{ "version": 2, "bookmarks": { "Form1": [11] }, "stale": true }""");
        project.AbsolutePath = destination;
        await sidecar.SaveAsync(project);

        var written = Read(SidecarOf(project));
        written.ContainsKey("stale").Should().BeFalse();
        written["version"]!.GetValue<int>().Should().Be(1);
        Lines(written, "bookmarks", "Form1").Should().Equal(1);
    }

    [Fact]
    public async Task ANewProjectsFirstSaveReplacesAStaleSidecarRatherThanTakingItsContent()
    {
        // A sidecar left beside where a deleted project was. Its marks and its keys are that project's: none of it
        // is grafted onto the new one, which would later show marks nobody set in it.
        var project = Project();
        File.WriteAllText(SidecarOf(project),
            """{ "version": 2, "bookmarks": { "Form1": [5] }, "marks": { "Form1": { "bookmarks": [9] } } }""");
        var bookmarks = new BookmarkService();
        var breakpoints = new BreakpointService();
        var sidecar = Service(bookmarks, breakpoints);

        breakpoints.SetDocument(Module1(project), [2]);
        await sidecar.SaveAsync(project);

        var written = Read(SidecarOf(project));
        written.Select(p => p.Key).Should().BeEquivalentTo("version", "breakpoints");
        written["version"]!.GetValue<int>().Should().Be(1);
        Lines(written, "breakpoints", "Module1").Should().Equal(2);
        bookmarks.GetBookmarks(Form1(project)).Should().BeEmpty();
    }

    [Fact]
    public async Task AWriteThatFailsAtANewPlaceDoesNotMakeItTheProjectsOwn()
    {
        // Otherwise the next save would read the sidecar lying there as the project's own and keep its content.
        var project = Project();
        File.WriteAllText(SidecarOf(project), """{ "version": 1, "bookmarks": { "Form1": [1] } }""");
        var sidecar = Service(new BookmarkService());
        await sidecar.LoadAsync(project);

        var destination = Elsewhere(project);
        var lyingThere = Path.ChangeExtension(destination, null) + ".user.hexproj";
        File.WriteAllText(lyingThere, """{ "version": 1, "stale": true }""");
        project.AbsolutePath = destination;
        Directory.CreateDirectory(lyingThere + ".tmp");
        await sidecar.SaveAsync(project);
        Directory.Delete(lyingThere + ".tmp");
        await sidecar.SaveAsync(project);

        var written = Read(lyingThere);
        written.ContainsKey("stale").Should().BeFalse();
        Lines(written, "bookmarks", "Form1").Should().Equal(1);
    }

    [Fact]
    public async Task AWriteThatFailsAtANewPlaceIsMadeAgainWhenTheProjectCloses()
    {
        // No mark changed, so nothing but the failed write itself says the sidecar is owed there.
        var project = Project();
        File.WriteAllText(SidecarOf(project), """{ "version": 1, "bookmarks": { "Form1": [1] }, "watches": ["x"] }""");
        var sidecar = Service(new BookmarkService());
        await sidecar.LoadAsync(project);

        project.AbsolutePath = Elsewhere(project);
        var blocker = SidecarOf(project) + ".tmp";
        Directory.CreateDirectory(blocker);
        await sidecar.SaveAsync(project);
        Directory.Delete(blocker);
        Close(project);

        var written = Read(SidecarOf(project));
        Same(written["watches"], """["x"]""").Should().BeTrue();
        Lines(written, "bookmarks", "Form1").Should().Equal(1);
    }

    [Fact]
    public async Task ASidecarThatAppearsAfterASaveWithNothingToRecordIsKeptAsTheProjectsOwn()
    {
        // A new project saved with no marks writes nothing, and its place is its own from then on: a sidecar
        // another build writes there is kept, as it would be for a project opened there.
        var project = Project();
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);
        await sidecar.SaveAsync(project);
        File.Exists(SidecarOf(project)).Should().BeFalse();

        File.WriteAllText(SidecarOf(project), """{ "version": 2, "marks": { "Form1": { "bookmarks": [9] } } }""");
        bookmarks.SetBookmarks(Form1(project), [2]);
        await debounce.ElapseAsync(sidecar);

        var written = Read(SidecarOf(project));
        written["version"]!.GetValue<int>().Should().Be(2);
        Same(written["marks"], """{"Form1":{"bookmarks":[9]}}""").Should().BeTrue();
    }

    [WindowsOnlyFact]
    public async Task TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn()
    {
        // On Windows a path differing only in case names the same file. Saved under that spelling, the project is
        // still at its own place, so an unreadable sidecar there is still not rewritten.
        var project = Project("Payroll");
        const string damaged = "not json";
        File.WriteAllText(SidecarOf(project), damaged);
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);
        await sidecar.LoadAsync(project);

        project.AbsolutePath = Path.Combine(Path.GetDirectoryName(project.AbsolutePath)!, "PAYROLL.vbp");
        bookmarks.SetBookmarks(Form1(project), [2]);
        await sidecar.SaveAsync(project);

        File.ReadAllText(SidecarOf(project)).Should().Be(damaged);
    }

    [Fact]
    public async Task AnUnreadableSidecarDoesNotStopAProjectSavedElsewhereFromWritingOne()
    {
        // The refusal protects that file. The new place has nothing to protect.
        var project = Project();
        File.WriteAllText(SidecarOf(project), "not json");
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);
        await sidecar.LoadAsync(project);

        project.AbsolutePath = Elsewhere(project);
        bookmarks.SetBookmarks(Form1(project), [3]);
        await sidecar.SaveAsync(project);

        Lines(Read(SidecarOf(project)), "bookmarks", "Form1").Should().Equal(3);
    }

    // ── B. Every project's last change reaches its own file ──────────────────────────────────────────────

    [Fact]
    public async Task AChangeIsWrittenOnceTheDebounceElapses()
    {
        // The control for the cases below: the debounce itself works.
        var project = Project();
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);

        bookmarks.SetBookmarks(Form1(project), [3]);
        File.Exists(SidecarOf(project)).Should().BeFalse("nothing is written while the change waits");
        await debounce.ElapseAsync(sidecar);

        Lines(Read(SidecarOf(project)), "bookmarks", "Form1").Should().Equal(3);
    }

    [Fact]
    public async Task ALaterChangeSupersedesTheWaitingOne_AndBothAreWritten()
    {
        var project = Project();
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);

        bookmarks.SetBookmarks(Form1(project), [3]);
        bookmarks.SetBookmarks(Module1(project), [4]);
        debounce.Cancelled.Should().Be(1, "the first change's wait gave way to the second's");
        await debounce.ElapseAsync(sidecar);

        var written = Read(SidecarOf(project));
        Lines(written, "bookmarks", "Form1").Should().Equal(3);
        Lines(written, "bookmarks", "Module1").Should().Equal(4);
    }

    [Fact]
    public async Task AChangeInOneProjectDoesNotCancelAnothersWaitingSave()
    {
        // #466 case 4: one shared debounce wrote B's sidecar and never A's. The two projects have one name, as two
        // in a group can, so it is the instance that tells them apart.
        var a = Project("P", folder: "A");
        var b = Project("P", folder: "B");
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);

        bookmarks.SetBookmarks(Form1(a), [1]);
        bookmarks.SetBookmarks(Form1(b), [2]);
        debounce.Cancelled.Should().Be(0);
        await debounce.ElapseAsync(sidecar);

        Lines(Read(SidecarOf(a)), "bookmarks", "Form1").Should().Equal(1);
        Lines(Read(SidecarOf(b)), "bookmarks", "Form1").Should().Equal(2);
    }

    [Fact]
    public async Task ClosingOneProjectDoesNotCancelAnothersWaitingSave()
    {
        var a = Project("P", folder: "A");
        var b = Project("P", folder: "B");
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);

        bookmarks.SetBookmarks(Form1(a), [1]);
        Close(b);
        await debounce.ElapseAsync(sidecar);

        Lines(Read(SidecarOf(a)), "bookmarks", "Form1").Should().Equal(1);
        File.Exists(SidecarOf(b)).Should().BeFalse("B had nothing to record");
    }

    [Fact]
    public async Task ClosingAProjectStraightAfterAChangeWritesThatChange_ThenClearsIt()
    {
        // #466 case 5: closing used to cancel the waiting save, and the change was gone at the next launch. It is
        // written first, with the state as it stood, and only then cleared from memory, so the clearing is
        // never what gets written.
        var project = Project();
        var bookmarks = new BookmarkService();
        var breakpoints = new BreakpointService();
        var sidecar = Service(bookmarks, breakpoints);

        bookmarks.SetBookmarks(Form1(project), [3]);
        breakpoints.SetDocument(Module1(project), [5]);
        Close(project);

        bookmarks.GetBookmarks(Form1(project)).Should().BeEmpty("the project is cleared from memory");
        var written = Read(SidecarOf(project));
        Lines(written, "bookmarks", "Form1").Should().Equal(3);
        Lines(written, "breakpoints", "Module1").Should().Equal(5);

        await debounce.ElapseAsync(sidecar);
        Lines(Read(SidecarOf(project)), "bookmarks", "Form1").Should().Equal(3);
    }

    [Fact]
    public async Task AWaitingSaveThatWakesAfterItsProjectClosedWritesNothing()
    {
        // In the IDE a debounced save resumes on the UI thread, so its delay can finish while the project is being
        // closed, and run afterwards. By then the stores are empty: writing them would erase every mark the close
        // had just written. Here the resumption is held in a queue until after the close, as the UI thread holds it.
        var project = Project();
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);
        var uiThread = new QueuedContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(uiThread);
        try
        {
            bookmarks.SetBookmarks(Form1(project), [3]);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        // Completed from outside the UI thread, as a timer does, so the resumption is posted to it rather than run
        // inline.
        debounce.Elapse();
        uiThread.Count.Should().Be(1, "the save has woken and waits to resume");

        Close(project);
        uiThread.Pump();
        await sidecar.SettledAsync();

        Lines(Read(SidecarOf(project)), "bookmarks", "Form1").Should().Equal(3);
    }

    /// <summary>Runs what is posted to it only when pumped, as a busy UI thread would.</summary>
    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> posted = new();

        public int Count => posted.Count;

        public override void Post(SendOrPostCallback d, object? state) => posted.Enqueue((d, state));

        public void Pump()
        {
            while (posted.TryDequeue(out var work)) work.Callback(work.State);
        }
    }

    [Fact]
    public async Task AWriteThatFailsIsMadeAgainWhenTheProjectCloses()
    {
        // Another program holding the file, say. The change stays owed rather than being dropped with the attempt.
        // Here the temporary file's path is a directory, which no write can replace, on any system.
        var project = Project();
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);
        var blocker = SidecarOf(project) + ".tmp";
        Directory.CreateDirectory(blocker);

        bookmarks.SetBookmarks(Form1(project), [3]);
        await debounce.ElapseAsync(sidecar);
        File.Exists(SidecarOf(project)).Should().BeFalse("the write could not be made");

        Directory.Delete(blocker);
        Close(project);

        Lines(Read(SidecarOf(project)), "bookmarks", "Form1").Should().Equal(3);
    }

    [Fact]
    public async Task ClosingTheIdeWithoutClosingItsProjectsWritesWhatIsOwed()
    {
        // A forced close does not close the projects, so the save on a project's close never runs. The IDE writes
        // what is owed as its window closes.
        var a = Project("A");
        var b = Project("B");
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);

        bookmarks.SetBookmarks(Form1(a), [1]);
        bookmarks.SetBookmarks(Form1(b), [2]);
        sidecar.FlushAll();

        Lines(Read(SidecarOf(a)), "bookmarks", "Form1").Should().Equal(1);
        Lines(Read(SidecarOf(b)), "bookmarks", "Form1").Should().Equal(2);
        debounce.Cancelled.Should().Be(2, "the waiting saves were superseded by the flush");
        await sidecar.SettledAsync();
    }

    [Fact]
    public async Task AnExplicitSaveSupersedesTheWaitingOne()
    {
        var project = Project();
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);

        bookmarks.SetBookmarks(Form1(project), [3]);
        await sidecar.SaveAsync(project);

        debounce.Cancelled.Should().Be(1);
        Lines(Read(SidecarOf(project)), "bookmarks", "Form1").Should().Equal(3);
    }

    [Fact]
    public async Task LoadingASidecarSchedulesNoSave()
    {
        // Its marks reach the stores by this service's own hand, and writing them straight back would only churn
        // the file.
        var project = Project();
        File.WriteAllText(SidecarOf(project), """{ "version": 1, "bookmarks": { "Form1": [1] } }""");
        var sidecar = Service(new BookmarkService());

        await sidecar.LoadAsync(project);

        debounce.Started.Should().Be(0);
    }

    [Fact]
    public async Task LoadingOneProjectDoesNotSilenceAnothersChanges()
    {
        // It used to be one flag for every project, raised across the file read.
        var a = Project("A");
        var b = Project("B");
        File.WriteAllText(SidecarOf(b), """{ "version": 1, "bookmarks": { "Form1": [1] } }""");
        var read = new TaskCompletionSource<string>();
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks, read: _ => read.Task);

        var loading = sidecar.LoadAsync(b);
        bookmarks.SetBookmarks(Form1(a), [4]);
        debounce.Started.Should().Be(1, "A's change is waiting to be written while B's sidecar is read");

        read.SetResult(File.ReadAllText(SidecarOf(b)));
        await loading;
        await debounce.ElapseAsync(sidecar);
        Lines(Read(SidecarOf(a)), "bookmarks", "Form1").Should().Equal(4);
    }

    [Fact]
    public async Task AChangeMadeWhileTheSidecarIsReadWaitsForIt()
    {
        // Written then, it would carry none of the file's marks, since the stores do not hold them yet, and they
        // would be gone from the file for good.
        var project = Project();
        const string sidecarText = """{ "version": 1, "breakpoints": { "Module1": [3] } }""";
        File.WriteAllText(SidecarOf(project), sidecarText);
        var read = new TaskCompletionSource<string>();
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks, read: _ => read.Task);

        var loading = sidecar.LoadAsync(project);
        bookmarks.SetBookmarks(Form1(project), [4]);
        await debounce.ElapseAsync(sidecar);
        File.ReadAllText(SidecarOf(project)).Should().Be(sidecarText, "nothing is written while the file is read");

        read.SetResult(sidecarText);
        await loading;

        var written = Read(SidecarOf(project));
        Lines(written, "breakpoints", "Module1").Should().Equal(3);
        Lines(written, "bookmarks", "Form1").Should().Equal(4);
    }

    [Fact]
    public async Task AProjectClosedWhileItsSidecarIsReadGetsNoneOfIt()
    {
        // The stores outlive the project. Marks put there for a closed project would stay, and the next change to
        // any of them, such as Clear All Breakpoints, would write that project's sidecar without them.
        var project = Project();
        const string sidecarText = """{ "version": 1, "breakpoints": { "Module1": [3] } }""";
        File.WriteAllText(SidecarOf(project), sidecarText);
        var read = new TaskCompletionSource<string>();
        var breakpoints = new BreakpointService();
        var sidecar = Service(new BookmarkService(), breakpoints, read: _ => read.Task);

        var loading = sidecar.LoadAsync(project);
        Close(project);
        read.SetResult(sidecarText);
        await loading;

        breakpoints.GetBreakpoints(Module1(project)).Should().BeEmpty();
        breakpoints.ClearAll();
        await debounce.ElapseAsync(sidecar);
        File.ReadAllText(SidecarOf(project)).Should().Be(sidecarText);
    }

    [Fact]
    public async Task AFormAndAModuleOfOneNameShareTheirEntry_AndTheSidecarIsStillWritten()
    {
        // A .vbp written elsewhere can hold both. A save unions their marks under the one name, so a load gives the
        // entry to each; refusing the sidecar instead would stop the project saving any mark at all.
        var project = Project();
        project.AddModule(new ModuleDefinition(project, "Form1", ModuleKind.StandardModule));
        project.Modules[1].UpdateCode(TwelveLines);
        File.WriteAllText(SidecarOf(project), """{ "version": 1, "bookmarks": { "Form1": [1] } }""");
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);
        await sidecar.LoadAsync(project);

        bookmarks.GetBookmarks(Form1(project)).Should().Equal(1);
        bookmarks.GetBookmarks(DocumentIdentity.For(project.Modules[1])).Should().Equal(1);

        bookmarks.SetBookmarks(DocumentIdentity.For(project.Modules[1]), [1, 6]);
        await sidecar.SaveAsync(project);

        Lines(Read(SidecarOf(project)), "bookmarks", "Form1").Should().Equal(1, 6);
    }

    [Fact]
    public async Task ASaveThatWouldChangeNothingLeavesTheFileAlone()
    {
        var project = Project();
        var bookmarks = new BookmarkService();
        var sidecar = Service(bookmarks);
        bookmarks.SetBookmarks(Form1(project), [3]);
        await sidecar.SaveAsync(project);
        var path = SidecarOf(project);
        var stamp = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);

        await sidecar.SaveAsync(project);

        File.GetLastWriteTimeUtc(path).Should().Be(stamp);
    }
}
