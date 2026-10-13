using System.IO;
using System.Reflection;
using Avalonia.Threading;
using HexIDE.Addins;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Addins;

/// <summary>
/// What an add-in reads is the whole file, every position it gives counts from the top of that file, and a call
/// that is refused says why (hexide-io/HexIDE#273 task 3.19, the addin-system delta).
/// </summary>
/// <remarks>
/// Through <see cref="AddinEditorService"/>, which is the add-in's own surface, against real code-window view
/// models. The guard's rules are proved on the view model in <c>GuardedWritersTests</c>; this is what an add-in
/// author sees of them.
/// </remarks>
public class AddinWholeFileTests : IDisposable
{
    private readonly IDocumentDockService dock = Substitute.For<IDocumentDockService>();
    private readonly IProjectManager projects = Substitute.For<IProjectManager>();
    private readonly List<ProjectDefinition> loaded = [];
    private readonly List<CodeEditorViewModel> open = [];
    private readonly List<CodeEditorViewModel> made = [];
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-addin-" + Guid.NewGuid().ToString("N"));
    private readonly AddinEditorService sut;

    /// <summary>The window in front. The dock brings a window forward as the real one does, unless told it cannot.</summary>
    private CodeEditorViewModel? active;
    private bool canBringForward = true;

    public AddinWholeFileTests()
    {
        projects.LoadedProjects.Returns(_ => loaded);
        dock.OpenDocuments.Returns(_ => open.Cast<BaseEditorWindowViewModel>().ToList());
        dock.ActiveDocument.Returns(_ => active);
        dock.TryActivate(Arg.Any<Func<CodeEditorViewModel, bool>>()).Returns(call =>
        {
            if (!canBringForward || open.FirstOrDefault(call.Arg<Func<CodeEditorViewModel, bool>>()) is not { } match)
                return false;
            active = match;
            return true;
        });
        sut = new AddinEditorService(dock, Substitute.For<IEditorService>(), projects);
    }

