using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Folding;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The header's fold: folded when the window opens, folded again whenever it is re-created, unless the developer
/// expanded it in that window, and none of it waiting on a language server (hexide-io/HexIDE#273 task 3.14).
/// </summary>
/// <remarks>
/// <para>
/// Against a real editor, because the fold is the folding manager's section and the cases that matter are the
/// ones where the manager removes it or hands it to another fold: a reload, a header rewritten whole, a server
/// fold starting where the header does.
/// </para>
/// <para>
/// Expanding is done by setting the section's <c>IsFolded</c>, which is all the fold margin's click does, read
/// in AvaloniaEdit's source. The click itself cannot be driven in the running IDE: no automation tool reaches
/// a margin (hexide-io/HexIDE#707).
/// </para>
/// </remarks>
public class HeaderFoldTests : IDisposable
{
    private readonly List<Window> windows = [];
    private readonly List<CodeEditorViewModel> made = [];
    private readonly ILspClient lsp = Substitute.For<ILspClient>();

    /// <summary>What the server answers a folding request with.</summary>
    private FoldingRange[] folds = [];

    public HeaderFoldTests()
    {
        lsp.IsRunning.Returns(true);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());
        lsp.RequestFoldingRangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(folds));
    }

    public void Dispose()
    {
        foreach (var w in windows) w.Close();
        foreach (var vm in made) vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    // Header: lines 1-9. Code from line 10.
    private const string Frm =
        "VERSION 5.00\r\n" +
        "Begin VB.Form frmOrders \r\n" +
        "   Caption         =   \"Orders\"\r\n" +
        "   Begin VB.CommandButton cmdOK \r\n" +
        "      Caption         =   \"OK\"\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"frmOrders\"\r\n" +
        "Attribute VB_PredeclaredId = True\r\n" +
        "Option Explicit\r\n" +
        "\r\n" +
        "Private Sub cmdOK_Click()\r\n" +
        "    Unload Me\r\n" +
        "End Sub\r\n";

    private const int HeaderLines = 9;

    /// <summary>The Sub, 0-based as a server sends it: lines 12-14 of the file.</summary>
    private static readonly FoldingRange SubFold = new(11, 13);

    private CodeEditorViewModel MakeViewModel()
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<DocumentSavedEvent>(Arg.Any<Action<DocumentSavedEvent>>()).Returns(Substitute.For<IDisposable>());

        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);

        var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), Substitute.For<IProjectService>(),
            eventBus, lsp, settings, Substitute.For<IStatusBarService>(), new BookmarkService(), new BreakpointService(),
            Substitute.For<IDebugController>(), new RunScope(), Substitute.For<ILocalizationService>());
        made.Add(vm);
        return vm;
    }

    private CodeEditorViewModel OpenForm()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        return MakeViewModel().Initialize(new FormDeserializer().Deserialize(project, Frm, NullSink.Instance)!);
    }

    private CodeEditorViewModel OpenModule(ModuleKind kind, string name)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, name, kind);
        module.UpdateCode("Option Explicit\r\n\r\nPublic Sub Go()\r\n    Debug.Print 1\r\nEnd Sub\r\n");
        return MakeViewModel().Initialize(module);
    }

    private (Window Window, TextEditor Editor) Show(CodeEditorViewModel vm)
    {
        var window = new Window { Width = 1200, Height = 800 };
        windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, Attach(window, vm));
    }

    private static TextEditor Attach(Window window, CodeEditorViewModel vm)
    {
        var view = new CodeEditorView { DataContext = vm };
        window.Content = view;
        Dispatcher.UIThread.RunJobs();
        return view.FindControl<TextEditor>("TextEditor")!;
    }

    private static FoldingManager Folding(TextEditor editor) =>
        (FoldingManager)editor.TextArea.TextView.GetService(typeof(FoldingManager))!;

    /// <summary>The section folding the top of the file, if there is one; asserts there is not more than one.</summary>
    private static FoldingSection? HeaderSection(TextEditor editor) =>
        Folding(editor).GetFoldingsAt(0).SingleOrDefault();

    /// <summary>Where the header fold should end: the end of the header's last line, before its line break.</summary>
    private static int HeaderEnd(TextEditor editor, int headerLines) =>
        editor.Document.GetLineByNumber(headerLines).EndOffset;

    /// <summary>Every section other than the header's, as 1-based first and last lines.</summary>
    private static IEnumerable<(int First, int Last)> OtherFolds(TextEditor editor) =>
        Folding(editor).AllFoldings
            .Where(s => s.StartOffset != 0)
            .Select(s => (editor.Document.GetLineByOffset(s.StartOffset).LineNumber,
                          editor.Document.GetLineByOffset(s.EndOffset).LineNumber));

    /// <summary>Lets the view's debounced folding request go out and its answer come back.</summary>
    /// <remarks>The view waits 500 ms before asking, and the answer is applied on the UI thread.</remarks>
    private static async Task SettleFolding()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(900), CancellationToken.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Expand(TextEditor editor)
    {
        HeaderSection(editor)!.IsFolded = false;
        Dispatcher.UIThread.RunJobs();
    }

    private static void ServerStateChanged(ILspClient lsp)
    {
        lsp.StateChanged += Raise.Event<EventHandler>(lsp, EventArgs.Empty);
        Dispatcher.UIThread.RunJobs();
    }

    // ── Opening ───────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void AFormOpensWithItsHeaderFolded()
    {
        // The delta's first scenario. The empty answer the attach asks for has been applied by now too; that the
        // fold does not wait for it is TheHeaderIsFoldedBeforeAServerAnswers.
        var vm = OpenForm();
        var (_, editor) = Show(vm);

        var header = HeaderSection(editor);
        header.Should().NotBeNull();
        header!.EndOffset.Should().Be(HeaderEnd(editor, HeaderLines), "the fold ends where the header's last line does");
        header.IsFolded.Should().BeTrue();

        editor.TextArea.TextView.EnsureVisualLines();
        editor.TextArea.TextView.VisualLines.Select(v => v.FirstDocumentLine.LineNumber).Take(2)
            .Should().Equal([1, HeaderLines + 1], "folded, the header is one row and the code starts on the next");
    }

    [AvaloniaFact]
    public void TheHeaderIsFoldedBeforeAServerAnswers()
    {
        // A server still starting, or slow, has not answered when the window opens. The fold is not waiting for it.
        var never = new TaskCompletionSource<FoldingRange[]>();
        lsp.RequestFoldingRangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(never.Task);
        var vm = OpenForm();
        var (_, editor) = Show(vm);

        lsp.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILspClient.RequestFoldingRangesAsync))
            .Should().BeGreaterThan(0, "the server was asked, and has not answered");
        HeaderSection(editor)!.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task NoServerIsAttached()
    {
        // The lsp-client delta's scenario, on a form; AClassOpensWithItsHeaderFolded is the class. An empty answer
        // is what no server looks like, and an answer used to replace every fold the window had.
        lsp.IsRunning.Returns(false);
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        await SettleFolding();

        HeaderSection(editor)!.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task TheHeaderStaysFoldedWhenTheServerAnswersWithNothing()
    {
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        await SettleFolding();

        lsp.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILspClient.RequestFoldingRangesAsync))
            .Should().BeGreaterThan(0, "the server was asked, and answered with nothing");
        HeaderSection(editor)!.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public void AOneLineHeaderIsNotFolded()
    {
        // A .bas's header is its VB_Name line, and folding one line hides nothing.
        var vm = OpenModule(ModuleKind.StandardModule, "Module1");
        var (_, editor) = Show(vm);

        editor.Document.GetText(editor.Document.GetLineByNumber(1)).Should().StartWith("Attribute VB_Name");
        HeaderSection(editor).Should().BeNull();
    }

    [AvaloniaFact]
    public void AClassOpensWithItsHeaderFolded()
    {
        // With no server, which is the lsp-client delta's scenario for a class.
        lsp.IsRunning.Returns(false);
        var vm = OpenModule(ModuleKind.ClassModule, "Order");
        var (_, editor) = Show(vm);

        var headerLines = ReadOnlyRegions.HeaderLineCount(vm.Document.Text, vm.BufferPrefixLength);
        headerLines.Should().BeGreaterThan(1);
        var header = HeaderSection(editor);
        header.Should().NotBeNull();
        header!.EndOffset.Should().Be(HeaderEnd(editor, headerLines));
        header.IsFolded.Should().BeTrue();
    }

    // ── A server's folds ─────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task AServersFoldsJoinTheHeaderFold()
    {
        folds = [SubFold];
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        await SettleFolding();

        HeaderSection(editor)!.IsFolded.Should().BeTrue();
        OtherFolds(editor).Should().Equal([(12, 14)]);
        Folding(editor).AllFoldings.Single(s => s.StartOffset != 0).IsFolded.Should().BeFalse(
            "a server's fold opens expanded, as it always has");
    }

    [AvaloniaFact]
    public async Task AServerFoldThatCrossesTheHeaderIsDropped()
    {
        folds =
        [
            new FoldingRange(5, 10),  // the button's End through the blank line: half header, half code
            new FoldingRange(1, 6),   // the form's Begin..End, wholly inside the header
            SubFold,
        ];
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        await SettleFolding();

        OtherFolds(editor).Should().BeEquivalentTo([(2, 7), (12, 14)], "a fold inside the header nests in it; one across it cannot");
        HeaderSection(editor)!.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task AServerFoldStartingWhereTheHeaderStartsLeavesTheHeaderFolded()
    {
        // Measured: the folding manager matches a fold to a section by start offset alone, so a server fold
        // starting at the top of the file, listed first, took the header's section and the header's fold came
        // back as a new, expanded one. Here the server answers twice, so the second answer meets the first's
        // sections.
        folds = [new FoldingRange(0, 13), SubFold];
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        await SettleFolding();
        ServerStateChanged(lsp);
        await SettleFolding();

        Folding(editor).GetFoldingsAt(0).Should().ContainSingle("the server's fold from the top of the file is dropped");
        var header = HeaderSection(editor);
        header!.EndOffset.Should().Be(HeaderEnd(editor, HeaderLines), "and the one kept is the header's");
        header.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task AServersFoldsSurviveTheHeaderFoldBeingMadeAgain()
    {
        // Between answers the window re-makes the header's fold on its own, and it keeps the server's folds it is
        // showing, and how the developer left them, rather than dropping them until the next answer.
        folds = [SubFold];
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        await SettleFolding();
        Folding(editor).AllFoldings.Single(s => s.StartOffset != 0).IsFolded = true;

        vm.Document.Insert(HeaderEnd(editor, HeaderLines), " 'appended");
        Dispatcher.UIThread.RunJobs();

        OtherFolds(editor).Should().Equal([(12, 14)]);
        Folding(editor).AllFoldings.Single(s => s.StartOffset != 0).IsFolded.Should().BeTrue();
    }

    // ── The developer's choice survives re-creation ─────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task AnExpandedHeaderStaysExpandedWhenTheServerAnswersAgain()
    {
        folds = [SubFold];
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        await SettleFolding();

        Expand(editor);
        ServerStateChanged(lsp);
        await SettleFolding();

        HeaderSection(editor)!.IsFolded.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task AnExpandedHeaderStaysExpandedWhenTheCodeIsReformatted()
    {
        // The delta's scenario. The answer is the bundled server's shape, one edit over the whole document
        // with every header line flattened, so the header's fold is rewritten if anything is.
        folds = [SubFold];
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        Expand(editor);

        var before = vm.Document.Text;
        var buffer = before.Replace("\r\n", "\n");
        var headerEnd = buffer.IndexOf("Option Explicit", StringComparison.Ordinal);
        var flattened = string.Join("\n", buffer[..headerEnd].Split('\n').Select(l => l.TrimStart()));
        var answer = flattened + buffer[headerEnd..].Replace("    Unload Me", "        Unload Me");
        vm.ApplyFormatting([new TextEdit(
            new HexIDE.Lsp.Messages.Range(new Position(0, 0), new Position(buffer.Split('\n').Length, 0)), answer)]);
        Dispatcher.UIThread.RunJobs();

        vm.Document.Text.Should().NotBe(before, "the formatting landed");
        HeaderSection(editor)!.IsFolded.Should().BeFalse();
        await SettleFolding();
        HeaderSection(editor)!.IsFolded.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task ReformattingAfterExpandingTheHeader()
    {
        // The delta's scenario as it is written, on a class, whose header is all of its prefix.
        var vm = OpenModule(ModuleKind.ClassModule, "Order");
        var (_, editor) = Show(vm);
        Expand(editor);

        var before = vm.Document.Text;
        var buffer = before.Replace("\r\n", "\n");
        var headerEnd = buffer.IndexOf("Option Explicit", StringComparison.Ordinal);
        var flattened = string.Join("\n", buffer[..headerEnd].Split('\n').Select(l => l.TrimStart()));
        var answer = flattened + buffer[headerEnd..].Replace("    Debug.Print 1", "        Debug.Print 1");
        vm.ApplyFormatting([new TextEdit(
            new HexIDE.Lsp.Messages.Range(new Position(0, 0), new Position(buffer.Split('\n').Length, 0)), answer)]);
        Dispatcher.UIThread.RunJobs();

        vm.Document.Text.Should().NotBe(before, "the formatting landed");
        HeaderSection(editor)!.IsFolded.Should().BeFalse();
        await SettleFolding();
        HeaderSection(editor)!.IsFolded.Should().BeFalse("and the answer the change asked for re-made the fold");
    }

    [AvaloniaFact]
    public async Task ReformattingADocumentWhoseHeaderIsFolded()
    {
        // The code-editor delta's scenario, with the answer the bundled server used to send: one edit over the
        // whole document, every header line flattened to column zero, in \n. The header comes through unchanged
        // because the guard dropped that part, and the fold over it stays folded. The reload half of the scenario
        // is AFoldedHeaderStaysFoldedWhenTheFileIsReloaded.
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        var header = vm.Document.Text[..HeaderEnd(editor, HeaderLines)];

        var buffer = vm.Document.Text.Replace("\r\n", "\n");
        var headerEnd = buffer.IndexOf("Option Explicit", StringComparison.Ordinal);
        var flattened = string.Join("\n", buffer[..headerEnd].Split('\n').Select(l => l.TrimStart()));
        vm.ApplyFormatting([new TextEdit(
            new HexIDE.Lsp.Messages.Range(new Position(0, 0), new Position(buffer.Split('\n').Length, 0)),
            flattened + buffer[headerEnd..].Replace("    Unload Me", "        Unload Me"))]);
        Dispatcher.UIThread.RunJobs();

        vm.Document.Text.Should().Contain("        Unload Me", "the formatting landed in the code");
        vm.Document.Text.Should().StartWith(header, "and left the header as it was");
        HeaderSection(editor)!.IsFolded.Should().BeTrue();
        await SettleFolding();
        HeaderSection(editor)!.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task AnExpandedHeaderStaysExpandedWhenTheDesignerRewritesIt()
    {
        // A caption changed in the designer reaches the code window as a header write.
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        Expand(editor);

        var prefix = vm.Document.Text[..vm.BufferPrefixLength];
        vm.RefreshPrefix(prefix.Replace("\"Orders\"", "\"Orders and returns\""));
        Dispatcher.UIThread.RunJobs();

        var header = HeaderSection(editor);
        header!.EndOffset.Should().Be(HeaderEnd(editor, HeaderLines), "the fold follows the header's new length");
        header.IsFolded.Should().BeFalse();
        await SettleFolding();
        HeaderSection(editor)!.IsFolded.Should().BeFalse("and the answer the change asked for re-made the fold");
    }

    [AvaloniaFact]
    public void AHeaderThatGrowsAtItsEndIsFoldedToItsNewEndAtOnce()
    {
        // Measured: text inserted exactly at a fold's end is left outside it, so the fold would end mid-line, with
        // the new text showing after the folded row until the next answer re-made it. The window re-makes it on
        // the change itself.
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        var end = HeaderEnd(editor, HeaderLines);

        vm.Document.Insert(end, " 'appended");
        Dispatcher.UIThread.RunJobs();

        var header = HeaderSection(editor);
        header!.EndOffset.Should().Be(end + " 'appended".Length);
        header.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public void AClassHeaderRewrittenWholeKeepsItsFoldState()
    {
        // A class's header is all of its prefix, so a header write replaces the fold's whole extent and the
        // folding manager removes it, unfolding it as it does. The window has to have read the state first.
        var vm = OpenModule(ModuleKind.ClassModule, "Order");
        var (_, editor) = Show(vm);
        var headerLines = ReadOnlyRegions.HeaderLineCount(vm.Document.Text, vm.BufferPrefixLength);
        Expand(editor);
        var before = HeaderSection(editor);

        var prefix = vm.Document.Text[..vm.BufferPrefixLength];
        vm.RefreshPrefix(prefix.Replace("\"Order\"", "\"Orders\""));
        Dispatcher.UIThread.RunJobs();

        var after = HeaderSection(editor);
        after.Should().NotBeNull();
        ReferenceEquals(before, after).Should().BeFalse("the rewrite removed the old fold, so this is a new one");
        after!.EndOffset.Should().Be(HeaderEnd(editor, headerLines));
        after.IsFolded.Should().BeFalse();
    }

    [AvaloniaFact]
    public void AFoldedClassHeaderRewrittenWholeIsFoldedAgain()
    {
        var vm = OpenModule(ModuleKind.ClassModule, "Order");
        var (_, editor) = Show(vm);

        var prefix = vm.Document.Text[..vm.BufferPrefixLength];
        vm.RefreshPrefix(prefix.Replace("\"Order\"", "\"Orders\""));
        Dispatcher.UIThread.RunJobs();

        HeaderSection(editor)!.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task AFoldedHeaderStaysFoldedWhenTheFileIsReloaded()
    {
        // The delta's scenario. A reload assigns the whole document, which removes every fold; asserted before
        // the debounced request, because waiting for it is the half-second of expanded header this prevents.
        var vm = OpenForm();
        var (_, editor) = Show(vm);

        var prefix = vm.Document.Text[..vm.BufferPrefixLength];
        vm.ReloadFrom(prefix, vm.BufferBody + "\r\nPrivate Sub Form_Load()\r\nEnd Sub\r\n");
        Dispatcher.UIThread.RunJobs();

        HeaderSection(editor)!.IsFolded.Should().BeTrue();
        await SettleFolding();
        HeaderSection(editor)!.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task AnExpandedHeaderStaysExpandedWhenTheFileIsReloaded()
    {
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        Expand(editor);

        var prefix = vm.Document.Text[..vm.BufferPrefixLength];
        vm.ReloadFrom(prefix, vm.BufferBody + "\r\nPrivate Sub Form_Load()\r\nEnd Sub\r\n");
        Dispatcher.UIThread.RunJobs();

        HeaderSection(editor)!.IsFolded.Should().BeFalse();
        await SettleFolding();
        HeaderSection(editor)!.IsFolded.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task AReloadThatGrowsTheHeaderLeavesItFolded()
    {
        // Seen live before the fix: the reload put the caret back at its old offset, which a longer header had
        // moved into the header, and the editor unfolds a fold the caret moves into. The window then kept it
        // open as though the developer had. The caret is at the start of the first line of code, as it was there.
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        vm.CaretOffset = editor.Document.GetLineByNumber(HeaderLines + 1).Offset;
        Dispatcher.UIThread.RunJobs();

        var prefix = vm.Document.Text[..vm.BufferPrefixLength];
        vm.ReloadFrom(prefix.Replace("\"Orders\"", "\"Orders, returns and a much longer caption\""), vm.BufferBody);
        Dispatcher.UIThread.RunJobs();

        editor.TextArea.Caret.Line.Should().Be(HeaderLines + 1, "the caret is still on the first line of code");
        HeaderSection(editor)!.IsFolded.Should().BeTrue();
        await SettleFolding();
        HeaderSection(editor)!.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task MovingTheCaretIntoTheHeaderExpandsItAndItStaysExpanded()
    {
        // The editor unfolds any fold the caret moves into, which is how a developer navigating to a header line
        // sees it. The window takes that as the developer expanding it, so it is not folded away again under
        // them by the next answer. It is why nothing the IDE does on its own may put the caret there.
        var vm = OpenForm();
        var (_, editor) = Show(vm);

        vm.CaretOffset = editor.Document.GetLineByNumber(3).Offset + 3;
        Dispatcher.UIThread.RunJobs();
        HeaderSection(editor)!.IsFolded.Should().BeFalse();

        ServerStateChanged(lsp);
        await SettleFolding();
        HeaderSection(editor)!.IsFolded.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task AHeaderFoldedAgainAfterExpandingIsFoldedWhenReCreated()
    {
        // The developer's last choice is the one kept, not their first. The expansion is seen by a re-making of
        // the fold before it is folded again, so a flag that could only ever be set would fail here.
        var vm = OpenForm();
        var (_, editor) = Show(vm);
        Expand(editor);
        ServerStateChanged(lsp);
        await SettleFolding();
        HeaderSection(editor)!.IsFolded.Should().BeFalse("the expansion has been seen");
        HeaderSection(editor)!.IsFolded = true;

        var prefix = vm.Document.Text[..vm.BufferPrefixLength];
        vm.ReloadFrom(prefix, vm.BufferBody + "\r\nPrivate Sub Form_Load()\r\nEnd Sub\r\n");
        Dispatcher.UIThread.RunJobs();

        HeaderSection(editor)!.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public void AnExpandedHeaderStaysExpandedAcrossADockMove()
    {
        // A dock move re-materialises the view around the same view model. Nothing changes in the document, so
        // the fold's state is read as the old view detaches or not at all.
        var vm = OpenForm();
        var (window, editor) = Show(vm);
        Expand(editor);

        window.Content = null;
        Dispatcher.UIThread.RunJobs();
        var moved = Attach(window, vm);

        HeaderSection(moved)!.IsFolded.Should().BeFalse();
    }

    [AvaloniaFact]
    public void ANewWindowOnTheSameFileOpensFolded()
    {
        // "In that window": expanding is not remembered for the file.
        var first = OpenForm();
        var (_, editor) = Show(first);
        Expand(editor);

        var (_, second) = Show(OpenForm());

        HeaderSection(second)!.IsFolded.Should().BeTrue();
    }
}
