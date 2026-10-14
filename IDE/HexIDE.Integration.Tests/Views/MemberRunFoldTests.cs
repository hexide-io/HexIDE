using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
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
/// A member's attribute run folded into the end of the line it describes: folded when the window opens and whenever
/// the fold is made again, unless the developer expanded that run in that window, nested in a server's fold of the
/// procedure, and with the caret kept off the run's side of the box (hexide-io/HexIDE#273 task 4.3).
/// </summary>
/// <remarks>
/// <para>
/// Against a real editor, as <see cref="HeaderFoldTests"/> is and for the same reasons: the fold is the folding
/// manager's section, and the cases that matter are where the manager removes it, pairs it with another or moves it.
/// What the library does at a fold starting part-way along a line, which all of this rests on, is measured on its
/// own in <see cref="MidLineFoldBehaviourTests"/>.
/// </para>
/// <para>
/// Expanding is done by setting the section's <c>IsFolded</c>, which is what the fold margin's marker and the box
/// itself do when clicked, read in AvaloniaEdit's source.
/// </para>
/// </remarks>
public class MemberRunFoldTests : IDisposable
{
    private readonly List<Window> windows = [];
    private readonly List<CodeEditorViewModel> made = [];
    private readonly ILspClient lsp = Substitute.For<ILspClient>();
    private readonly IStatusBarService statusBar = Substitute.For<IStatusBarService>();
    private readonly BreakpointService breakpoints = new();

    /// <summary>What the server answers a folding request with, computed from the document when asked.</summary>
    private Func<CodeEditorViewModel, FoldingRange[]> folds = _ => [];

    private CodeEditorViewModel? answering;