    public void Dispose()
    {
        foreach (var vm in made) vm.Dispose();
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    // Lines 1-7 the designer block, 8-9 the attribute run: the header is 9 lines, and the code starts at 10.
    private const string Frm =
        "VERSION 5.00\r\n" +
        "Begin VB.Form frmOrders \r\n" +
        "   Caption         =   \"Orders\"\r\n" +
        "   Begin VB.CommandButton Command1 \r\n" +
        "      Caption         =   \"OK\"\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"frmOrders\"\r\n" +
        "Attribute VB_PredeclaredId = True\r\n" +
        "Option Explicit\r\n" +
        "\r\n" +
        "Private Sub Command1_Click()\r\n" +
        "    Command1.Enabled = False\r\n" +
        "End Sub\r\n";

    private const string ModuleCode =
        "Option Explicit\r\n" +
        "\r\n" +
        "Public Function Total() As Currency\r\n" +
        "    Total = 0\r\n" +
        "End Function\r\n";

    private CodeEditorViewModel NewEditor()
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), eventBus, Substitute.For<ILspClient>(),
            Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(), Substitute.For<IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<IRunScope>(), Substitute.For<ILocalizationService>());
        made.Add(vm);
        return vm;
    }

    /// <summary>A form in a loaded project, open in a code window that is the active one.</summary>
    private CodeEditorViewModel OpenForm(string projectName = "Shop")
    {
        var project = TestHelpers.CreateProject(projectName);
        var form = new FormDeserializer().Deserialize(project, Frm, NullSink.Instance)!;
        project.AddForm(form);
        loaded.Add(project);
        return Show(NewEditor().Initialize(form));
    }

    /// <summary>A module of the given kind in a project, loaded and open in a code window that is the active one.</summary>
    private CodeEditorViewModel OpenModule(string projectName, string name, ModuleKind kind = ModuleKind.StandardModule)
    {
        var project = loaded.FirstOrDefault(p => p.Name == projectName);
        if (project is null)
        {
            project = TestHelpers.CreateProject(projectName);
            loaded.Add(project);
        }
        var module = TestHelpers.CreateModule(project, name, kind);
        module.UpdateCode(ModuleCode);
        project.AddModule(module);
        return Show(NewEditor().Initialize(module));
    }

    private CodeEditorViewModel Show(CodeEditorViewModel editor)
    {
        open.Add(editor);
        active = editor;
        return editor;
    }

    private static int LineOf(string text, string needle) =>
        text[..text.IndexOf(needle, StringComparison.Ordinal)].Split('\n').Length;

    // ── The addin-system delta's scenarios ───────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ReadingAForm()
    {
        var editor = OpenForm();

        var content = sut.GetActiveDocument()!.Content;

        content.Should().Be(Frm, "an add-in reads the form's designer block and code together, as the file holds them");
        content.Should().StartWith("VERSION 5.00\r\nBegin VB.Form frmOrders");
        content.Should().Be(editor.Document.Text);
    }

    [AvaloniaFact]
    public void ALineFoundByReadingTheContentIsTheLineTheSurfaceCounts()
    {
        // The reason the content changed: before, a line found by reading it was out by the header's length.
        var editor = OpenForm();
        var content = sut.GetActiveDocument()!.Content;
        var line = LineOf(content, "    Command1.Enabled");

        editor.SelectionStart = editor.Document.GetLineByNumber(line).Offset;

        sut.GetSelection()!.StartLine.Should().Be(line);
        line.Should().Be(13, "the designer block and attribute run are lines 1-9, and the code counts on from them");
    }

    [AvaloniaFact]
    public async Task RewritingAModuleAndKeepingItsHeader()
    {
        var editor = OpenModule("Shop", "Order", ModuleKind.ClassModule);
        var read = sut.GetActiveDocument()!.Content;
        var header = read[..read.IndexOf("Option Explicit", StringComparison.Ordinal)];
        header.Should().StartWith("VERSION 1.0 CLASS", "a class's content includes its header");

        var result = await sut.TrySetContent("Order", read.Replace("Total = 0", "Total = 1"), null);

        result.Succeeded.Should().BeTrue();
        result.Reason.Should().BeNull();
        result.HeaderKept.Should().BeFalse("the whole file was sent, so nothing was put in front of it");
        editor.Document.Text.Should().Be(read.Replace("Total = 0", "Total = 1"));
        editor.Document.Text.Should().StartWith(header);
    }

    [AvaloniaFact]
    public async Task ApplyingABlockOfCodeAlone()
    {
        var editor = OpenForm();
        var header = Frm[..Frm.IndexOf("Option Explicit", StringComparison.Ordinal)];
        const string block = "Option Explicit\r\n\r\nPrivate Sub Form_Load()\r\nEnd Sub\r\n";

        var result = await sut.TrySetContent("frmOrders", block, null);

        result.Succeeded.Should().BeTrue();
        result.HeaderKept.Should().BeTrue("the add-in can tell its block was given the document's header");
        editor.Document.Text.Should().Be(header + block);
    }

    [AvaloniaFact]
    public async Task TwoProjectsWithADocumentOfTheSameName()
    {
        var first = OpenModule("Shop", "Module1");
        var second = OpenModule("Stock", "Module1");
        var line = LineOf(first.Document.Text, "    Total = 0");
        var (firstBefore, secondBefore) = (first.Document.Text, second.Document.Text);

        var content = await sut.TrySetContent("Module1", ModuleCode, null);
        var edits = await sut.TryApplyEdits("Module1", [new AddinTextEdit(line, 13, line, 14, "1")], null);
        var move = await sut.TryNavigateTo("Module1", line, 1, null);

        foreach (var refused in new[] { content, edits, move })
        {
            refused.Succeeded.Should().BeFalse();
            refused.Reason.Should().Contain("more than one document").And.Contain("Shop/Module1").And.Contain("Stock/Module1")
                .And.Contain("project", "the reason says how to name the one meant");
        }
        first.Document.Text.Should().Be(firstBefore);
        second.Document.Text.Should().Be(secondBefore);

        (await sut.SetContent("Module1", ModuleCode, null)).Should().BeFalse("the members that answer true or false refuse it too");
        sut.NavigateTo("Module1", line, 1, null).Should().BeFalse();

        // Naming the project reaches that project's document, and only it, through each of the three members. The
        // first project is named for the move, so a project dropped on the way would land on the other document.
        (await sut.TryApplyEdits("Module1", [new AddinTextEdit(line, 13, line, 14, "1")], "Stock")).Succeeded.Should().BeTrue();
        second.Document.Text.Should().Contain("Total = 1");
        first.Document.Text.Should().Be(firstBefore, "naming the project reaches that project's document only");

        var replaced = ModuleCode.Replace("Total = 0", "Total = 2");
        (await sut.TrySetContent("Module1", replaced, "Stock")).Succeeded.Should().BeTrue();
        second.Document.Text.Should().Contain("Total = 2");
        first.Document.Text.Should().Be(firstBefore);

        (await sut.TryNavigateTo("Module1", line, 5, "Shop")).Succeeded.Should().BeTrue();
        first.Document.GetLocation(first.CaretOffset).Line.Should().Be(line);
        second.Document.GetLocation(second.CaretOffset).Line.Should().NotBe(line);
        dock.ActiveDocument.Should().BeSameAs(first, "the move brings the named project's document to the front");
    }

    [AvaloniaFact]
    public async Task NavigatingToADocumentOpenBehindAnotherBringsItToTheFront()
    {
        // An editor already open was returned where it was, so the caret moved in a tab the developer could not
        // see, and AI Chat told its model it had taken the developer to the line.
        var behind = OpenModule("Shop", "Module1");
        var inFront = OpenModule("Shop", "Module2");
        var line = LineOf(behind.Document.Text, "    Total = 0");

        var result = await sut.TryNavigateTo("Module1", line, 1, null);

        result.Succeeded.Should().BeTrue();
        dock.ActiveDocument.Should().BeSameAs(behind).And.NotBeSameAs(inFront);
        behind.Document.GetLocation(behind.CaretOffset).Line.Should().Be(line);
    }

    [AvaloniaFact]
    public async Task NavigatingToADocumentThatCannotComeToTheFrontSaysSo()
    {
        var behind = OpenModule("Shop", "Module1");
        OpenModule("Shop", "Module2");
        canBringForward = false;

        var result = await sut.TryNavigateTo("Module1", 3, 1, null);

        result.Succeeded.Should().BeFalse("the developer is not looking at the line, so the move did not do what was asked");
        result.Reason.Should().Contain("could not be brought to the front").And.Contain("Shop/Module1");
        behind.Document.GetLocation(behind.CaretOffset).Line.Should().Be(3, "the caret was moved, and the reason says so");
    }

    // ── Refusals reach the add-in, with their reason (task 3.21's extra case) ───────────────────────────────

    [AvaloniaFact]
    public async Task AnAddinReplacementThatChangesTheHeaderIsRefused()
    {
        var editor = OpenForm();

        var result = await sut.TrySetContent("frmOrders", Frm.Replace("\"OK\"", "\"Go\""), null);

        result.Succeeded.Should().BeFalse();
        result.Reason.Should().Contain("header", "the add-in is told why, not only that").And.Contain("only the IDE changes");
        result.HeaderKept.Should().BeFalse();
        editor.Document.Text.Should().Be(Frm, "nothing is written when the replacement is refused");
    }

    [AvaloniaFact]
    public async Task AnEditReachingTheHeaderIsRefusedAndTheReasonNamesItsLineAndColumn()
    {
        var editor = OpenForm();
        var code = LineOf(Frm, "    Command1.Enabled");

        var result = await sut.TryApplyEdits("frmOrders",
            [new AddinTextEdit(code, 5, code, 13, "cmdOk"), new AddinTextEdit(3, 24, 3, 32, "\"Sales\"")], null);

        result.Succeeded.Should().BeFalse();
        result.Reason.Should().Contain("line 3, column 24", "the add-in named the edit by line and column, not by a buffer offset")
            .And.Contain("Edit 2 of 2").And.NotContain("offset");
        editor.Document.Text.Should().Be(Frm, "all or nothing: the edit in the code is not applied either");
    }

    [AvaloniaFact]
    public async Task ARefusedEditIsNamedAsTheAddinSentIt()
    {
        // A column past a line's end means the line's end, so the buffer places this edit at the end of line 3,
        // which the refusal used to name. The add-in sent column 200, as the second of three edits, and that is
        // what it can recognise.
        var editor = OpenForm();
        var code = LineOf(Frm, "    Command1.Enabled");

        var result = await sut.TryApplyEdits("frmOrders",
        [
            new AddinTextEdit(code, 5, code, 13, "cmdOk"),
            new AddinTextEdit(3, 200, 3, 200, " "),
            new AddinTextEdit(code + 1, 1, code + 1, 1, "'"),
        ], null);

        result.Succeeded.Should().BeFalse();
        result.Reason.Should().StartWith("Edit 2 of 3 (line 3, column 200 to line 3, column 200) would change the document's header");
        editor.Document.Text.Should().Be(Frm);
    }

    [AvaloniaFact]
    public async Task AnEditThatEndsBeforeItStartsIsRefused()
    {
        var editor = OpenForm();
        var code = LineOf(Frm, "    Command1.Enabled");

        var result = await sut.TryApplyEdits("frmOrders", [new AddinTextEdit(code, 9, code, 5, "x")], null);

        result.Succeeded.Should().BeFalse();
        result.Reason.Should().Be(
            $"The edit ends (line {code}, column 5) before it starts (line {code}, column 9). Nothing was changed.");
        editor.Document.Text.Should().Be(Frm);
    }

    [AvaloniaFact]
    public async Task APositionTheDocumentDoesNotHaveIsRefusedRatherThanThrown()
    {
        var editor = OpenForm();
        var lines = editor.Document.LineCount;

        var edit = await sut.TryApplyEdits("frmOrders", [new AddinTextEdit(lines + 5, 1, lines + 5, 1, "x")], null);
        var move = await sut.TryNavigateTo("frmOrders", lines + 5, 1, null);
        var column = await sut.TryNavigateTo("frmOrders", 10, 0, null);

        edit.Reason.Should().Contain($"numbered 1..{lines} from the top of the file");
        move.Reason.Should().Contain($"line {lines + 5}").And.Contain($"1..{lines}");
        column.Reason.Should().Contain("columns are numbered from 1");
        editor.Document.Text.Should().Be(Frm);
    }

    [AvaloniaFact]
    public void NavigatingToALineTheDocumentDoesNotHaveMovesNothingAndDoesNotThrow()
    {
        // The move is posted to the UI thread, where a throw ends the process. It used to throw there.
        var editor = OpenForm();
        var caret = editor.CaretOffset;

        sut.NavigateTo("frmOrders", 999, 1, null).Should().BeTrue("the name was found; the move is what is refused");
        Dispatcher.UIThread.RunJobs();

        editor.CaretOffset.Should().Be(caret);
    }

    [AvaloniaFact]
    public async Task NavigatingToALineMovesTheCaretThereCountedFromTheTopOfTheFile()
    {
        var editor = OpenForm();
        var line = LineOf(Frm, "Private Sub Command1_Click");

        (await sut.TryNavigateTo("FRMORDERS", line, 5, null)).Succeeded.Should().BeTrue("a name is matched without regard to case");

        editor.Document.GetLocation(editor.CaretOffset).Line.Should().Be(line);
        editor.Document.GetLocation(editor.CaretOffset).Column.Should().Be(5);
    }

    [Fact]
    public async Task ANameNoLoadedProjectHasIsRefusedWithWhatTheProjectsHold()
    {
        loaded.Add(TestHelpers.CreateProjectWithModule("Shop", "Helpers"));

        var result = await sut.TrySetContent("Helpres", ModuleCode, null);

        result.Succeeded.Should().BeFalse();
        result.Reason.Should().Contain("No form or module named 'Helpres'").And.Contain("Helpers");
    }

    // ── Files VB6 wrote ─────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task AFormVb6WroteReadsAsItsFileAndTakesItBackUnchanged()
        => await RoundTrip("bill-of-fare", "frmBillOfFare", "BillOfFare.frm");

    [AvaloniaFact]
    public async Task AClassVb6WroteReadsAsItsFileAndTakesItBackUnchanged()
        => await RoundTrip("battleship", "Game", "Game.cls");

    /// <summary>
    /// A document VB6 wrote, opened in a code window, read through the add-in surface and written straight back.
    /// The read's header is found from the text and the document's from the prefix it was composed with, so a
    /// disagreement between the two on a real file would refuse an unchanged echo.
    /// </summary>
    private async Task RoundTrip(string demo, string name, string file)
    {
        await MakeService().OpenProject(Stage(Path.Join(FindUpwards("demo")!, demo)));
        var document = DocumentLookup.Find(loaded, name, null).Single();
        var editor = Show(document.Module is { } module ? NewEditor().Initialize(module) : NewEditor().Initialize(document.Form!));
        var bytes = await File.ReadAllBytesAsync(document.AbsolutePath!, TestContext.Current.CancellationToken);

        var read = sut.GetActiveDocument()!.Content;
        read.Should().Be(Vb6TextFile.ReadDocument(bytes).Text, $"an add-in reads {file} as the file holds it");

        var result = await sut.TrySetContent(name, read, null);

        result.Succeeded.Should().BeTrue(result.Reason);
        result.HeaderKept.Should().BeFalse();
        editor.Document.Text.Should().Be(read);
    }

    private ProjectService MakeService()
    {
        var manager = Substitute.For<IProjectManager>();
        manager.LoadedProjects.Returns(_ => loaded);
        manager.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
               .Do(ci => loaded.Add(ci.Arg<ProjectDefinition>()));

        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);

        var emptyDock = Substitute.For<IDocumentDockService>();
        emptyDock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel>());

        return new ProjectService(
            () => throw new InvalidOperationException("new-project dialog must not be reached"),
            Substitute.For<IWindowManager>(),
            Substitute.For<IEventBus>(),
            manager,
            Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(),
            sidecar,
            new FileBaselineStore(),
            Substitute.For<ILocalizationService>(),
            new HeaderRefresher(new EventBus(), emptyDock, new BreakpointService(), new BookmarkService()));
    }

    private static readonly string[] SourceExtensions =
        [".vbp", ".frm", ".frx", ".bas", ".cls", ".ctl", ".ctx", ".pag", ".pgx"];

    /// <summary>A copy of a repository project's source files in a directory of its own, returning its <c>.vbp</c>.</summary>
    private string Stage(string projectDirectory)
    {
        var target = Path.Join(dir, Path.GetFileName(projectDirectory));
        Directory.CreateDirectory(target);
        foreach (var source in Directory.EnumerateFiles(projectDirectory))
        {
            if (SourceExtensions.Contains(Path.GetExtension(source), StringComparer.OrdinalIgnoreCase))
                File.Copy(source, Path.Join(target, Path.GetFileName(source)));
        }
        return Directory.EnumerateFiles(target, "*.vbp").Single();
    }

    private static string? FindUpwards(string relative)
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            var candidate = Path.Join(at.FullName, relative);
            if (Directory.Exists(candidate))
                return candidate;
        }
        return null;
    }

    // ── Add-ins already built keep working ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TheMembersEarlierAddinsCallKeepTheirShapes()
    {
        // Add-ins are loaded as built assemblies, so a member they call must keep its exact signature: a change
        // of return type is a MissingMethodException in an add-in nobody rebuilt.
        Member("NavigateTo", typeof(string), typeof(int), typeof(int)).ReturnType.Should().Be(typeof(void));
        Member("SetContent", typeof(string), typeof(string)).ReturnType.Should().Be(typeof(Task));
        Member("ApplyEdits", typeof(string), typeof(IReadOnlyList<AddinTextEdit>)).ReturnType.Should().Be(typeof(Task));
        Member("NavigateTo", typeof(string), typeof(int), typeof(int), typeof(string)).ReturnType.Should().Be(typeof(bool));
        Member("SetContent", typeof(string), typeof(string), typeof(string)).ReturnType.Should().Be(typeof(Task<bool>));
        Member("ApplyEdits", typeof(string), typeof(IReadOnlyList<AddinTextEdit>), typeof(string)).ReturnType.Should().Be(typeof(Task<bool>));

        static MethodInfo Member(string name, params Type[] parameters) =>
            typeof(IEditorAccess).GetMethod(name, parameters)
            ?? throw new InvalidOperationException($"IEditorAccess.{name}({string.Join(", ", parameters.Select(p => p.Name))}) is gone");
    }

    /// <summary>
    /// A host written before the reason-giving members existed, implementing only what it had to then. That this
    /// compiles is half the test.
    /// </summary>
    private sealed class EarlierHost(bool answer) : IEditorAccess
    {
        public AddinDocument? GetActiveDocument() => null;
        public AddinSelection? GetSelection() => null;
        public void NavigateTo(string fileName, int line, int column) { }
        public Task SetContent(string fileName, string content) => Task.CompletedTask;
        public Task ApplyEdits(string fileName, IReadOnlyList<AddinTextEdit> edits) => Task.CompletedTask;
        public bool NavigateTo(string fileName, int line, int column, string? project) => answer;
        public Task<bool> SetContent(string fileName, string content, string? project) => Task.FromResult(answer);
        public Task<bool> ApplyEdits(string fileName, IReadOnlyList<AddinTextEdit> edits, string? project) => Task.FromResult(answer);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AHostThatOnlyAnswersTrueOrFalseStillAnswersTheReasonGivingMembers(bool answer)
    {
        IEditorAccess host = new EarlierHost(answer);

        var results = new[]
        {
            await host.TrySetContent("Module1", "", null),
            await host.TryApplyEdits("Module1", [], null),
            await host.TryNavigateTo("Module1", 1, 1, null),
        };

        foreach (var result in results)
        {
            result.Succeeded.Should().Be(answer);
            if (answer) result.Reason.Should().BeNull();
            else result.Should().Be(AddinEditResult.WithoutReason, "a refusal still says that no reason was given");
        }
    }
}
