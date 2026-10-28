using System.IO;
using System.Text.Json.Nodes;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Sidecar;

/// <summary>
/// A sidecar written before lines counted from the top of the file is migrated as its project opens
/// (hexide-io/HexIDE#273 task 3.17): every mark stays on the statement it was set on, an entry that cannot be
/// placed is kept untouched for a later opening, and the file is rewritten once and only when something moved.
/// </summary>
/// <remarks>
/// <para>
/// Most of these open real files through <see cref="ProjectService"/>, because the shift each document's marks
/// take is whatever the loader puts in front of its code, and a project assembled by hand here would only agree
/// with this test's idea of that. The fixtures are in the shape VB6 writes them: a form with its designer block
/// and five <c>Attribute</c> lines, a module, a class with the standard header, and a UserControl.
/// </para>
/// <para>
/// "On its statement" is asserted by reading the line the mark is on, not by arithmetic: a test that computed
/// the expected line the way the migration does would agree with any mistake in it.
/// </para>
/// </remarks>
public sealed class SidecarMigrationTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "HexIDE.Tests.Sidecar", Guid.NewGuid().ToString("N"));
    private readonly IProjectManager projectManager = Substitute.For<IProjectManager>();
    private readonly List<ProjectDefinition> loaded = [];
    private Action<ProjectDefinition>? beforeSidecar;

    public SidecarMigrationTests()
    {
        Directory.CreateDirectory(dir);
        projectManager.LoadedProjects.Returns(_ => loaded);
        projectManager.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
            .Do(ci =>
            {
                var project = ci.Arg<ProjectDefinition>();
                loaded.Add(project);
                beforeSidecar?.Invoke(project);
            });
    }

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────────────────────────────────

    private static string Crlf(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static readonly string OrdersFrm = Crlf(
        "VERSION 5.00",
        "Begin VB.Form Orders ",
        "   Caption         =   \"Orders\"",
        "   ClientHeight    =   3195",
        "   ClientLeft      =   60",
        "   ClientTop       =   345",
        "   ClientWidth     =   4680",
        "   LinkTopic       =   \"Form1\"",
        "   ScaleHeight     =   3195",
        "   ScaleWidth      =   4680",
        "   StartUpPosition =   3  'Windows Default",
        "   Begin VB.CommandButton cmdTotal ",
        "      Caption         =   \"Total\"",
        "      Height          =   495",
        "      Left            =   1680",
        "      TabIndex        =   0",
        "      Top             =   1320",
        "      Width           =   1215",
        "   End",
        "End",
        "Attribute VB_Name = \"Orders\"",
        "Attribute VB_GlobalNameSpace = False",
        "Attribute VB_Creatable = False",
        "Attribute VB_PredeclaredId = True",
        "Attribute VB_Exposed = False",
        "Option Explicit",
        "",
        "Private Sub cmdTotal_Click()",
        "    Dim total As Currency",
        "    total = 12.5 * 3",
        "    MsgBox \"Total: \" & total",
        "End Sub");

    private static readonly string UtilsBas = Crlf(
        "Attribute VB_Name = \"Utils\"",
        "Option Explicit",
        "",
        "Public Function Twice(ByVal n As Long) As Long",
        "Attribute Twice.VB_Description = \"Doubles a number\"",
        "    Twice = n * 2",
        "End Function");

    private static readonly string LedgerCls = Crlf(
        "VERSION 1.0 CLASS",
        "BEGIN",
        "  MultiUse = -1  'True",
        "  Persistable = 0  'NotPersistable",
        "  DataBindingBehavior = 0  'vbNone",
        "  DataSourceBehavior  = 0  'vbNone",
        "  MTSTransactionMode  = 0  'NotAnMTSObject",
        "END",
        "Attribute VB_Name = \"Ledger\"",
        "Attribute VB_GlobalNameSpace = False",
        "Attribute VB_Creatable = True",
        "Attribute VB_PredeclaredId = False",
        "Attribute VB_Exposed = False",
        "Option Explicit",
        "Private mBalance As Currency",
        "Public Sub Post(ByVal amount As Currency)",
        "    mBalance = mBalance + amount",
        "End Sub");

    private static readonly string GaugeCtl = Crlf(
        "VERSION 5.00",
        "Begin VB.UserControl Gauge ",
        "   ClientHeight    =   3600",
        "   ClientLeft      =   0",
        "   ClientTop       =   0",
        "   ClientWidth     =   4800",
        "   ScaleHeight     =   3600",
        "   ScaleWidth      =   4800",
        "End",
        "Attribute VB_Name = \"Gauge\"",
        "Attribute VB_GlobalNameSpace = False",
        "Attribute VB_Creatable = True",
        "Attribute VB_PredeclaredId = False",
        "Attribute VB_Exposed = True",
        "Option Explicit",
        "",
        "Private mLevel As Integer",
        "",
        "Public Property Let Level(ByVal newLevel As Integer)",
        "    mLevel = newLevel",
        "    PropertyChanged \"Level\"",
        "End Property");

    /// <summary>For each document, the statement a mark is set on and the text that holds it.</summary>
    public static TheoryData<string> Documents => new() { "Orders", "Utils", "Ledger", "Gauge" };

    private static readonly Dictionary<string, (string File, string Statement)> Marked = new()
    {
        ["Orders"] = ("Orders.frm", "    total = 12.5 * 3"),
        ["Utils"] = ("Utils.bas", "    Twice = n * 2"),
        ["Ledger"] = ("Ledger.cls", "    mBalance = mBalance + amount"),
        ["Gauge"] = ("Gauge.ctl", "    mLevel = newLevel"),
    };

    /// <summary>
    /// Writes the four documents and a .vbp naming them, plus any <paramref name="extraVbpLines"/>, and returns the
    /// .vbp's path. The sidecar beside it is written separately.
    /// </summary>
    private string Stage(params string[] extraVbpLines)
    {
        File.WriteAllText(Path.Join(dir, "Orders.frm"), OrdersFrm);
        File.WriteAllText(Path.Join(dir, "Utils.bas"), UtilsBas);
        File.WriteAllText(Path.Join(dir, "Ledger.cls"), LedgerCls);
        File.WriteAllText(Path.Join(dir, "Gauge.ctl"), GaugeCtl);
        var vbp = Path.Join(dir, "Shop.vbp");
        File.WriteAllText(vbp, Crlf([
            "Type=Exe",
            "Form=Orders.frm",
            "Module=Utils; Utils.bas",
            "Class=Ledger; Ledger.cls",
            "UserControl=Gauge.ctl",
            .. extraVbpLines,
            "Startup=\"Orders\"",
            "Name=\"Shop\"",
        ]));
        return vbp;
    }

    private string SidecarPath => Path.Join(dir, "Shop.user.hexproj");

    private sealed record Opened(
        ProjectDefinition Project, BookmarkService Bookmarks, BreakpointService Breakpoints, UserSidecarService Sidecar);

    /// <summary>Opens the project as the IDE does, with this test's own stores and sidecar service.</summary>
    private async Task<Opened> Open(string vbp)
    {
        loaded.Clear();
        var bookmarks = new BookmarkService();
        var breakpoints = new BreakpointService();
        var sidecar = new UserSidecarService(bookmarks, breakpoints, projectManager)
        {
            // A change waits until the test saves explicitly; nothing is written behind its back.
            Debounce = token => Task.Delay(Timeout.Infinite, token),
        };

        var windowManager = Substitute.For<IWindowManager>();
        windowManager.ShowDialog(Arg.Any<IDialog>()).Returns(ci =>
        {
            if (ci.Arg<IDialog>() is SaveChangesViewModel vm) vm.Yes();
            return Task.FromResult(true);
        });

        var service = new ProjectService(
            () => throw new InvalidOperationException("new-project dialog must not be reached"),
            windowManager,
            Substitute.For<IEventBus>(),
            projectManager,
            Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(),
            sidecar,
            new FileBaselineStore(),
            Substitute.For<HexIDE.Localization.ILocalizationService>(),
            Substitute.For<IHeaderRefresher>());
        await service.OpenProject(vbp);
        return new Opened(loaded.Single(), bookmarks, breakpoints, sidecar);
    }

    private void Close(ProjectDefinition project) =>
        projectManager.ProjectUnloaded += Raise.Event<Action<ProjectDefinition>>(project);

    private static DocumentIdentity Doc(ProjectDefinition project, string name) =>
        DocumentLookup.DocumentsOf(project).Single(d => d.Name == name);

    /// <summary>The 1-based line of a document as its code window numbers it, without its terminator.</summary>
    private static string LineOf(DocumentIdentity document, int line) =>
        CodeWindowText.Of(document).Text.Split('\n')[line - 1].TrimEnd('\r');

    /// <summary>The 1-based line the statement was on in the document's code, which is how version 1 counted.</summary>
    private static int OldLineOf(string fileText, ModuleKind? kind, string statement)
    {
        // The code section as the reader splits it: after the designer block for a form or UserControl, after the
        // header for a module or class.
        var code = kind is { } k
            ? ModuleFileFormat.SplitHeader(fileText, k).Body
            : fileText[(fileText.IndexOf("Attribute VB_Name", StringComparison.Ordinal))..];
        var lines = code.Replace("\r\n", "\n").Split('\n');
        return Array.IndexOf(lines, statement) + 1;
    }

    private static JsonObject ReadJson(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private static int[] Lines(JsonObject sidecar, string kind, string document) =>
        sidecar[kind]![document]!.AsArray().Select(n => n!.GetValue<int>()).ToArray();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MigratedMarksSurviveAnUnappliedHeaderChange(int version)
    {
        var vbp = Stage();
        var key = version == 1 ? "breakpoints" : "fileBreakpoints";
        var bookmarkKey = version == 1 ? "bookmarks" : "fileBookmarks";
        var line = version == 1 ? 10 : 30;
        File.WriteAllText(SidecarPath,
            $$"""{ "version": {{version}}, "{{key}}": { "Orders": [{{line}}] }, "{{bookmarkKey}}": { "Orders": [{{line - 1}}] } }""");
        var opened = await Open(vbp);
        var orders = Doc(opened.Project, "Orders");
        LineOf(orders, opened.Breakpoints.GetBreakpoints(orders).Single()).Should().Be("    total = 12.5 * 3");

        // A conflicted external edit stays on disk; neither the model nor its marks move before close.
        File.WriteAllText(Path.Join(dir, "Orders.frm"), OrdersFrm.Replace(
            "   Caption         =   \"Orders\"", "   Caption         =   \"Orders\"\r\n   ' extra header line"));
        await opened.Sidecar.SaveAsync(opened.Project);
        Close(opened.Project);
        var reopened = await Open(vbp);
        orders = Doc(reopened.Project, "Orders");
        LineOf(orders, reopened.Breakpoints.GetBreakpoints(orders).Single()).Should().Be("    total = 12.5 * 3");
        LineOf(orders, reopened.Bookmarks.GetBookmarks(orders).Single() + 1).Should().Be("    total = 12.5 * 3");
        ReadJson(SidecarPath)["version"]!.GetValue<int>().Should().Be(3);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task VersionThreeKeepsHeaderMarksAtTheirFileLines(int version)
    {
        var vbp = Stage();
        var bookmarkKey = version == 2 ? "fileBookmarks" : "headerBookmarks";
        var breakpointKey = version == 2 ? "fileBreakpoints" : "headerBreakpoints";
        File.WriteAllText(SidecarPath,
            $$"""{ "version": {{version}}, "{{bookmarkKey}}": { "Orders": [1] }, "{{breakpointKey}}": { "Orders": [2] } }""");
        var opened = await Open(vbp);
        var orders = Doc(opened.Project, "Orders");
        opened.Bookmarks.GetBookmarks(orders).Should().Equal(1);
        opened.Breakpoints.GetBreakpoints(orders).Should().Equal(2);
        await opened.Sidecar.SaveAsync(opened.Project);
        Lines(ReadJson(SidecarPath), "headerBookmarks", "Orders").Should().Equal(1);
        Lines(ReadJson(SidecarPath), "headerBreakpoints", "Orders").Should().Equal(2);
    }

    [Theory]
    [InlineData("Orders", 5, false)]
    [InlineData("Utils", 5, false)]
    [InlineData("Ledger", 4, false)]
    [InlineData("Gauge", 6, false)]
    [InlineData("Orders", 5, true)]
    [InlineData("Utils", 5, true)]
    [InlineData("Ledger", 4, true)]
    [InlineData("Gauge", 6, true)]
    public async Task VersionThreeWritesCodeOffsetsAndReopensAfterHeaderChange(string name, int offset, bool shorter)
    {
        var vbp = Stage();
        var path = Path.Join(dir, Marked[name].File);
        if (shorter)
        {
            var original = File.ReadAllText(path);
            File.WriteAllText(path, original.Insert(original.IndexOf("Attribute VB_Name", StringComparison.Ordinal),
                "Attribute VB_Description = \"extra header line\"\r\n"));
        }
        var opened = await Open(vbp);
        var document = Doc(opened.Project, name);
        var statement = Marked[name].Statement;
        var line = Array.IndexOf(CodeWindowText.Of(document).Text.ReplaceLineEndings("\n").Split('\n'), statement) + 1;
        opened.Bookmarks.SetBookmarks(document, [line - 1]);
        opened.Breakpoints.SetDocument(document, [line]);
        await opened.Sidecar.SaveAsync(opened.Project);
        var written = ReadJson(SidecarPath);
        written["version"]!.GetValue<int>().Should().Be(3);
        Lines(written, "codeBookmarks", name).Should().Equal(offset - 1);
        Lines(written, "codeBreakpoints", name).Should().Equal(offset);
        written.ContainsKey("fileBookmarks").Should().BeFalse();
        written.ContainsKey("fileBreakpoints").Should().BeFalse();

        var text = File.ReadAllText(path);
        var attribute = text.IndexOf(shorter ? "Attribute VB_Description" : "Attribute VB_Name", StringComparison.Ordinal);
        File.WriteAllText(path, shorter
            ? text.Remove(attribute, text.IndexOf('\n', attribute) - attribute + 1)
            : text.Insert(attribute, "Attribute VB_Description = \"extra header line\"\r\n"));
        Close(opened.Project);
        var reopened = await Open(vbp);
        document = Doc(reopened.Project, name);
        LineOf(document, reopened.Bookmarks.GetBookmarks(document).Single() + 1).Should().Be(statement);
        LineOf(document, reopened.Breakpoints.GetBreakpoints(document).Single()).Should().Be(statement);
    }

    [Fact]
    public async Task VersionThreeJoinsOlderEntriesAndCarriesUnplaceableMarks()
    {
        var vbp = Stage();
        File.WriteAllText(SidecarPath,
            """
            { "version": 4,
              "codeBookmarks": { "Orders": [4], "Absent": [7] },
              "codeBreakpoints": { "Orders": [5], "Absent": [8] },
              "headerBookmarks": { "Absent": [0] }, "headerBreakpoints": { "Absent": [1] },
              "bookmarks": { "Orders": [10] }, "fileBreakpoints": { "Orders": [32] },
              "future": { "keep": true } }
            """);
        var opened = await Open(vbp);
        var orders = Doc(opened.Project, "Orders");
        opened.Bookmarks.GetBookmarks(orders).Should().Equal(29, 30);
        opened.Breakpoints.GetBreakpoints(orders).Should().Equal(30, 32);
        var written = ReadJson(SidecarPath);
        written["version"]!.GetValue<int>().Should().Be(4);
        written["future"]!["keep"]!.GetValue<bool>().Should().BeTrue();
        written.ContainsKey("bookmarks").Should().BeFalse();
        written.ContainsKey("fileBreakpoints").Should().BeFalse();
        Lines(written, "codeBookmarks", "Absent").Should().Equal(7);
        Lines(written, "codeBreakpoints", "Absent").Should().Equal(8);
        Lines(written, "headerBookmarks", "Absent").Should().Equal(0);
        Lines(written, "headerBreakpoints", "Absent").Should().Equal(1);
    }

    [Fact]
    public async Task VersionThreeValidatesOffsetsAndKeepsOnlyExplicitHeaderMarksOnReadOnlyLines()
    {
        var vbp = Stage();
        File.WriteAllText(SidecarPath,
            """
            { "version": 3,
              "codeBookmarks": { "Utils": [-1, 0, 3, 4, 99, 2147483647] },
              "codeBreakpoints": { "Utils": [0, 1, 4, 5, 99, 2147483647] },
              "headerBookmarks": { "Utils": [-1, 0, 4, 99] },
              "headerBreakpoints": { "Utils": [0, 1, 5, 99] } }
            """);
        var opened = await Open(vbp);
        var utils = Doc(opened.Project, "Utils");
        opened.Bookmarks.GetBookmarks(utils).Should().Equal(0, 1, 5);
        opened.Breakpoints.GetBreakpoints(utils).Should().Equal(1, 2, 6);
        await opened.Sidecar.SaveAsync(opened.Project);
        var written = ReadJson(SidecarPath);
        Lines(written, "codeBookmarks", "Utils").Should().Equal(0, 4);
        Lines(written, "codeBreakpoints", "Utils").Should().Equal(1, 5);
        Lines(written, "headerBookmarks", "Utils").Should().Equal(0);
        Lines(written, "headerBreakpoints", "Utils").Should().Equal(1);
    }

    [Fact]
    public async Task VersionTwoMigrationRetriesItsWriteWithoutKeepingConsumedFileEntries()
    {
        var vbp = Stage();
        const string original = """{ "version": 2, "fileBookmarks": { "Orders": [29] }, "fileBreakpoints": { "Orders": [30] } }""";
        File.WriteAllText(SidecarPath, original);
        var previousOpen = AtomicFileWriter.OpenStream.Value;
        AtomicFileWriter.OpenStream.Value = _ => throw new IOException("Injected sidecar write refusal");
        Opened opened;
        try { opened = await Open(vbp); }
        finally { AtomicFileWriter.OpenStream.Value = previousOpen; }
        File.ReadAllText(SidecarPath).Should().Be(original);

        await opened.Sidecar.SaveAsync(opened.Project);
        var written = ReadJson(SidecarPath);
        Lines(written, "codeBookmarks", "Orders").Should().Equal(4);
        Lines(written, "codeBreakpoints", "Orders").Should().Equal(5);
        written.ContainsKey("fileBookmarks").Should().BeFalse();
        written.ContainsKey("fileBreakpoints").Should().BeFalse();
    }

    // ── The user-sidecar delta's scenarios ───────────────────────────────────────────────────────────────

    /// <summary>
    /// GIVEN a sidecar with a breakpoint on the third line of a class's code, recorded before this change; WHEN the
    /// project is opened; THEN the breakpoint is shown on that same statement, now numbered from the top of the file.
    /// </summary>
    [Fact]
    public async Task OpeningAProjectWithAnEarlierSidecar()
    {
        var vbp = Stage();
        File.WriteAllText(SidecarPath, """{ "version": 1, "breakpoints": { "Ledger": [3] } }""");

        var opened = await Open(vbp);

        var ledger = Doc(opened.Project, "Ledger");
        var header = ModuleFileFormat.SplitHeader(LedgerCls, ModuleKind.ClassModule).Header;
        var headerLines = header.Count(c => c == '\n');
        headerLines.Should().Be(13, "the fixture carries the standard class header");
        opened.Breakpoints.GetBreakpoints(ledger).Should().Equal(headerLines + 3);
        LineOf(ledger, headerLines + 3).Should().Be("Public Sub Post(ByVal amount As Currency)",
            "the class's third line of code, which is where the breakpoint was set");
    }

    /// <summary>
    /// GIVEN a sidecar entry for a module whose file cannot be found; WHEN the project is opened and later saved;
    /// THEN the entry is still in the sidecar, unchanged.
    /// </summary>
    [Theory]
    [InlineData("Module=Gone; Gone.bas")]
    [InlineData("Class=Gone; Gone.cls")]
    [InlineData("UserControl=Gone.ctl")]
    [InlineData("PropertyPage=Gone.pag")]
    public async Task AnEntryForAFileThatIsMissing(string vbpLine)
    {
        var vbp = Stage(vbpLine);
        const string entries = """
            {
              "version": 1,
              "breakpoints": { "Gone": [1, 3, 40] },
              "bookmarks": { "Gone": [0, 2] },
              "fileBreakpoints": { "Gone": [2, 5] }
            }
            """;
        File.WriteAllText(SidecarPath, entries);

        var opened = await Open(vbp);
        DocumentLookup.DocumentsOf(opened.Project).Should().NotContain(d => d.Name == "Gone");
        opened.Project.PreservedItemLines.Should().Contain(vbpLine);

        // Saved, with a mark set elsewhere so the sidecar really is rewritten.
        opened.Breakpoints.SetDocument(Doc(opened.Project, "Utils"), [6]);
        await opened.Sidecar.SaveAsync(opened.Project);

        var written = ReadJson(SidecarPath);
        var original = JsonNode.Parse(entries)!.AsObject();
        JsonNode.DeepEquals(written["breakpoints"], original["breakpoints"]).Should().BeTrue();
        JsonNode.DeepEquals(written["bookmarks"], original["bookmarks"]).Should().BeTrue();
        Lines(written, "fileBreakpoints", "Gone").Should().Equal(2, 5);
        Lines(written, "codeBreakpoints", "Utils").Should().Equal(5);
    }

    /// <summary>
    /// The entry for a file that was missing is migrated by a later opening that finds the file, as the ADDED
    /// requirement says an unmeasurable entry SHALL be "once it can be".
    /// </summary>
    /// <remarks>
    /// The first session also rewrites the sidecar, with a mark set elsewhere, so the later opening reads a file
    /// this build wrote, holding the carried entry under the old keys beside its own under the new ones. That is
    /// the file a developer actually has when a missing module comes back.
    /// </remarks>
    [Fact]
    public async Task AnEntryForAFileThatIsMissingIsMigratedOnceTheFileIsFound()
    {
        var vbp = Stage("Module=Gone; Gone.bas");
        var goneBas = Crlf(
            "Attribute VB_Name = \"Gone\"",
            "Option Explicit",
            "",
            "Public Sub Go()",
            "    Debug.Print 1",
            "End Sub");
        var oldLine = OldLineOf(goneBas, ModuleKind.StandardModule, "    Debug.Print 1");
        oldLine.Should().BeGreaterThan(0, "the statement is in the fixture's code");
        File.WriteAllText(SidecarPath, $$"""{ "version": 1, "breakpoints": { "Gone": [{{oldLine}}] } }""");

        var first = await Open(vbp);
        DocumentLookup.DocumentsOf(first.Project).Should().NotContain(d => d.Name == "Gone");
        first.Breakpoints.SetDocument(Doc(first.Project, "Utils"), [6]);
        await first.Sidecar.SaveAsync(first.Project);
        Close(first.Project);

        var between = ReadJson(SidecarPath);
        between["version"]!.GetValue<int>().Should().Be(3, "this build rewrote the file");
        Lines(between, "breakpoints", "Gone").Should().Equal([oldLine], "the entry was carried as it was");

        File.WriteAllText(Path.Join(dir, "Gone.bas"), goneBas);
        var second = await Open(vbp);

        var gone = Doc(second.Project, "Gone");
        var breakpoint = second.Breakpoints.GetBreakpoints(gone).Should().ContainSingle().Subject;
        LineOf(gone, breakpoint).Should().Be("    Debug.Print 1", "the mark is on the statement it was set on");
        var written = ReadJson(SidecarPath);
        Lines(written, "codeBreakpoints", "Gone").Should().Equal(breakpoint - CodeWindowText.Of(gone).HeaderLineCount);
        Lines(written, "codeBreakpoints", "Utils").Should().Equal(5);
        written.ContainsKey("breakpoints").Should().BeFalse("the entry left the old keys once it was migrated");
    }

    /// <summary>
    /// Where a document whose file is missing shares its name with one that is present, as a project written
    /// elsewhere can, the entry under that name is carried for both rather than placed in either: it is one entry,
    /// and half of it cannot be measured.
    /// </summary>
    /// <remarks>
    /// The form is the document that could take the marks, so it is the one that shows a regression. Placed, the
    /// entry would also be consumed, and the next opening could never migrate it into the module once its
    /// <c>.bas</c> is restored. Both formats are here, since the old keys are migrated and the new ones gathered.
    /// </remarks>
    [Theory]
    [InlineData("Module=Orders; Missing.bas")]
    [InlineData("Class=Orders; Missing.cls")]
    [InlineData("UserControl=Orders.ctl")]
    [InlineData("PropertyPage=Orders.pag")]
    public async Task ANameSharedWithADocumentWhoseFileIsMissingIsCarried(string line)
    {
        var vbp = Stage(line);
        var formOld = OldLineOf(OrdersFrm, null, "    total = 12.5 * 3");
        var text = $$"""
            {
              "version": 1,
              "breakpoints": { "Orders": [{{formOld}}] },
              "fileBookmarks": { "Orders": [29] }
            }
            """;
        File.WriteAllText(SidecarPath, text);

        var opened = await Open(vbp);

        var named = DocumentLookup.DocumentsOf(opened.Project).Where(d => d.Name == "Orders").ToList();
        named.Should().ContainSingle().Which.Form.Should().NotBeNull();
        opened.Project.PreservedItemLines.Should().Contain(line);
        foreach (var document in named)
        {
            opened.Breakpoints.GetBreakpoints(document).Should().BeEmpty($"{document.Display} cannot take half an entry");
            opened.Bookmarks.GetBookmarks(document).Should().BeEmpty();
        }
        File.ReadAllText(SidecarPath).Should().Be(text, "nothing was placed, so nothing was migrated or written");
    }

    /// <summary>
    /// A mark set this session in a document whose entry is being carried is written beside that entry, not over
    /// it: the next opening that can measure the entry joins the two.
    /// </summary>
    /// <remarks>
    /// The document's file disappears after loading but before the sidecar is read, so its entry under the new
    /// keys is carried from the file, while its loaded code window can still take a breakpoint. Writing the stores' marks
    /// alone under that name would delete the carried one for good.
    /// </remarks>
    [Fact]
    public async Task AMarkSetWhereAnEntryIsCarriedIsWrittenBesideIt()
    {
        var vbp = Stage("Module=Gone; Gone.bas");
        var gonePath = Path.Join(dir, "Gone.bas");
        File.WriteAllText(gonePath, "Attribute VB_Name = \"Gone\"\r\n");
        beforeSidecar = _ => File.Delete(gonePath);
        File.WriteAllText(SidecarPath, """{ "version": 2, "fileBreakpoints": { "Gone": [5] } }""");

        var opened = await Open(vbp);
        var gone = Doc(opened.Project, "Gone");
        opened.Breakpoints.GetBreakpoints(gone).Should().BeEmpty("its file is missing, so its entry is carried");

        opened.Breakpoints.SetDocument(gone, [2]);
        await opened.Sidecar.SaveAsync(opened.Project);

        Lines(ReadJson(SidecarPath), "fileBreakpoints", "Gone").Should().Equal(5);
        Lines(ReadJson(SidecarPath), "codeBreakpoints", "Gone").Should().Equal(1);
    }

    /// <summary>
    /// WHEN a migrated sidecar is read again; THEN nothing in it moves.
    /// </summary>
    /// <remarks>
    /// Asserted on the file as well as the stores: a reopening that wrote the same marks back would leave the stores
    /// right and still be a migration run twice. The write time is pinned to a date nothing here produces, so a
    /// rewrite with identical bytes shows too. An entry that cannot be placed is in the file throughout, because
    /// it is the one part a careless second pass could move.
    /// </remarks>
    [Fact]
    public async Task OpeningAnAlreadyMigratedSidecarAgain()
    {
        var vbp = Stage();
        File.WriteAllText(SidecarPath, """
            {
              "version": 1,
              "breakpoints": { "vb6://form/Orders": [10], "Utils": [5], "Ledger": [4], "Gauge": [6], "Absent": [3] },
              "bookmarks": { "Utils": [4] }
            }
            """);
        var first = await Open(vbp);
        var marksAfterFirst = new[] { "Orders", "Utils", "Ledger", "Gauge" }
            .ToDictionary(n => n, n => first.Breakpoints.GetBreakpoints(Doc(first.Project, n)).ToArray());
        var bookmarkAfterFirst = first.Bookmarks.GetBookmarks(Doc(first.Project, "Utils")).ToArray();
        Close(first.Project);

        // The second opening reads a migrated file. Without this, an opening that never wrote its migration would
        // pass too: reading the same earlier sidecar twice gives the same marks and writes nothing either time.
        var afterFirst = ReadJson(SidecarPath);
        afterFirst["version"]!.GetValue<int>().Should().Be(3, "the first opening migrated the file");
        afterFirst["codeBreakpoints"]!.AsObject().Select(p => p.Key).Should()
            .BeEquivalentTo(["Orders", "Utils", "Ledger", "Gauge"], "every entry that could be placed was moved");
        afterFirst["breakpoints"]!.AsObject().Select(p => p.Key).Should().Equal("Absent");

        var migrated = File.ReadAllBytes(SidecarPath);
        var stamp = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(SidecarPath, stamp);

        var second = await Open(vbp);

        foreach (var (name, lines) in marksAfterFirst)
            second.Breakpoints.GetBreakpoints(Doc(second.Project, name)).Should().Equal(lines, $"{name} does not move again");
        second.Bookmarks.GetBookmarks(Doc(second.Project, "Utils")).Should().Equal(bookmarkAfterFirst);
        File.ReadAllBytes(SidecarPath).Should().Equal(migrated);
        File.GetLastWriteTimeUtc(SidecarPath).Should().Be(stamp, "opening it again does not rewrite it");

        // And the one that could not be placed is still where it was, in the old keys.
        Lines(ReadJson(SidecarPath), "breakpoints", "Absent").Should().Equal(3);
    }

    // ── Every kind keeps its marks on their statements ─────────────────────────────────────────────────────

    /// <summary>
    /// A breakpoint and a bookmark set on one statement in the old numbering are on that statement afterwards, for
    /// a form, a module, a class and a UserControl, and are written under the new keys by the document's own name.
    /// </summary>
    /// <remarks>
    /// The four move by different amounts, which is the point of covering all four. A class moves by its whole
    /// header; a form and a UserControl by their designer block alone, because the old window already began at their
    /// <c>Attribute</c> lines — moving them by the whole header would put the mark five lines too low. The form is
    /// keyed by its old <c>vb6://</c> spelling, so the re-key is exercised as well.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Documents))]
    public async Task AMigratedSidecarKeepsEachMarkOnItsStatement(string name)
    {
        var (file, statement) = Marked[name];
        var kind = name switch
        {
            "Utils" => ModuleKind.StandardModule,
            "Ledger" => ModuleKind.ClassModule,
            _ => (ModuleKind?)null,
        };
        var oldLine = OldLineOf(FixtureText(file), kind, statement);
        oldLine.Should().BeGreaterThan(0, "the statement is in the fixture's code");
        var key = name == "Orders" ? "vb6://form/Orders" : name;
        var vbp = Stage();
        File.WriteAllText(SidecarPath,
            $$"""{ "version": 1, "breakpoints": { "{{key}}": [{{oldLine}}] }, "bookmarks": { "{{key}}": [{{oldLine - 1}}] } }""");

        var opened = await Open(vbp);

        var document = Doc(opened.Project, name);
        var breakpoint = opened.Breakpoints.GetBreakpoints(document).Should().ContainSingle().Subject;
        LineOf(document, breakpoint).Should().Be(statement);
        opened.Bookmarks.GetBookmarks(document).Should().Equal([breakpoint - 1],
            "a bookmark counts from 0 and a breakpoint from 1, and the two were on one statement");

        var written = ReadJson(SidecarPath);
        Lines(written, "codeBreakpoints", name).Should().Equal(breakpoint - CodeWindowText.Of(document).HeaderLineCount);
        Lines(written, "codeBookmarks", name).Should().Equal(breakpoint - 1 - CodeWindowText.Of(document).HeaderLineCount);
        written.ContainsKey("breakpoints").Should().BeFalse("every entry was placed, so none is left in the old keys");
        written.ContainsKey("bookmarks").Should().BeFalse();
        written["version"]!.GetValue<int>().Should().Be(3);
    }

    private static string FixtureText(string file) => file switch
    {
        "Orders.frm" => OrdersFrm,
        "Utils.bas" => UtilsBas,
        "Ledger.cls" => LedgerCls,
        _ => GaugeCtl,
    };

    // ── What the migration drops, keeps and writes ─────────────────────────────────────────────────────────

    /// <summary>
    /// WHEN a project is opened whose sidecar, written before lines counted from the top of the file, holds a
    /// breakpoint on a form's <c>Attribute VB_Name</c> line, which the code window used to show as its first line;
    /// THEN the form has no breakpoint there, the log says it was dropped, and the next save does not write it.
    /// </summary>
    /// <remarks>
    /// The old window showed a form's leading <c>Attribute</c> lines and a member's <c>Attribute</c> line as
    /// editable code, so a mark could be set there. Moved to the top of the file, the first is in the header and
    /// the second describes a procedure; neither ever runs. The next save is the one the migration makes as the
    /// project opens. An entry every line of which is dropped has still been migrated, so it leaves the old keys
    /// with the rest: left there, it would be dropped again at every opening and never leave the file.
    /// </remarks>
    [Fact]
    public async Task AnEarlierSidecarsMarkThatLandsOnAReadOnlyLine()
    {
        var vbp = Stage();
        var formOld = OldLineOf(OrdersFrm, null, "    total = 12.5 * 3");
        var memberAttributeOld = OldLineOf(UtilsBas, ModuleKind.StandardModule,
            "Attribute Twice.VB_Description = \"Doubles a number\"");
        File.WriteAllText(SidecarPath, $$"""
            {
              "version": 1,
              "breakpoints": { "Orders": [1, {{formOld}}], "Utils": [{{memberAttributeOld}}] },
              "bookmarks": { "Orders": [4] }
            }
            """);
        using var log = new CapturedLog();

        var opened = await Open(vbp);

        var orders = Doc(opened.Project, "Orders");
        var breakpoint = opened.Breakpoints.GetBreakpoints(orders).Should().ContainSingle().Subject;
        LineOf(orders, breakpoint).Should().Be("    total = 12.5 * 3", "the mark on code is kept");
        opened.Bookmarks.GetBookmarks(orders).Should().BeEmpty("its bookmark was on Attribute VB_Exposed");
        opened.Breakpoints.GetBreakpoints(Doc(opened.Project, "Utils")).Should().BeEmpty();

        var readOnlyDrops = log.Messages.Where(m => m.Contains("read-only lines")).ToList();
        readOnlyDrops.Should().HaveCount(3, "one breakpoint and one bookmark on the form, one breakpoint on the module");
        readOnlyDrops.Should().Contain(m => m.Contains("Shop/Orders") && m.Contains("breakpoint"));
        readOnlyDrops.Should().Contain(m => m.Contains("Shop/Orders") && m.Contains("bookmark"));
        readOnlyDrops.Should().Contain(m => m.Contains("Shop/Utils"));

        var written = ReadJson(SidecarPath);
        Lines(written, "codeBreakpoints", "Orders").Should().Equal(breakpoint - 25);
        written["codeBreakpoints"]!.AsObject().ContainsKey("Utils").Should().BeFalse();
        written.ContainsKey("codeBookmarks").Should().BeFalse();
        written.ContainsKey("breakpoints").Should().BeFalse(
            "Utils' entry was migrated although its only line was dropped, so it is not left in the old keys");
        written.ContainsKey("bookmarks").Should().BeFalse("nor is the form's bookmark, every line of which was dropped");
    }

    /// <summary>
    /// The user-sidecar scenario "A sidecar naming lines past the end of its document", for an earlier sidecar: an
    /// entry whose every line is past the end is migrated all the same, so the file is rewritten without it as the
    /// project opens, though no mark moved.
    /// </summary>
    /// <remarks>
    /// Nothing else in the file is migrated, so this entry alone is what makes the write. Left under the old keys
    /// it would be dropped and logged at every opening and never written out of the file, which is what the
    /// requirement's "the next save does not write it" rules out.
    /// </remarks>
    [Fact]
    public async Task AnEarlierSidecarsEntryWhoseEveryLineIsPastTheEndLeavesTheFile()
    {
        var vbp = Stage();
        File.WriteAllText(SidecarPath, """{ "version": 1, "breakpoints": { "Utils": [99] } }""");
        using var log = new CapturedLog();

        var opened = await Open(vbp);

        opened.Breakpoints.GetBreakpoints(Doc(opened.Project, "Utils")).Should().BeEmpty();
        log.Messages.Should().Contain(m => m.Contains("Dropped breakpoints on") && m.Contains("99"));
        var written = ReadJson(SidecarPath);
        written["version"]!.GetValue<int>().Should().Be(3, "the file was rewritten as the project opened");
        written.ContainsKey("breakpoints").Should().BeFalse();
        written.ContainsKey("codeBreakpoints").Should().BeFalse("nothing was left to place");
    }

    /// <summary>Version 2 header marks stay file-relative; member Attribute marks are still dropped.</summary>
    [Fact]
    public async Task MigratingFileMarksKeepsHeaderLinesAndDropsMemberAttributes()
    {
        var vbp = Stage();
        const string text = """{ "version": 2, "fileBreakpoints": { "Ledger": [5, 16] }, "fileBookmarks": { "Utils": [4, 5] } }""";
        File.WriteAllText(SidecarPath, text);
        using var log = new CapturedLog();

        var opened = await Open(vbp);

        var ledger = Doc(opened.Project, "Ledger");
        var utils = Doc(opened.Project, "Utils");
        LineOf(ledger, 5).Should().Be("  DataBindingBehavior = 0  'vbNone", "line 5 is in the class's header");
        opened.Breakpoints.GetBreakpoints(ledger).Should().Equal(5, 16);
        opened.Bookmarks.GetBookmarks(utils).Should().Equal(5);
        log.Messages.Where(m => m.Contains("read-only lines")).Should().ContainSingle()
            .Which.Should().Contain("Shop/Utils").And.Contain("bookmark");

        var written = ReadJson(SidecarPath);
        Lines(written, "headerBreakpoints", "Ledger").Should().Equal(5);
        Lines(written, "codeBreakpoints", "Ledger").Should().Equal(3);
        Lines(written, "codeBookmarks", "Utils").Should().Equal(4);
    }

    /// <summary>
    /// A module whose header the reader did not recognise (hexide-io/HexIDE#472) keeps its whole file in its code,
    /// so the old window already showed the whole file and nothing moves.
    /// </summary>
    /// <remarks>
    /// This is what tells the prefix apart from the header region. The code window still finds a header in such
    /// a file, from its text, so a shift taken from the header region would move the mark two lines down.
    /// </remarks>
    [Fact]
    public async Task AModuleWhoseHeaderWasNotRecognisedDoesNotMove()
    {
        // A comment above the attribute run: a blank line there no longer hides the header (#472).
        File.WriteAllText(Path.Join(dir, "Odd.bas"), Crlf(
            "' generated",
            "Attribute VB_Name = \"Odd\"",
            "Option Explicit",
            "",
            "Public Sub Go()",
            "    Debug.Print 1",
            "End Sub"));
        var vbp = Stage("Module=Odd; Odd.bas");
        File.WriteAllText(SidecarPath, """{ "version": 1, "breakpoints": { "Odd": [6] } }""");

        var opened = await Open(vbp);

        var odd = Doc(opened.Project, "Odd");
        odd.Module!.OriginalHeader.Should().BeEmpty("the reader split nothing off");
        opened.Breakpoints.GetBreakpoints(odd).Should().Equal(6);
        LineOf(odd, 6).Should().Be("    Debug.Print 1");
    }

    /// <summary>
    /// GIVEN a migrated sidecar into which an earlier version has since written marks of its own; WHEN the project
    /// is opened; THEN the marks of both versions are shown, each on the statement it was set on.
    /// </summary>
    /// <remarks>
    /// The file is of this build's version and holds version-1 entries beside its own, as a build with the #466 fix
    /// and without the migration writes it. Both name one document, because a store's setter replaces: setting the
    /// two sets in turn would keep only the second, and a test with the two on different documents could not see it.
    /// </remarks>
    [Fact]
    public async Task EarlierMarksWrittenIntoAMigratedSidecar()
    {
        var vbp = Stage();
        var ledgerOld = OldLineOf(LedgerCls, ModuleKind.ClassModule, "    mBalance = mBalance + amount");
        File.WriteAllText(SidecarPath, $$"""
            {
              "version": 2,
              "fileBreakpoints": { "Ledger": [16] },
              "breakpoints": { "Ledger": [{{ledgerOld}}] }
            }
            """);

        var opened = await Open(vbp);

        var ledger = Doc(opened.Project, "Ledger");
        var lines = opened.Breakpoints.GetBreakpoints(ledger);
        lines.Should().HaveCount(2, "both sets reach the one document");
        LineOf(ledger, lines[0]).Should().Be("Public Sub Post(ByVal amount As Currency)", "the version-2 line, unmoved");
        LineOf(ledger, lines[1]).Should().Be("    mBalance = mBalance + amount", "the version-1 line, moved");

        var written = ReadJson(SidecarPath);
        Lines(written, "codeBreakpoints", "Ledger").Should().Equal(lines.Select(line => line - 13));
        written.ContainsKey("breakpoints").Should().BeFalse();
    }

    /// <summary>
    /// The user-sidecar scenario "A sidecar deleted while the project is open", with entries this build keeps
    /// without placing: they are taken from the file each time it is written, never from memory, so a sidecar the
    /// developer deleted does not come back holding them.
    /// </summary>
    [Fact]
    public async Task ADeletedSidecarStaysDeletedOfItsOldEntries()
    {
        var vbp = Stage();
        File.WriteAllText(SidecarPath, """
            {
              "version": 1,
              "breakpoints": { "Absent": [3], "Utils": [5] },
              "fileBookmarks": { "Elsewhere": [7] }
            }
            """);
        var opened = await Open(vbp);
        Lines(ReadJson(SidecarPath), "breakpoints", "Absent").Should().Equal([3], "it was carried through the migration");

        File.Delete(SidecarPath);
        opened.Bookmarks.SetBookmarks(Doc(opened.Project, "Ledger"), [15]);
        await opened.Sidecar.SaveAsync(opened.Project);

        var written = ReadJson(SidecarPath);
        written.Select(p => p.Key).Should().BeEquivalentTo("version", "codeBookmarks", "codeBreakpoints");
        written["codeBookmarks"]!.AsObject().Select(p => p.Key).Should().Equal("Ledger");
        written["codeBreakpoints"]!.AsObject().Select(p => p.Key).Should().Equal("Utils");
    }

    /// <summary>
    /// A version-1 file with nothing the migration can place is left exactly as it is: nothing moved, so nothing is
    /// written.
    /// </summary>
    [Fact]
    public async Task AnEarlierSidecarWithNothingToPlaceIsNotRewritten()
    {
        var vbp = Stage();
        const string text = """{ "version": 1, "breakpoints": { "Absent": [3] }, "watches": ["x"] }""";
        File.WriteAllText(SidecarPath, text);
        var stamp = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(SidecarPath, stamp);

        await Open(vbp);

        File.ReadAllText(SidecarPath).Should().Be(text);
        File.GetLastWriteTimeUtc(SidecarPath).Should().Be(stamp);
    }

    /// <summary>
    /// When the write that records a migration fails, the entries it moved are not written back in the old keys by
    /// a later save: they would sit beside the marks they became and be migrated a second time on the next opening.
    /// </summary>
    /// <remarks>
    /// Every save takes the old keys from the file as it is then, and until the migration's write lands the file
    /// still holds what was moved. The atomic writer's stream factory refuses the migration's write.
    /// </remarks>
    [Fact]
    public async Task AMigrationWhoseWriteFailsIsNotWrittenBackInTheOldNumberingLater()
    {
        var vbp = Stage();
        File.WriteAllText(SidecarPath, """{ "version": 1, "breakpoints": { "Ledger": [3], "Absent": [9] } }""");
        var previousOpen = AtomicFileWriter.OpenStream.Value;
        AtomicFileWriter.OpenStream.Value = _ => throw new IOException("Injected sidecar write refusal");

        Opened opened;
        try { opened = await Open(vbp); }
        finally { AtomicFileWriter.OpenStream.Value = previousOpen; }
        ReadJson(SidecarPath)["version"]!.GetValue<int>().Should().Be(1, "the migration's write could not be made");

        Close(opened.Project);

        var written = ReadJson(SidecarPath);
        Lines(written, "codeBreakpoints", "Ledger").Should().Equal(3);
        written["breakpoints"]!.AsObject().Select(p => p.Key).Should().Equal(["Absent"],
            "the entry that was migrated is gone from the old keys, and the one that was not is still there");
    }

    /// <summary>
    /// While the migration's write is still to land, an earlier build that rewrites a migrated entry under the old
    /// keys keeps what it wrote: only an entry that is still exactly what the load moved is left out of the file,
    /// and the next opening migrates the rest.
    /// </summary>
    /// <remarks>
    /// The file stays in the old format while another program holds it, so a build without the migration, with the
    /// #466 fix, can open the project meanwhile and add a breakpoint under the key this one migrated. Subtracting
    /// the key whatever it holds would delete that breakpoint at the next save, unseen.
    /// </remarks>
    [Fact]
    public async Task AnEarlierBuildsLaterWriteUnderAMigratedKeyIsKeptForTheNextOpening()
    {
        var vbp = Stage();
        File.WriteAllText(SidecarPath, """{ "version": 1, "breakpoints": { "Ledger": [3] } }""");
        var previousOpen = AtomicFileWriter.OpenStream.Value;
        AtomicFileWriter.OpenStream.Value = _ => throw new IOException("Injected sidecar write refusal");

        Opened opened;
        try { opened = await Open(vbp); }
        finally { AtomicFileWriter.OpenStream.Value = previousOpen; }
        ReadJson(SidecarPath)["version"]!.GetValue<int>().Should().Be(1, "the migration's write could not be made");

        // The earlier build's save: its own marks, in its own numbering, under the key this one migrated.
        File.WriteAllText(SidecarPath, """{ "version": 1, "breakpoints": { "Ledger": [3, 4] } }""");
        await opened.Sidecar.SaveAsync(opened.Project);

        var written = ReadJson(SidecarPath);
        Lines(written, "codeBreakpoints", "Ledger").Should().Equal(3);
        written.ContainsKey("breakpoints").Should().BeTrue("the entry is no longer what the load moved");
        Lines(written, "breakpoints", "Ledger").Should().Equal([3, 4], "it is kept as the earlier build wrote it");
        Close(opened.Project);

        var again = await Open(vbp);
        var ledger = Doc(again.Project, "Ledger");
        again.Breakpoints.GetBreakpoints(ledger).Should().Equal(16, 17);
        LineOf(ledger, 17).Should().Be("    mBalance = mBalance + amount", "the earlier build's mark, on its statement");
    }

    /// <summary>
    /// A file from a newer build keeps its version, and the version-1 keys in it are migrated all the same: those
    /// keys are counted the old way whatever the file's version says.
    /// </summary>
    [Fact]
    public async Task AFileFromANewerBuildHasItsOldKeysMigratedAndKeepsItsVersion()
    {
        var vbp = Stage();
        File.WriteAllText(SidecarPath, """{ "version": 3, "breakpoints": { "Ledger": [3] }, "marks": { "x": 1 } }""");

        var opened = await Open(vbp);

        opened.Breakpoints.GetBreakpoints(Doc(opened.Project, "Ledger")).Should().Equal(16);
        var written = ReadJson(SidecarPath);
        written["version"]!.GetValue<int>().Should().Be(3);
        JsonNode.DeepEquals(written["marks"], JsonNode.Parse("""{ "x": 1 }""")).Should().BeTrue();
    }

    // ── Which entries a session owns ───────────────────────────────────────────────────────────────────────

    private static ProjectDefinition InMemoryProject(string directory)
    {
        Directory.CreateDirectory(directory);
        var project = new ProjectDefinition(VBProjectType.EXE, "P") { AbsolutePath = Path.Join(directory, "P.vbp") };
        foreach (var name in new[] { "Utils", "Tools" })
        {
            var module = new ModuleDefinition(project, name, ModuleKind.StandardModule);
            module.UpdateCode(string.Join("\r\n", Enumerable.Range(1, 12).Select(i => $"' line {i}")));
            project.AddModule(module);
        }
        return project;
    }

    private static string SidecarOf(ProjectDefinition project) => Path.ChangeExtension(project.AbsolutePath!, null) + ".user.hexproj";

    /// <summary>
    /// An entry for a name the project did not have when it opened is carried, and goes with the project when it
    /// is saved elsewhere, in both formats.
    /// </summary>
    [Fact]
    public async Task AnEntryForANameTheProjectDidNotHaveGoesWithItWhenSavedElsewhere()
    {
        var project = InMemoryProject(Path.Join(dir, "here"));
        File.WriteAllText(SidecarOf(project),
            """{ "version": 2, "fileBreakpoints": { "Absent": [4], "Utils": [3] }, "breakpoints": { "Gone": [2] } }""");
        var breakpoints = new BreakpointService();
        var sidecar = new UserSidecarService(new BookmarkService(), breakpoints, projectManager);
        await sidecar.LoadAsync(project);

        project.AbsolutePath = Path.Join(dir, "there", "P.vbp");
        Directory.CreateDirectory(Path.Join(dir, "there"));
        await sidecar.SaveAsync(project);

        var written = ReadJson(SidecarOf(project));
        Lines(written, "fileBreakpoints", "Absent").Should().Equal(4);
        Lines(written, "codeBreakpoints", "Utils").Should().Equal(2);
        Lines(written, "breakpoints", "Gone").Should().Equal(2);
    }

    /// <summary>
    /// Two of the ways the obvious carry rule — keep every entry naming no current document — fails, each pinned: a
    /// renamed document's old entry is not kept beside its new one, and a mark removed while it was renamed does not
    /// come back with the name.
    /// </summary>
    /// <remarks>
    /// The third way, a later document of the old name inheriting the renamed one's marks, is not pinned apart from
    /// the first, because it cannot happen apart from it. The names a session carries are fixed as the project
    /// opens, so a name it owned is never carried, and the only way this session can leave an entry under the old
    /// name for a newcomer to inherit is the first failure: the renamed document's old entry kept.
    /// </remarks>
    [Fact]
    public async Task OnlyNamesTheProjectLackedWhenItOpenedAreCarried()
    {
        var project = InMemoryProject(Path.Join(dir, "rename"));
        File.WriteAllText(SidecarOf(project), """{ "version": 2, "fileBreakpoints": { "Utils": [3] } }""");
        var breakpoints = new BreakpointService();
        var sidecar = new UserSidecarService(new BookmarkService(), breakpoints, projectManager);
        await sidecar.LoadAsync(project);
        var utils = DocumentIdentity.For(project.Modules[0]);

        utils.Module!.Name = "Helpers";
        await sidecar.SaveAsync(project);
        var renamed = ReadJson(SidecarOf(project));
        renamed["codeBreakpoints"]!.AsObject().Select(p => p.Key).Should().Equal(["Helpers"],
            "the old name's entry is not kept beside the new one");

        breakpoints.SetDocument(utils, []);
        utils.Module!.Name = "Utils";
        await sidecar.SaveAsync(project);
        ReadJson(SidecarOf(project)).ContainsKey("codeBreakpoints").Should().BeFalse(
            "the mark removed while it was renamed does not come back with the name");
    }
}