    public MemberRunFoldTests()
    {
        lsp.IsRunning.Returns(true);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());
        lsp.RequestFoldingRangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(answering is null ? [] : folds(answering)));
    }

    public void Dispose()
    {
        foreach (var w in windows) w.Close();
        foreach (var vm in made) vm.Dispose();
        GC.SuppressFinalize(this);
    }

    // A procedure with a two-line run, as VB6 writes one, and its body.
    private const string Procedure =
        "Option Explicit\r\n" +
        "\r\n" +
        "Public Sub Go()\r\n" +
        "Attribute Go.VB_Description = \"Goes\"\r\n" +
        "Attribute Go.VB_UserMemId = 0\r\n" +
        "    Debug.Print 1\r\n" +
        "    Debug.Print 2\r\n" +
        "End Sub\r\n";

    // A property whose Get and Let each carry a run, which VB6 keeps (IDE probes A and B2): keyed TOTAL 0 and TOTAL 1.
    private const string Property =
        "Option Explicit\r\n" +
        "\r\n" +
        "Public Property Get Total() As Long\r\n" +
        "Attribute Total.VB_Description = \"The total\"\r\n" +
        "    Total = 1\r\n" +
        "End Property\r\n" +
        "\r\n" +
        "Public Property Let Total(ByVal Value As Long)\r\n" +
        "Attribute Total.VB_Description = \"Sets the total\"\r\n" +
        "End Property\r\n";

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
            eventBus, lsp, settings, statusBar, new BookmarkService(), breakpoints,
            Substitute.For<IDebugController>(), new RunScope(), Substitute.For<ILocalizationService>());
        made.Add(vm);
        return vm;
    }

    private CodeEditorViewModel Open(string code, ModuleKind kind = ModuleKind.StandardModule)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, kind == ModuleKind.StandardModule ? "Module1" : "Order", kind);
        module.UpdateCode(code);
        var vm = MakeViewModel().Initialize(module);
        answering = vm;
        return vm;
    }

    private (Window Window, TextEditor Editor) Show(CodeEditorViewModel vm)
    {
        var window = new Window { Width = 1200, Height = 800 };
        windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = Attach(window, vm);
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
        return (window, editor);
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

    private static DocumentLine LineWith(TextEditor editor, string text, int nth = 0)
    {
        var at = -1;
        for (var i = 0; i <= nth; i++)
            at = editor.Document.Text.IndexOf(text, at + 1, StringComparison.Ordinal);
        at.Should().BeGreaterThanOrEqualTo(0, $"the premise: the text has {text}");
        return editor.Document.GetLineByOffset(at);
    }

    /// <summary>The fold starting at the end of the line holding <paramref name="declaration"/>, if there is one.</summary>
    private static FoldingSection? RunFold(TextEditor editor, string declaration, int nth = 0) =>
        Folding(editor).GetFoldingsAt(LineWith(editor, declaration, nth).EndOffset).SingleOrDefault();

    /// <summary>The server's fold of the Sub, 0-based as a server sends it.</summary>
    private static FoldingRange[] ServerFoldsTheSub(CodeEditorViewModel vm)
    {
        var text = vm.Document.Text;
        var start = vm.Document.GetLineByOffset(text.IndexOf("Public Sub Go()", StringComparison.Ordinal)).LineNumber;
        var end = vm.Document.GetLineByOffset(text.IndexOf("End Sub", StringComparison.Ordinal)).LineNumber;
        return [new FoldingRange(start - 1, end - 1)];
    }

    /// <summary>Lets the view's debounced folding request go out and its answer come back.</summary>
    private static async Task SettleFolding()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(900), CancellationToken.None);
        Dispatcher.UIThread.RunJobs();
    }

    private void ServerStateChanged()
    {
        lsp.StateChanged += Raise.Event<EventHandler>(lsp, EventArgs.Empty);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(TextEditor editor, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        editor.TextArea.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers,
        });
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Typed text as the input system delivers it: a TextInput event on the focused text area.</summary>
    private static void TypeText(TextEditor editor, string text)
    {
        editor.TextArea.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });
        Dispatcher.UIThread.RunJobs();
    }

    private static void PutCaret(TextEditor editor, int offset)
    {
        editor.TextArea.Caret.Offset = offset;
        Dispatcher.UIThread.RunJobs();
    }

    private static int Caret(TextEditor editor) => editor.TextArea.Caret.Offset;

    // ── The delta's scenario ────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void AProcedureWithADescription()
    {
        // The code-editor delta's scenario: the attribute is folded into the end of the procedure's declaration line,
        // and cannot be edited.
        var vm = Open(Procedure);
        var (_, editor) = Show(vm);
        var declaration = LineWith(editor, "Public Sub Go()");
        var last = LineWith(editor, "Attribute Go.VB_UserMemId");

        var fold = RunFold(editor, "Public Sub Go()");
        fold.Should().NotBeNull();
        fold!.IsFolded.Should().BeTrue();
        (fold.StartOffset, fold.EndOffset).Should().Be((declaration.EndOffset, last.EndOffset),
            "from the end of the declaration's text to the end of the run's, so the body keeps its own line");
        fold.Title.Should().BeNull("the library draws its own box, and there is nothing to translate");

        editor.TextArea.TextView.EnsureVisualLines();
        editor.TextArea.TextView.VisualLines
            .Select(v => (v.FirstDocumentLine.LineNumber, v.LastDocumentLine.LineNumber))
            .Should().Contain((declaration.LineNumber, last.LineNumber), "folded into the declaration's own row")
            .And.Contain((last.LineNumber + 1, last.LineNumber + 1), "and the body starts the next one");

        var before = vm.Document.Text;
        var sections = editor.TextArea.ReadOnlySectionProvider;
        var run = LineWith(editor, "Attribute Go.VB_Description");
        sections.CanInsert(run.Offset + 10).Should().BeFalse("the run cannot be typed into");
        sections.GetDeletableSegments(new TextSegment { StartOffset = run.Offset, EndOffset = last.EndOffset })
            .Sum(s => s.Length).Should().Be(0, "nor deleted");
        var end = declaration.EndOffset;
        PutCaret(editor, last.EndOffset);
        TypeText(editor, "X");
        vm.Document.Text.Should().Be(before[..end] + "X" + before[end..],
            "a key typed with the caret put after the box lands at the end of the declaration, not in the run");
    }

    // ── With and without a server ──────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ARunIsFoldedBeforeAServerAnswers()
    {
        // The fold is the window's own, as the header's is, and does not wait for a server that may never answer.
        var never = new TaskCompletionSource<FoldingRange[]>();
        lsp.RequestFoldingRangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(never.Task);
        var (_, editor) = Show(Open(Procedure));

        RunFold(editor, "Public Sub Go()")!.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task ARunsFoldNestsInsideTheServersFoldOfTheProcedure()
    {
        folds = ServerFoldsTheSub;
        var vm = Open(Procedure);
        var (_, editor) = Show(vm);
        await SettleFolding();

        var declaration = LineWith(editor, "Public Sub Go()");
        var procedure = Folding(editor).GetFoldingsAt(declaration.Offset).Single();
        var run = RunFold(editor, "Public Sub Go()")!;
        procedure.EndOffset.Should().Be(LineWith(editor, "End Sub").EndOffset, "the premise: the server's fold is the Sub");
        procedure.IsFolded.Should().BeFalse("a server's fold opens expanded, as it always has");
        run.IsFolded.Should().BeTrue("and the run's inside it is folded");
        (run.StartOffset > procedure.StartOffset && run.EndOffset < procedure.EndOffset).Should().BeTrue("nested");

        procedure.IsFolded = true;
        Dispatcher.UIThread.RunJobs();
        editor.TextArea.TextView.VisualLines.Select(v => v.LastDocumentLine.LineNumber)
            .Should().Contain(LineWith(editor, "End Sub").LineNumber, "folding the procedure hides its run with it");
        procedure.IsFolded = false;
        Dispatcher.UIThread.RunJobs();
        run.IsFolded.Should().BeTrue("and opening it again leaves the run as it was");
    }

    [AvaloniaFact]
    public async Task AServerAnswerLeavesAnExpandedRunExpandedAndAFoldedOneFolded()
    {
        var vm = Open(Property);
        var (_, editor) = Show(vm);
        RunFold(editor, "Property Let Total")!.IsFolded = false;
        Dispatcher.UIThread.RunJobs();

        ServerStateChanged();
        await SettleFolding();

        RunFold(editor, "Property Get Total")!.IsFolded.Should().BeTrue();
        RunFold(editor, "Property Let Total")!.IsFolded.Should().BeFalse();
    }

    // ── Made again: a reload, a format, a dock move ────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task ARunIsFoldedAgainAfterAReloadAndAnExpandedOneStaysExpanded()
    {
        // A reload assigns the whole document, which removes every fold. Asserted before the debounced request, since
        // waiting for it is the half-second of expanded runs the window prevents.
        var vm = Open(Property);
        var (_, editor) = Show(vm);
        RunFold(editor, "Property Let Total")!.IsFolded = false;
        Dispatcher.UIThread.RunJobs();
        var getBefore = RunFold(editor, "Property Get Total");

        var prefix = vm.Document.Text[..vm.BufferPrefixLength];
        vm.ReloadFrom(prefix, vm.BufferBody + "\r\nPublic Sub Other()\r\nEnd Sub\r\n");
        Dispatcher.UIThread.RunJobs();

        var get = RunFold(editor, "Property Get Total");
        get.Should().NotBeSameAs(getBefore, "the premise: the reload removed the old folds and these are new");
        get!.IsFolded.Should().BeTrue("a run is folded again unless its key was expanded");
        RunFold(editor, "Property Let Total")!.IsFolded.Should().BeFalse("and the Let's, by its key, was");
        await SettleFolding();
        RunFold(editor, "Property Get Total")!.IsFolded.Should().BeTrue();
        RunFold(editor, "Property Let Total")!.IsFolded.Should().BeFalse();
    }

    [AvaloniaFact]
    public void ExpandingTheGetsRunOfAPropertyLeavesTheLetsFoldedAfterAReload()
    {
        // The key's ordinal is what tells the two apart: both runs describe Total.
        var vm = Open(Property);
        var (_, editor) = Show(vm);
        RunFold(editor, "Property Get Total")!.IsFolded = false;
        Dispatcher.UIThread.RunJobs();

        var prefix = vm.Document.Text[..vm.BufferPrefixLength];
        vm.ReloadFrom(prefix, vm.BufferBody + "\r\n");
        Dispatcher.UIThread.RunJobs();

        RunFold(editor, "Property Get Total")!.IsFolded.Should().BeFalse();
        RunFold(editor, "Property Let Total")!.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task AFormatLeavesAFoldedRunFoldedAndAnExpandedOneExpanded()
    {
        // The bundled server's shape: one edit over the whole document. Here it re-cases the declarations and
        // re-indents the bodies, so lines on both sides of each run change.
        var vm = Open(Property.Replace("Public Property", "public property").Replace("    Total = 1", "  Total = 1"));
        var (_, editor) = Show(vm);
        RunFold(editor, "property Let Total")!.IsFolded = false;
        Dispatcher.UIThread.RunJobs();

        var buffer = vm.Document.Text;
        vm.ApplyFormatting([new TextEdit(
            new HexIDE.Lsp.Messages.Range(new Position(0, 0), new Position(vm.Document.LineCount, 0)),
            buffer.Replace("public property", "Public Property").Replace("  Total = 1", "    Total = 1"))]);
        Dispatcher.UIThread.RunJobs();

        vm.Document.Text.Should().Contain("Public Property Get Total").And.Contain("    Total = 1", "the premise: it landed");
        RunFold(editor, "Property Get Total")!.IsFolded.Should().BeTrue();
        RunFold(editor, "Property Let Total")!.IsFolded.Should().BeFalse();
        await SettleFolding();
        RunFold(editor, "Property Get Total")!.IsFolded.Should().BeTrue();
        RunFold(editor, "Property Let Total")!.IsFolded.Should().BeFalse();
    }

    [AvaloniaFact]
    public void AnExpandedRunStaysExpandedAndAFoldedOneFoldedAcrossADockMove()
    {
        // A dock move re-materialises the view around the same view model. Nothing in the document changes, so the
        // state is read as the old view detaches or not at all.
        var vm = Open(Property);
        var (window, editor) = Show(vm);
        RunFold(editor, "Property Let Total")!.IsFolded = false;
        Dispatcher.UIThread.RunJobs();

        window.Content = null;
        Dispatcher.UIThread.RunJobs();
        var moved = Attach(window, vm);

        RunFold(moved, "Property Get Total")!.IsFolded.Should().BeTrue();
        RunFold(moved, "Property Let Total")!.IsFolded.Should().BeFalse();
    }

    [AvaloniaFact]
    public void ADockMoveWithTheCaretAtTheEndOfAFoldedDeclarationBringsItsRunBackFolded()
    {
        // The caret at the anchor is before the box, not in the run, so the fold made new around it is folded. Taken
        // for inside, the run came back expanded after End and a dock move (task 4.3 review).
        var vm = Open(Procedure);
        var (window, editor) = Show(vm);
        var declaration = LineWith(editor, "Public Sub Go()");
        PutCaret(editor, declaration.Offset);
        Press(editor, Key.End);
        Caret(editor).Should().Be(declaration.EndOffset, "the premise");

        window.Content = null;
        Dispatcher.UIThread.RunJobs();
        var moved = Attach(window, vm);

        Caret(moved).Should().Be(declaration.EndOffset, "the premise: the caret came back where it was");
        RunFold(moved, "Public Sub Go()")!.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public void ARunFoldedAgainAfterExpandingIsFoldedWhenMadeAgain()
    {
        // The developer's last choice is the one kept. The expansion is seen by a reload before the run is folded
        // again, so a key that could only ever be added would fail here.
        var vm = Open(Procedure);
        var (_, editor) = Show(vm);
        RunFold(editor, "Public Sub Go()")!.IsFolded = false;
        var prefix = vm.Document.Text[..vm.BufferPrefixLength];
        vm.ReloadFrom(prefix, vm.BufferBody + "\r\n");
        Dispatcher.UIThread.RunJobs();
        RunFold(editor, "Public Sub Go()")!.IsFolded.Should().BeFalse("the expansion has been seen");

        RunFold(editor, "Public Sub Go()")!.IsFolded = true;
        vm.ReloadFrom(prefix, vm.BufferBody + "\r\n");
        Dispatcher.UIThread.RunJobs();

        RunFold(editor, "Public Sub Go()")!.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public void ANewWindowOnTheSameFileOpensItsRunsFolded()
    {
        // "In that window": expanding is not remembered for the file.
        var first = Open(Procedure);
        var (_, editor) = Show(first);
        RunFold(editor, "Public Sub Go()")!.IsFolded = false;

        var (_, second) = Show(Open(Procedure));

        RunFold(second, "Public Sub Go()")!.IsFolded.Should().BeTrue();
    }

    // ── A run that an edit makes, moves or unmakes ─────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task ARunThatStopsDescribingItsMemberLosesItsFoldAtOnceAndItIsNotKeptAsAServers()
    {
        // Commenting out the declaration leaves the run an orphan, which is free text and not folded. The window
        // drops the fold on the change itself, with no server to answer, and does not carry it forward as one of a
        // server's folds when it next re-makes its own.
        lsp.IsRunning.Returns(false);
        var vm = Open(Procedure);
        var (_, editor) = Show(vm);
        var declaration = LineWith(editor, "Public Sub Go()");

        vm.Document.Insert(declaration.Offset, "'");
        Dispatcher.UIThread.RunJobs();

        Folding(editor).GetFoldingsContaining(LineWith(editor, "Attribute Go.VB_Description").Offset).Should().BeEmpty(
            "the orphan's lines are in no fold");
        Folding(editor).AllFoldings.Should().BeEmpty();
        await SettleFolding();
        Folding(editor).AllFoldings.Should().BeEmpty("and nothing brought it back");

        vm.Document.Remove(LineWith(editor, "'Public Sub Go()").Offset, 1);
        Dispatcher.UIThread.RunJobs();
        RunFold(editor, "Public Sub Go()")!.IsFolded.Should().BeTrue("uncommented, the run is the procedure's again, folded");
    }

    [AvaloniaFact]
    public void ARunThatGrowsIsFoldedToItsNewEndAtOnce()
    {
        // Deleting the statement between a run and the orphan below it joins them, and the procedure's run takes in
        // the orphan's lines. The section keeps its start, so the manager pairs it and it stays folded, now to the end
        // of the longer run.
        var vm = Open(
            "Option Explicit\r\n\r\n" +
            "Public Sub Go()\r\n" +
            "Attribute Go.VB_Description = \"Goes\"\r\n" +
            "    Debug.Print 1\r\n" +
            "Attribute Go.VB_HelpID = 7\r\n" +
            "End Sub\r\n");
        var (_, editor) = Show(vm);
        var before = RunFold(editor, "Public Sub Go()");
        before!.EndOffset.Should().Be(LineWith(editor, "Attribute Go.VB_Description").EndOffset, "the premise");

        var statement = LineWith(editor, "Debug.Print");
        vm.Document.Remove(statement.Offset, statement.TotalLength);
        Dispatcher.UIThread.RunJobs();

        var after = RunFold(editor, "Public Sub Go()");
        after.Should().BeSameAs(before);
        after!.EndOffset.Should().Be(LineWith(editor, "Attribute Go.VB_HelpID").EndOffset);
        after.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public void ARunsSectionThatTheManagerPairsKeepsItsStateWhenOneEditBothGrowsTheRunAndRenamesItsMember()
    {
        // One undo step or Replace All that joins the orphan below the run onto it and then retypes the procedure's name:
        // the run's key changes from GO to GOO, and nothing between the two changes could record the expansion under
        // the new key, because the run had already grown past its fold. The manager pairs the section by its start, and
        // a section paired keeps the state it has, so the run the developer opened stays open.
        var vm = Open(
            "Option Explicit\r\n\r\n" +
            "Public Sub Go()\r\n" +
            "Attribute Go.VB_Description = \"Goes\"\r\n" +
            "    Debug.Print 1\r\n" +
            "Attribute Go.VB_HelpID = 7\r\n" +
            "End Sub\r\n");
        var (_, editor) = Show(vm);
        var fold = RunFold(editor, "Public Sub Go()")!;
        fold.IsFolded = false;
        Dispatcher.UIThread.RunJobs();

        var statement = LineWith(editor, "Debug.Print");
        var name = LineWith(editor, "Public Sub Go()").Offset + "Public Sub Go".Length;
        vm.Document.BeginUpdate();
        vm.Document.Remove(statement.Offset, statement.TotalLength);
        vm.Document.Insert(name, "o");
        vm.Document.EndUpdate();
        Dispatcher.UIThread.RunJobs();

        vm.MemberRunsNow.Single().Key.Should().Be(new MemberRunKey("GOO", 0), "the premise: the run is Goo's now");
        var after = RunFold(editor, "Public Sub Goo()");
        after.Should().BeSameAs(fold, "the premise: the section was paired, not made anew");
        after!.EndOffset.Should().Be(LineWith(editor, "Attribute Go.VB_HelpID").EndOffset);
        after.IsFolded.Should().BeFalse();

        // And the key it was last seen under, GO, is kept for it while its fold does not match it, so the next look
        // finds it moved from GO to GOO and drops GO: a Go described later opens folded (task 4.3 review).
        var prefix = vm.Document.Text[..vm.BufferPrefixLength];
        vm.ReloadFrom(prefix, vm.BufferBody + "\r\nPublic Sub Go()\r\nAttribute Go.VB_Description = \"Goes\"\r\nEnd Sub\r\n");
        Dispatcher.UIThread.RunJobs();
        RunFold(editor, "Public Sub Goo()")!.IsFolded.Should().BeFalse();
        RunFold(editor, "Public Sub Go()")!.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public void ARunDeletedWithItsProcedureAndBroughtBackByUndoComesBackAsItWasLeft()
    {
        // Its key is kept while no run has it, so Undo restores what the developer saw, expanded or folded.
        var vm = Open(Property);
        var (_, editor) = Show(vm);
        RunFold(editor, "Property Let Total")!.IsFolded = false;
        Dispatcher.UIThread.RunJobs();

        var from = LineWith(editor, "Public Property Get Total").Offset;
        vm.Document.Remove(from, vm.Document.TextLength - from);
        Dispatcher.UIThread.RunJobs();
        Folding(editor).AllFoldings.Should().BeEmpty("the premise: both runs went with their procedures");

        vm.Document.UndoStack.Undo();
        Dispatcher.UIThread.RunJobs();

        RunFold(editor, "Property Get Total")!.IsFolded.Should().BeTrue();
        RunFold(editor, "Property Let Total")!.IsFolded.Should().BeFalse();
    }

    [AvaloniaFact]
    public void UndoAfterDeletingAPropertysGetAloneBringsItsFoldedRunBackFolded()
    {
        // Deleting the Get alone renumbers the Let's run from TOTAL 1 to TOTAL 0, the Get's key, and the Let's expansion
        // was recorded under it, so Undo opened the Get's run, left folded (task 4.3 review). The Let's run now hands
        // the key back as Undo renumbers it, and the expansion goes with it.
        var vm = Open(Property);
        var (_, editor) = Show(vm);
        RunFold(editor, "Property Let Total")!.IsFolded = false;
        Dispatcher.UIThread.RunJobs();

        var get = LineWith(editor, "Public Property Get Total");
        var end = LineWith(editor, "End Property");
        vm.Document.Remove(get.Offset, end.Offset + end.TotalLength - get.Offset);
        Dispatcher.UIThread.RunJobs();
        vm.MemberRunsNow.Single(r => r.DescribesMember).Key.Should().Be(new MemberRunKey("TOTAL", 0),
            "the premise: the Let's run has the Get's key");
        RunFold(editor, "Property Let Total")!.IsFolded.Should().BeFalse("the premise: and is still open");

        vm.Document.UndoStack.Undo();
        Dispatcher.UIThread.RunJobs();

        RunFold(editor, "Property Get Total")!.IsFolded.Should().BeTrue("the Get's run was left folded");
        RunFold(editor, "Property Let Total")!.IsFolded.Should().BeFalse();
    }

    [AvaloniaFact]
    public void ANameRetypedKeyByKeyLeavesNoExpansionBehindForAMemberNamedLikeOneItPassedThrough()
    {
        // Retyping Go as Gap passes through G and Ga, and the run's expansion was recorded under each and kept, so a
        // member named G or Ga, or Go again, opened its run expanded at the next reload or dock move, though nobody
        // had opened it (task 4.3 review). The expansion now moves with the run, to the name it ends on.
        var vm = Open(Procedure);
        var (_, editor) = Show(vm);
        RunFold(editor, "Public Sub Go()")!.IsFolded = false;
        Dispatcher.UIThread.RunJobs();

        PutCaret(editor, LineWith(editor, "Public Sub Go()").Offset + "Public Sub Go".Length);
        Press(editor, Key.Back);
        TypeText(editor, "a");
        TypeText(editor, "p");
        RunFold(editor, "Public Sub Gap()")!.IsFolded.Should().BeFalse("the premise: the run the developer opened is open");

        var prefix = vm.Document.Text[..vm.BufferPrefixLength];
        vm.ReloadFrom(prefix, vm.BufferBody +
            "\r\nPublic Sub G()\r\nAttribute G.VB_Description = \"Gee\"\r\nEnd Sub\r\n" +
            "\r\nPublic Sub Ga()\r\nAttribute Ga.VB_Description = \"Gas\"\r\nEnd Sub\r\n" +
            "\r\nPublic Sub Go()\r\nAttribute Go.VB_Description = \"Goes\"\r\nEnd Sub\r\n");
        Dispatcher.UIThread.RunJobs();

        RunFold(editor, "Public Sub Gap()")!.IsFolded.Should().BeFalse("the renamed run is remembered under its new name");
        RunFold(editor, "Public Sub G()")!.IsFolded.Should().BeTrue();
        RunFold(editor, "Public Sub Ga()")!.IsFolded.Should().BeTrue();
        RunFold(editor, "Public Sub Go()")!.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public void ARunPastedForAPropertysLetAboveItsGetsOpensFoldedAndTheGetsKeepsItsState()
    {
        // The Let's new run takes TOTAL 0 and the Get's becomes TOTAL 1. The new run opened as the Get's had been left,
        // since what was remembered under TOTAL 0 was the Get's (task 4.3 review); the Get's run now takes its state to
        // its new key, and the new run opens folded, as any does.
        var vm = Open(
            "Option Explicit\r\n\r\n" +
            "Public Property Let Total(ByVal Value As Long)\r\n" +
            "End Property\r\n\r\n" +
            "Public Property Get Total() As Long\r\n" +
            "Attribute Total.VB_Description = \"The total\"\r\n" +
            "    Total = 1\r\n" +
            "End Property\r\n");
        var (_, editor) = Show(vm);
        RunFold(editor, "Property Get Total")!.IsFolded = false;
        Dispatcher.UIThread.RunJobs();

        vm.Document.Insert(LineWith(editor, "End Property").Offset, "Attribute Total.VB_Description = \"Sets the total\"\r\n");
        Dispatcher.UIThread.RunJobs();

        vm.MemberRunsNow.Where(r => r.DescribesMember).Select(r => r.Key)
            .Should().Equal([new MemberRunKey("TOTAL", 0), new MemberRunKey("TOTAL", 1)], "the premise: the Let's run is first");
        RunFold(editor, "Property Let Total")!.IsFolded.Should().BeTrue();
        RunFold(editor, "Property Get Total")!.IsFolded.Should().BeFalse();

        var prefix = vm.Document.Text[..vm.BufferPrefixLength];
        vm.ReloadFrom(prefix, vm.BufferBody + "\r\n");
        Dispatcher.UIThread.RunJobs();
        RunFold(editor, "Property Let Total")!.IsFolded.Should().BeTrue("and both are made again as they were");
        RunFold(editor, "Property Get Total")!.IsFolded.Should().BeFalse();
    }

    [AvaloniaFact]
    public void TwoRunsWhoseMembersTradeNamesInOneEditGroupEachKeepTheirOwnState()
    {
        // One undo step renaming Alpha to Bravo and Bravo to Alpha: each run's fold is found under the key the other
        // left. Every key left is dropped before any is recorded, or the second run would drop the key the first had
        // just recorded its state under.
        var vm = Open(
            "Option Explicit\r\n\r\n" +
            "Public Sub Alpha()\r\nAttribute Alpha.VB_Description = \"A\"\r\nEnd Sub\r\n\r\n" +
            "Public Sub Bravo()\r\nAttribute Bravo.VB_Description = \"B\"\r\nEnd Sub\r\n");
        var (_, editor) = Show(vm);
        RunFold(editor, "Public Sub Alpha()")!.IsFolded = false;
        Dispatcher.UIThread.RunJobs();

        var first = LineWith(editor, "Public Sub Alpha()").Offset + "Public Sub ".Length;
        var second = LineWith(editor, "Public Sub Bravo()").Offset + "Public Sub ".Length;
        vm.Document.BeginUpdate();
        vm.Document.Replace(second, "Bravo".Length, "Alpha");
        vm.Document.Replace(first, "Alpha".Length, "Bravo");
        vm.Document.EndUpdate();
        Dispatcher.UIThread.RunJobs();
        RunFold(editor, "Public Sub Bravo()")!.IsFolded.Should().BeFalse("the premise: the open run is Bravo's now");

        var prefix = vm.Document.Text[..vm.BufferPrefixLength];
        vm.ReloadFrom(prefix, vm.BufferBody + "\r\n");
        Dispatcher.UIThread.RunJobs();

        RunFold(editor, "Public Sub Bravo()")!.IsFolded.Should().BeFalse();
        RunFold(editor, "Public Sub Alpha()")!.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public void UncommentingADeclarationWithTheCaretAtItsEndFoldsItsRun()
    {
        // A run's fold made new with the caret at its start, the end of the declaration, is folded: the caret is before
        // the box. Taken for inside, the run opened (task 4.3 review).
        var vm = Open(Procedure.Replace("Public Sub Go()", "'Public Sub Go()"));
        var (_, editor) = Show(vm);
        RunFold(editor, "'Public Sub Go()").Should().BeNull("the premise: commented out, the run is an orphan");
        PutCaret(editor, LineWith(editor, "'Public Sub Go()").EndOffset);

        vm.Document.Remove(LineWith(editor, "'Public Sub Go()").Offset, 1);
        Dispatcher.UIThread.RunJobs();

        Caret(editor).Should().Be(LineWith(editor, "Public Sub Go()").EndOffset, "the premise: the caret is at the anchor");
        RunFold(editor, "Public Sub Go()")!.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public void ARunPastedUnderADeclarationIsFolded()
    {
        var vm = Open("Option Explicit\r\n\r\nPublic Sub Go()\r\n    Debug.Print 1\r\nEnd Sub\r\n");
        var (_, editor) = Show(vm);
        PutCaret(editor, LineWith(editor, "End Sub").Offset);

        vm.Document.Insert(LineWith(editor, "Debug.Print").Offset, "Attribute Go.VB_Description = \"Goes\"\r\n");
        Dispatcher.UIThread.RunJobs();

        RunFold(editor, "Public Sub Go()")!.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public void ARunTypedInUnderItsDeclarationIsMadeExpandedSoTheLineBeingTypedStaysOnScreen()
    {
        // A run typed by hand describes the procedure from its first '.', and is read-only from then on (design.md,
        // "Settled while building 4.1"). Folded there, the line would vanish into a box under the caret.
        var vm = Open("Option Explicit\r\n\r\nPublic Sub Halt()\r\n\r\nEnd Sub\r\n");
        var (_, editor) = Show(vm);
        PutCaret(editor, LineWith(editor, "Public Sub Halt()").EndOffset + 2);

        TypeText(editor, "Attribute Halt");
        RunFold(editor, "Public Sub Halt()").Should().BeNull("the premise: no run yet");
        TypeText(editor, ".");

        var fold = RunFold(editor, "Public Sub Halt()");
        fold.Should().NotBeNull("the run describes Halt now");
        fold!.IsFolded.Should().BeFalse();
        Caret(editor).Should().Be(LineWith(editor, "Attribute Halt.").EndOffset, "and the caret is where it was typed");
    }

    // ── The caret at a folded run ──────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void EndOnAFoldedDeclarationPutsTheCaretAtTheEndOfTheDeclaration()
    {
        // Measured, End lands after the box, at the end of the run's last line. So F9 and the status bar would name a
        // line that is not on screen, and typing there would be refused for no reason the developer can see.
        var vm = Open(Procedure);
        var (_, editor) = Show(vm);
        var declaration = LineWith(editor, "Public Sub Go()");
        PutCaret(editor, declaration.Offset);

        Press(editor, Key.End);

        Caret(editor).Should().Be(declaration.EndOffset);
        vm.CaretOffset.Should().Be(declaration.EndOffset, "what F9 reads");
        statusBar.Line.Should().Be(declaration.LineNumber);
        statusBar.Column.Should().Be(declaration.Length + 1);
        vm.ToggleBreakpoint(vm.Document.GetLineByOffset(vm.CaretOffset).LineNumber).Should().BeTrue();
        breakpoints.GetBreakpoints(vm.Identity).Should().Equal(declaration.LineNumber);
    }

    [AvaloniaFact]
    public void EndAtTheEndOfAFoldedDeclarationStaysThere()
    {
        // End from the declaration's end lands where Right from it does; only Right goes on past the run.
        var vm = Open(Procedure);
        var (_, editor) = Show(vm);
        var declaration = LineWith(editor, "Public Sub Go()");
        PutCaret(editor, declaration.EndOffset);

        Press(editor, Key.End);

        Caret(editor).Should().Be(declaration.EndOffset);
    }

    [AvaloniaFact]
    public void RightFromTheEndOfAFoldedDeclarationGoesPastTheRunToTheNextLine()
    {
        // Moved back to where it came from, Right would never get past the run.
        var vm = Open(Procedure);
        var (_, editor) = Show(vm);
        var declaration = LineWith(editor, "Public Sub Go()");
        var body = LineWith(editor, "Debug.Print 1");

        foreach (var modifiers in new[] { KeyModifiers.None, KeyModifiers.Control })
        {
            PutCaret(editor, declaration.EndOffset);
            Press(editor, Key.Right, modifiers);
            Caret(editor).Should().Be(body.Offset, $"Right with {modifiers}");
        }
        RunFold(editor, "Public Sub Go()")!.IsFolded.Should().BeTrue("and stepping over the run left it folded");
    }

    [AvaloniaFact]
    public void RightFromTheEndOfAFoldedDeclarationWhoseRunEndsTheFileStaysWhereItIs()
    {
        // No line after the run to go past it to, and the stop after the box is inside it: the end of the file is the
        // run's last character, so the caret keeps to the declaration's end.
        var vm = Open("Option Explicit\r\n\r\nPublic Sub Go()\r\nAttribute Go.VB_Description = \"Goes\"");
        var (_, editor) = Show(vm);
        var declaration = LineWith(editor, "Public Sub Go()");
        RunFold(editor, "Public Sub Go()")!.EndOffset.Should().Be(vm.Document.TextLength, "the premise: the run ends the file");
        PutCaret(editor, declaration.EndOffset);

        Press(editor, Key.Right);

        Caret(editor).Should().Be(declaration.EndOffset);
    }

    [AvaloniaFact]
    public void ShiftRightFromTheEndOfAFoldedDeclarationSelectsTheRunWithTheLineBreak()
    {
        // By character and by word: each is a step of its own to the caret navigation.
        var vm = Open(Procedure);
        var (_, editor) = Show(vm);
        var declaration = LineWith(editor, "Public Sub Go()");

        foreach (var modifiers in new[] { KeyModifiers.Shift, KeyModifiers.Control | KeyModifiers.Shift })
        {
            editor.TextArea.ClearSelection();
            PutCaret(editor, declaration.EndOffset);

            Press(editor, Key.Right, modifiers);

            var selection = editor.TextArea.Selection.SurroundingSegment!;
            (selection.Offset, selection.EndOffset).Should().Be(
                (declaration.EndOffset, LineWith(editor, "Debug.Print 1").Offset), $"Right with {modifiers}");
        }
    }

    [AvaloniaFact]
    public void CmdRightAndCmdShiftRightOnMacOSLeaveTheCaretAtTheEndOfAFoldedDeclaration()
    {
        // macOS binds Cmd+Right to the end of the line and Cmd+Shift+Right to selecting there: End and Shift+End on a
        // Mac, read from Avalonia.Native's keymap, which AvaloniaEdit builds its bindings from. Taken for steps by
        // their key, both went past the run from the declaration's end, where End stays put (task 4.3 review). This
        // harness has Windows' keymap, so the Mac's two bindings are added as its keymap adds them.
        var vm = Open(Procedure);
        var (_, editor) = Show(vm);
        var bindings = editor.TextArea.DefaultInputHandler.CaretNavigation.KeyBindings;
        bindings.Add(new KeyBinding { Command = EditingCommands.MoveToLineEnd, Gesture = new KeyGesture(Key.Right, KeyModifiers.Meta) });
        bindings.Add(new KeyBinding
        {
            Command = EditingCommands.SelectToLineEnd, Gesture = new KeyGesture(Key.Right, KeyModifiers.Meta | KeyModifiers.Shift),
        });
        var declaration = LineWith(editor, "Public Sub Go()");
        PutCaret(editor, declaration.Offset);
        Press(editor, Key.Right, KeyModifiers.Meta);
        Caret(editor).Should().Be(declaration.EndOffset, "the premise: Cmd+Right is End here");

        Press(editor, Key.Right, KeyModifiers.Meta);
        Caret(editor).Should().Be(declaration.EndOffset, "End from the end of the declaration stays there");

        Press(editor, Key.Right, KeyModifiers.Meta | KeyModifiers.Shift);
        Caret(editor).Should().Be(declaration.EndOffset);
        editor.TextArea.Selection.IsEmpty.Should().BeTrue("and Shift+End from there selects nothing of the run");
    }

    [AvaloniaFact]
    public void ARightTakenBeforeTheTextAreaMovesTheCaretIsNoStepForTheNextLandingAtTheRunsEnd()
    {
        // A step that moves nothing raises no caret move, and noted until the caret next moved, it sent the next landing
        // at the run's end past the run, whatever made that landing (task 4.3 review). Here the key is taken after the
        // window has noted the step and before the text area's caret navigation sees it: by a class handler, which
        // Avalonia runs ahead of an element's own handlers as the key bubbles, where the caret navigation's is. A
        // tunnel handler added here would run ahead of the window's, since a tunnel visits an element's handlers last
        // added first, and the window would note nothing.
        var vm = Open(Procedure);
        var (_, editor) = Show(vm);
        var declaration = LineWith(editor, "Public Sub Go()");
        using var taken = InputElement.KeyDownEvent.AddClassHandler<AvaloniaEdit.Editing.TextArea>((area, e) =>
        {
            if (ReferenceEquals(area, editor.TextArea) && e.Key == Key.Right) e.Handled = true;
        }, RoutingStrategies.Bubble);
        PutCaret(editor, declaration.EndOffset);

        Press(editor, Key.Right);
        Caret(editor).Should().Be(declaration.EndOffset, "the premise: nothing moved");
        PutCaret(editor, LineWith(editor, "Attribute Go.VB_UserMemId").EndOffset);

        Caret(editor).Should().Be(declaration.EndOffset);
    }

    [AvaloniaFact]
    public void AfterRightPastARunAPlacementAtTheRunsEndThatNoKeyMadeGoesToTheEndOfTheDeclaration()
    {
        // The step ends with its key. Navigation, a search or automation putting the caret at the end of the run next
        // is a landing like a click, and goes to the anchor.
        var vm = Open(Procedure);
        var (_, editor) = Show(vm);
        var declaration = LineWith(editor, "Public Sub Go()");
        PutCaret(editor, declaration.EndOffset);
        Press(editor, Key.Right);
        Caret(editor).Should().Be(LineWith(editor, "Debug.Print 1").Offset, "the premise: the step went past the run");

        PutCaret(editor, LineWith(editor, "Attribute Go.VB_UserMemId").EndOffset);

        Caret(editor).Should().Be(declaration.EndOffset);
    }

    [AvaloniaFact]
    public void AContinuedAttributeLineUnderAContinuedDeclarationFoldsFromTheDeclarationsLastLineToTheContinuationsEnd()
    {
        // A continued declaration, as MemberRunsTests reads one, over p11a's continued attribute line: the anchor is the
        // end of the declaration's last physical line, and the fold ends where the run's last logical line does.
        var vm = Open(
            "Option Explicit\r\n\r\n" +
            "Public Function Total( _\r\n" +
            "    ByVal Rate As Double) As Double\r\n" +
            "Attribute Total.VB_Description = _\r\n" +
            "    \"Totals\"\r\n" +
            "    Total = 1\r\n" +
            "End Function\r\n");
        var (_, editor) = Show(vm);
        var declaration = LineWith(editor, "    ByVal Rate As Double) As Double");
        var fold = RunFold(editor, "    ByVal Rate As Double) As Double");

        fold.Should().NotBeNull();
        (fold!.StartOffset, fold.EndOffset).Should().Be((declaration.EndOffset, LineWith(editor, "    \"Totals\"").EndOffset));
        fold.IsFolded.Should().BeTrue();
        PutCaret(editor, declaration.Offset);
        Press(editor, Key.End);
        Caret(editor).Should().Be(declaration.EndOffset);
        Press(editor, Key.Right);
        Caret(editor).Should().Be(LineWith(editor, "    Total = 1").Offset);
    }

    [AvaloniaFact]
    public void DownOntoAFoldedRowFromALongerLineAboveLandsAtTheEndOfTheDeclarationAndDownAgainOnTheBody()
    {
        var vm = Open(Procedure.Replace("Option Explicit\r\n\r\n",
            "Option Explicit\r\n' a comment a good deal longer than the declaration below it\r\n"));
        var (_, editor) = Show(vm);
        PutCaret(editor, LineWith(editor, "' a comment").EndOffset);

        Press(editor, Key.Down);
        Caret(editor).Should().Be(LineWith(editor, "Public Sub Go()").EndOffset);
        Press(editor, Key.Down);
        editor.Document.GetLineByOffset(Caret(editor)).LineNumber.Should().Be(LineWith(editor, "Debug.Print 1").LineNumber);
    }

    [AvaloniaFact]
    public void CtrlEndInAFileThatARunEndsLandsAtTheEndOfTheDeclaration()
    {
        // The end of the file is the run's last character, after the box.
        var vm = Open("Option Explicit\r\n\r\nPublic Total As Long\r\nAttribute Total.VB_VarDescription = \"The total\"");
        var (_, editor) = Show(vm);
        RunFold(editor, "Public Total As Long")!.EndOffset.Should().Be(vm.Document.TextLength, "the premise: the run ends the file");
        PutCaret(editor, LineWith(editor, "Option Explicit").Offset);

        Press(editor, Key.End, KeyModifiers.Control);

        Caret(editor).Should().Be(LineWith(editor, "Public Total As Long").EndOffset);
    }

    [AvaloniaFact]
    public async Task AServersFoldOfTheAttributeLinesThemselvesNestsInsideTheRunsFoldAndTheCaretRuleStillHolds()
    {
        // A server that folds the run's own lines, from the first's start to the last's end: inside the run's fold, so
        // the merge keeps both, and the caret keeps to the run's box.
        folds = model =>
        {
            var first = model.Document.GetLineByOffset(model.Document.Text.IndexOf("Attribute Go.VB_Description", StringComparison.Ordinal));
            var last = model.Document.GetLineByOffset(model.Document.Text.IndexOf("Attribute Go.VB_UserMemId", StringComparison.Ordinal));
            return [new FoldingRange(first.LineNumber - 1, last.LineNumber - 1)];
        };
        var vm = Open(Procedure);
        var (_, editor) = Show(vm);
        ServerStateChanged();
        await SettleFolding();

        var declaration = LineWith(editor, "Public Sub Go()");
        var first = LineWith(editor, "Attribute Go.VB_Description");
        var last = LineWith(editor, "Attribute Go.VB_UserMemId");
        Folding(editor).AllFoldings.Select(f => (f.StartOffset, f.EndOffset, f.IsFolded)).Should().Equal(
            [(declaration.EndOffset, last.EndOffset, true), (first.Offset, last.EndOffset, false)]);
        PutCaret(editor, declaration.Offset);
        Press(editor, Key.End);
        Caret(editor).Should().Be(declaration.EndOffset);
        Press(editor, Key.Right);
        Caret(editor).Should().Be(LineWith(editor, "Debug.Print 1").Offset);
    }

    [AvaloniaFact]
    public void LeftFromTheLineAfterAFoldedRunLandsAtTheEndOfTheDeclaration()
    {
        var vm = Open(Procedure);
        var (_, editor) = Show(vm);
        var declaration = LineWith(editor, "Public Sub Go()");

        foreach (var modifiers in new[] { KeyModifiers.None, KeyModifiers.Control })
        {
            PutCaret(editor, LineWith(editor, "Debug.Print 1").Offset);
            Press(editor, Key.Left, modifiers);
            Caret(editor).Should().Be(declaration.EndOffset, $"Left with {modifiers}");
        }
    }

    [AvaloniaFact]
    public void UpOntoAFoldedRowFromALongerLineLandsAtTheEndOfTheDeclaration()
    {
        var vm = Open(Procedure.Replace("    Debug.Print 1", "    Debug.Print \"a line longer than the declaration\""));
        var (_, editor) = Show(vm);
        PutCaret(editor, LineWith(editor, "Debug.Print \"a line").EndOffset);

        Press(editor, Key.Up);

        Caret(editor).Should().Be(LineWith(editor, "Public Sub Go()").EndOffset);
    }

    [AvaloniaFact]
    public void AClickPastTheEndOfAFoldedRowPutsTheCaretAtTheEndOfTheDeclaration()
    {
        // From the declaration's end too: measured, a click there lands exactly as Right does, so the rule goes by the
        // key, and a click is not a step.
        var vm = Open(Procedure);
        var (window, editor) = Show(vm);
        var declaration = LineWith(editor, "Public Sub Go()");
        var textView = editor.TextArea.TextView;

        foreach (var from in new[] { declaration.Offset, declaration.EndOffset })
        {
            PutCaret(editor, from);
            textView.EnsureVisualLines();
            var row = textView.GetOrConstructVisualLine(declaration);
            var at = textView.TranslatePoint(
                new Point(textView.Bounds.Width - 20, row.VisualTop - textView.VerticalOffset + row.Height / 2), window)!.Value;
            var pointer = new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            editor.TextArea.RaiseEvent(new PointerPressedEventArgs(
                editor.TextArea, pointer, window, at, timestamp: 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None));
            editor.TextArea.RaiseEvent(new PointerReleasedEventArgs(
                editor.TextArea, pointer, window, at, timestamp: 1,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
                KeyModifiers.None, MouseButton.Left));
            Dispatcher.UIThread.RunJobs();

            Caret(editor).Should().Be(declaration.EndOffset, $"a click from {from}");
        }
    }

    [AvaloniaFact]
    public void TypingAtTheEndOfAFoldedDeclarationLandsOnTheDeclarationAndLeavesTheRunFolded()
    {
        var vm = Open(Procedure);
        var (_, editor) = Show(vm);
        PutCaret(editor, LineWith(editor, "Public Sub Go()").Offset);
        Press(editor, Key.End);

        TypeText(editor, " ' Starts");

        editor.Document.GetText(LineWith(editor, "Public Sub Go()")).Should().Be("Public Sub Go() ' Starts");
        var fold = RunFold(editor, "Public Sub Go()");
        fold.Should().NotBeNull("the fold still starts at the end of the declaration");
        fold!.IsFolded.Should().BeTrue();
        vm.Document.Text.Should().Contain("' Starts\r\nAttribute Go.VB_Description", "and the run is where it was");
    }

    [AvaloniaFact]
    public void WithTheRunExpandedEndAndRightAreTheLibrarysOwn()
    {
        // The control: nothing is moved when the run is shown, so the caret can go into the run's lines, which are
        // read-only and can be read and selected.
        var vm = Open(Procedure);
        var (_, editor) = Show(vm);
        RunFold(editor, "Public Sub Go()")!.IsFolded = false;
        var declaration = LineWith(editor, "Public Sub Go()");

        PutCaret(editor, declaration.Offset);
        Press(editor, Key.End);
        Caret(editor).Should().Be(declaration.EndOffset);
        Press(editor, Key.Right);
        Caret(editor).Should().Be(LineWith(editor, "Attribute Go.VB_Description").Offset);

        var last = LineWith(editor, "Attribute Go.VB_UserMemId");
        PutCaret(editor, last.Offset);
        Press(editor, Key.End);
        Caret(editor).Should().Be(last.EndOffset, "the end of the run's last line, shown, is a place the caret may be");
    }

    [AvaloniaFact]
    public void TheCaretAtTheEndOfAFoldedHeaderIsLeftThere()
    {
        // The caret rule is the runs'. The header's folded row has the same two stops, and the one after its box is the
        // end of the header's last line; nothing moves a caret off it, as before task 4.3.
        var vm = Open(Procedure, ModuleKind.ClassModule);
        var (_, editor) = Show(vm);
        var header = Folding(editor).GetFoldingsAt(0).Single();
        header.IsFolded.Should().BeTrue("the premise: a class's header is folded");

        PutCaret(editor, header.EndOffset);

        Caret(editor).Should().Be(header.EndOffset);
        header.IsFolded.Should().BeTrue();
    }

    // ── Read-only, folded and expanded (task 4.2) ──────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void AFoldedRunAndAnExpandedOneAreBothReadOnly()
    {
        // #273 task 4.2's read-only half, once 4.3 folds the runs: the section provider refuses the run either way, and
        // the line break it hangs from, while the end of the declaration takes text. The greying half is
        // ReadOnlyGreyingTests.AFoldedRunIsDrawnAsABoxInTheReadOnlyColourAndAnExpandedOneIsGreyedLineByLine.
        var vm = Open(Procedure);
        var (_, editor) = Show(vm);
        var declaration = LineWith(editor, "Public Sub Go()");
        var first = LineWith(editor, "Attribute Go.VB_Description");
        var last = LineWith(editor, "Attribute Go.VB_UserMemId");
        var sections = editor.TextArea.ReadOnlySectionProvider;
        var fold = RunFold(editor, "Public Sub Go()")!;

        foreach (var folded in new[] { true, false })
        {
            fold.IsFolded = folded;
            Dispatcher.UIThread.RunJobs();
            var state = folded ? "folded" : "expanded";
            sections.CanInsert(declaration.EndOffset).Should().BeTrue($"{state}, the declaration's end takes text");
            sections.CanInsert(first.Offset).Should().BeFalse($"{state}, not the run's first character");
            sections.CanInsert(last.EndOffset).Should().BeFalse($"{state}, nor its last line's end");
            sections.GetDeletableSegments(new TextSegment { StartOffset = declaration.EndOffset, EndOffset = last.EndOffset })
                .Sum(s => s.Length).Should().Be(0, $"{state}, nothing of the run or the break it hangs from can be deleted");
            vm.IsReadOnlyRegion(first.Offset + 3, 2).Should().BeTrue($"{state}, and every other writer is refused it too");
        }

        var before = vm.Document.Text;
        fold.IsFolded = true;
        PutCaret(editor, first.Offset + 10);
        fold.IsFolded.Should().BeFalse("the premise: the caret moved into the run opened it");
        TypeText(editor, "X");
        Press(editor, Key.Back);
        Press(editor, Key.Delete);
        vm.Document.Text.Should().Be(before, "typed into, the expanded run is unchanged");
    }

    // ── What reading the runs costs, and when it is read again ─────────────────────────────────────────────

    [AvaloniaFact]
    public void AFormatOfManyLinesReadsTheRunsOnceForItsWholeEditGroupAndKeepsEachRunsState()
    {
        // A server's formatting answer is applied as one change per line it rewrites, all in one edit group. Recorded at
        // every change, the folds read the runs at every change, a whole-buffer scan each: measured headlessly, 8000 line
        // edits of a 165,230-character module took 99 seconds (task 4.3 review). Counted, not timed, so a loaded machine
        // can neither pass it nor fail it. Every other body line is already indented, so the reduction cannot join two
        // changed lines into one change.
        var code = new StringBuilder("Option Explicit\r\n\r\n");
        for (var p = 0; p < 20; p++)
        {
            code.Append($"Public Sub P{p}()\r\nAttribute P{p}.VB_Description = \"Does {p}\"\r\n");
            for (var i = 0; i < 10; i++)
                code.Append("  Debug.Print 1\r\n    Debug.Print 2\r\n");
            code.Append("End Sub\r\n\r\n");
        }
        var vm = Open(code.ToString());
        var (_, editor) = Show(vm);
        RunFold(editor, "Public Sub P7()")!.IsFolded = false;
        Dispatcher.UIThread.RunJobs();
        var scans = vm.MemberRunScans;

        var reduction = vm.ApplyFormatting([new TextEdit(
            new HexIDE.Lsp.Messages.Range(new Position(0, 0), new Position(vm.Document.LineCount, 0)),
            vm.Document.Text.Replace("  Debug.Print 1", "    Debug.Print 1"))]);
        Dispatcher.UIThread.RunJobs();

        reduction.Changes.Should().HaveCount(200, "the premise: one change per body line, in one group");
        (vm.MemberRunScans - scans).Should().BeLessThanOrEqualTo(2,
            "the runs are read as the group begins and once it has changed the text, not at each of its changes");
        RunFold(editor, "Public Sub P7()")!.IsFolded.Should().BeFalse("and what was recorded still holds");
        RunFold(editor, "Public Sub P8()")!.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public void TheRunsFollowAReloadThatMovesTheSplitWithoutChangingTheText()
    {
        // A reload of the buffer's own text returns before it touches the document, so the version stays and only the
        // prefix's length says the runs were read against another split. Here the prefix takes in the declaration, and
        // the run below it hangs from no line of code.
        var vm = Open(Procedure);
        vm.MemberRunsNow.Should().ContainSingle(r => r.DescribesMember, "the premise");
        var text = vm.Document.Text;
        var split = text.IndexOf("Attribute Go.VB_Description", StringComparison.Ordinal);

        vm.ReloadFrom(text[..split], text[split..]);

        vm.Document.Text.Should().Be(text, "the premise: the text is the same");
        vm.MemberRunsNow.Should().NotContain(r => r.DescribesMember);
    }
}
