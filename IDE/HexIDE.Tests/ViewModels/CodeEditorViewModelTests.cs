using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.ViewModels;

public class CodeEditorViewModelTests : IDisposable
{
    private readonly IWindowManager _windowManager = Substitute.For<IWindowManager>();
    private readonly IEditorService _editorService = Substitute.For<IEditorService>();
    private readonly IProjectService _projectService = Substitute.For<IProjectService>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly ILspClient _lspClient = Substitute.For<ILspClient>();
    private readonly ISettingsService _settingsService = Substitute.For<ISettingsService>();
    private readonly IStatusBarService _statusBarService = Substitute.For<IStatusBarService>();
    private readonly IBookmarkService _bookmarkService = Substitute.For<IBookmarkService>();
    private readonly HexIDE.Debugging.IBreakpointService _breakpointService = Substitute.For<HexIDE.Debugging.IBreakpointService>();
    private readonly HexIDE.Runtime.Debugging.IDebugController _debugController = Substitute.For<HexIDE.Runtime.Debugging.IDebugController>();
    private readonly HexIDE.Debugging.IRunScope _runScope = Substitute.For<HexIDE.Debugging.IRunScope>();
    private readonly ILocalizationService _localization = Substitute.For<ILocalizationService>();
    private CodeEditorViewModel? _sut;

    public CodeEditorViewModelTests()
    {
        _localization.GetString("Str.Document.CodeSuffix").Returns("Code");

        _eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>())
            .Returns(Substitute.For<IDisposable>());
        _eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>())
            .Returns(Substitute.For<IDisposable>());
        _eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>())
            .Returns(Substitute.For<IDisposable>());
    }

    private CodeEditorViewModel CreateSut()
    {
        _sut = new CodeEditorViewModel(
            _windowManager, _editorService, _projectService, _eventBus, _lspClient, _settingsService, _statusBarService, _bookmarkService, _breakpointService, _debugController, _runScope, _localization);
        return _sut;
    }

    public void Dispose()
    {
        _sut?.Dispose();
    }

    // ── Edit-while-running reset prompt (VB6-faithful E&C affordance) ──

    [AvaloniaFact]
    public async Task ConfirmResetWhileRunningAsync_Yes_RequestsProjectEnd()
    {
        _windowManager.MessageBox(Arg.Any<string>(), Arg.Any<string>(), MessageBoxButtons.YesNo, Arg.Any<MessageBoxIcon>())
            .Returns(MessageBoxResult.Yes);

        var reset = await CreateSut().ConfirmResetWhileRunningAsync();

        reset.Should().BeTrue();
        _eventBus.Received(1).Publish(Arg.Any<EndProjectRequestedEvent>());
    }

    [AvaloniaFact]
    public async Task ConfirmResetWhileRunningAsync_No_KeepsRunning()
    {
        _windowManager.MessageBox(Arg.Any<string>(), Arg.Any<string>(), MessageBoxButtons.YesNo, Arg.Any<MessageBoxIcon>())
            .Returns(MessageBoxResult.No);

        var reset = await CreateSut().ConfirmResetWhileRunningAsync();

        reset.Should().BeFalse();
        _eventBus.DidNotReceive().Publish(Arg.Any<EndProjectRequestedEvent>());
    }

    [AvaloniaFact]
    public void IsProjectRunning_ReflectsTheDebugController()
    {
        _debugController.IsSessionActive.Returns(true);
        CreateSut().IsProjectRunning.Should().BeTrue();
    }

    // ── Initialization — Form ────────────────────────────────────────

    [AvaloniaFact]
    public void Initialize_Form_SetsFormDefinition()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);

        vm.FormDefinition.Should().BeSameAs(form);
    }

    [AvaloniaFact]
    public void Initialize_Form_SetsDocumentTextFromFormCode()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);

        vm.Document.Text.Should().Be(form.Code);
    }

    [AvaloniaFact]
    public void Initialize_Form_ObjectNamesContainsGeneral()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);

        vm.ObjectNames.Should().Contain("(General)");
    }

    [AvaloniaFact]
    public void Initialize_Form_TitleContainsFormAndProjectName()
    {
        var project = TestHelpers.CreateProject("MyProject");
        var form = TestHelpers.CreateForm(owner: project, name: "MainForm");
        var vm = CreateSut().Initialize(form);

        vm.Title.Should().Contain("MyProject");
        vm.Title.Should().Contain("MainForm");
    }

    // ── Initialization — Module ──────────────────────────────────────

    [AvaloniaFact]
    public void Initialize_Module_SetsModuleDefinition()
    {
        var module = TestHelpers.CreateModule(name: "Module1");
        var vm = CreateSut().Initialize(module);

        vm.ModuleDefinition.Should().BeSameAs(module);
    }

    [AvaloniaFact]
    public void Initialize_Module_SetsDocumentTextToTheWholeFile()
    {
        // Renamed and retargeted rather than adjusted. It asserted buffer == Code, which was the contract
        // #273 task 3.2 exists to replace: the buffer is now the whole file, so that one line number means
        // the same thing to the editor, a language server, the interpreter and the debugger. A module
        // HexIDE created has no preserved header, so its prefix is the canonical literal.
        var module = TestHelpers.CreateModule(name: "Module1");
        var vm = CreateSut().Initialize(module);

        vm.Document.Text.Should().Be("Attribute VB_Name = \"Module1\"\r\n" + module.Code);
        vm.BufferBody.Should().Be(module.Code, "the split has to invert the composition exactly");
    }

    [AvaloniaFact]
    public void Initialize_Form_ShowsItsDesignerHalfInFrontOfItsCode()
    {
        // The form half of the same rule. A form HexIDE created has no designer text, so nothing is put in
        // front of it -- there is no file yet for the buffer to differ from.
        var form = TestHelpers.CreateForm(name: "Form1");
        form.RecordDesignerText("VERSION 5.00\r\nBegin VB.Form Form1 \r\nEnd\r\n");
        var vm = CreateSut().Initialize(form);

        vm.Document.Text.Should().StartWith("VERSION 5.00").And.EndWith(form.Code);
        vm.BufferBody.Should().Be(form.Code);
    }

    [AvaloniaFact]
    public void AFormWithNoFileShowsItsCodeAlone()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);

        vm.Document.Text.Should().Be(form.Code);
        vm.BufferBody.Should().Be(form.Code);
    }

    [AvaloniaFact]
    public void AModuleWhoseHeaderWasNotRecognisedGetsNoPrefix()
    {
        // hexide-io/HexIDE#472: recognition is positional and tests only line 0, so a leading blank line
        // is enough to leave the WHOLE file in Code with an empty recorded header. Prepending the canonical
        // literal on top of that would show the developer two headers. Null means "never read from disk";
        // empty means "read, and nothing was split off" -- ToFileContent conflates them with IsNullOrEmpty,
        // which is the defect, and the buffer must not.
        var module = TestHelpers.CreateModule(name: "Module1");
        module.RecordOriginalHeader("");
        module.UpdateCode("\r\nAttribute VB_Name = \"Module1\"\r\nOption Explicit\r\n");

        var vm = CreateSut().Initialize(module);

        vm.Document.Text.Should().Be(module.Code);
        vm.BufferBody.Should().Be(module.Code);
    }

    [AvaloniaFact]
    public void Initialize_Module_ObjectNamesContainsOnlyGeneral()
    {
        var module = TestHelpers.CreateModule(name: "Module1");
        var vm = CreateSut().Initialize(module);

        vm.ObjectNames.Should().ContainSingle()
            .Which.Should().Be("(General)");
    }

    [AvaloniaFact]
    public void Initialize_Module_TitleContainsModuleAndProjectName()
    {
        var project = TestHelpers.CreateProject("MyProject");
        var module = TestHelpers.CreateModule(owner: project, name: "Utilities");
        var vm = CreateSut().Initialize(module);

        vm.Title.Should().Contain("MyProject");
        vm.Title.Should().Contain("Utilities");
    }

    // ── Undo history after a load (#673) ─────────────────────────────

    private const string SomeCode = "Private Sub Foo()\r\n    Debug.Print 1\r\nEnd Sub\r\n";

    [AvaloniaFact]
    public void OpeningAModuleLeavesNothingToUndo()
    {
        var module = TestHelpers.CreateModule(name: "Module1");
        module.UpdateCode(SomeCode);
        var vm = CreateSut().Initialize(module);

        vm.Document.UndoStack.CanUndo.Should().BeFalse("loading the code is not an edit the developer can undo");
    }

    [AvaloniaFact]
    public void OpeningAFormLeavesNothingToUndo()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        form.UpdateCode(SomeCode);
        var vm = CreateSut().Initialize(form);

        vm.Document.UndoStack.CanUndo.Should().BeFalse("loading the code is not an edit the developer can undo");
    }

    [AvaloniaFact]
    public void UndoingOneEditMoreThanWasMadeCannotEmptyTheWindow()
    {
        // The reported path: one real edit, then two Undos. The second used to undo the load itself and leave
        // the buffer empty, which the next save wrote to disk.
        var module = TestHelpers.CreateModule(name: "Module1");
        module.UpdateCode(SomeCode);
        var vm = CreateSut().Initialize(module);

        vm.Document.Insert(0, "x");
        vm.Document.UndoStack.Undo();
        if (vm.Document.UndoStack.CanUndo) vm.Document.UndoStack.Undo();

        // BufferBody, not Document.Text: since #273 task 3.2 the buffer holds the whole file, so a module's
        // own `Attribute VB_Name` header sits above the code. What this test is about is that the code
        // survived, and the code is the body.
        vm.BufferBody.Should().Be(SomeCode);
        vm.Document.TextLength.Should().BeGreaterThan(0, "undoing past the load must not empty the window");
    }

    [AvaloniaFact]
    public void AReloadFromDiskLeavesNothingToUndo()
    {
        var module = TestHelpers.CreateModule(name: "Module1");
        module.UpdateCode(SomeCode);
        var vm = CreateSut().Initialize(module);
        vm.Document.Insert(0, "x");

        // A module has no designer prefix, so the reload is body-only. ReloadFrom takes both halves since
        // #273 task 3.2, because a form's buffer holds its designer block above the code.
        vm.ReloadFrom("", "Private Sub Bar()\r\nEnd Sub\r\n");

        vm.Document.UndoStack.CanUndo.Should().BeFalse("undoing across a reload would put back text the file no longer has");
    }

    // ── Document URI ─────────────────────────────────────────────────

    [AvaloniaFact]
    public void GetDocumentUri_AFormWithNoFile_IsNamedUntitledUnderItsProject()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);

        vm.GetDocumentUriPublic().Should().Be("untitled:TestProject/Form1.frm");
    }

    [AvaloniaFact]
    public void GetDocumentUri_AModuleWithNoFile_IsNamedUntitledUnderItsProject()
    {
        var module = TestHelpers.CreateModule(name: "Module1");
        var vm = CreateSut().Initialize(module);

        vm.GetDocumentUriPublic().Should().Be("untitled:TestProject/Module1.bas");
    }

    // ── LSP open on init ─────────────────────────────────────────────

    [AvaloniaFact]
    public void Initialize_Form_WhenLspRunning_CallsOpenDocument()
    {
        _lspClient.IsRunning.Returns(true);
        var form = TestHelpers.CreateForm(name: "Form1");

        CreateSut().Initialize(form);

        _lspClient.Received(1).OpenDocumentAsync(
            "untitled:TestProject/Form1.frm",
            form.Code,
            true,
            Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public void Initialize_Form_TellsTheClientEvenWhenNoServerIsRunning()
    {
        // This used to assert the opposite, mirroring a gate in the view-model rather than a requirement.
        // The gate was a defect: the client tracks a document BEFORE checking whether a server is up, and
        // replays every tracked document after a (re)connect — so skipping the call meant a file opened
        // while the server was down was never tracked and never replayed once it came back. It also has to
        // go for lazy start to work at all: opening a document is what starts the server claiming its
        // language, so a gate on "is one running" leaves nothing to do the starting.
        _lspClient.IsRunning.Returns(false);
        var form = TestHelpers.CreateForm(name: "Form1");

        CreateSut().Initialize(form);

        _lspClient.Received(1).OpenDocumentAsync(
            "untitled:TestProject/Form1.frm", form.Code, true, Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public void Initialize_Module_WhenLspRunning_CallsOpenDocument()
    {
        _lspClient.IsRunning.Returns(true);
        var module = TestHelpers.CreateModule(name: "Module1");

        CreateSut().Initialize(module);

        _lspClient.Received(1).OpenDocumentAsync(
            "untitled:TestProject/Module1.bas",
            // The whole file since #273 task 3.2 -- see the sibling assertion below.
            "Attribute VB_Name = \"Module1\"\r\n" + module.Code,
            true,
            Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public void Initialize_Module_TellsTheClientEvenWhenNoServerIsRunning()
    {
        // This used to assert the opposite, mirroring a gate in the view-model rather than a requirement.
        // The gate was a defect: the client tracks a document BEFORE checking whether a server is up, and
        // replays every tracked document after a (re)connect — so skipping the call meant a file opened
        // while the server was down was never tracked and never replayed once it came back. It also has to
        // go for lazy start to work at all: opening a document is what starts the server claiming its
        // language, so a gate on "is one running" leaves nothing to do the starting.
        _lspClient.IsRunning.Returns(false);
        var module = TestHelpers.CreateModule(name: "Module1");

        CreateSut().Initialize(module);

        // The WHOLE file, which is the point of the change rather than a side effect of it: a server that
        // reads a document from disk and a server that is handed the buffer must see the same text, or the
        // positions they report do not mean the same thing.
        _lspClient.Received(1).OpenDocumentAsync(
            "untitled:TestProject/Module1.bas",
            "Attribute VB_Name = \"Module1\"\r\n" + module.Code, true, Arg.Any<CancellationToken>());
    }

    // ── Dirty detection follows the split (#273 task 3.2a) ───────────

    [AvaloniaFact]
    public void AnUneditedOpenDocumentIsACleanReloadRatherThanAConflict()
    {
        // THE reason 3.2a ships with 3.2 rather than after it. Once the buffer is the whole file it never
        // equals Code again, so a detector still comparing the raw buffer reports EVERY open document as
        // edited. And Conflict is not merely "skip the reload" -- it queues the ConflictGate and raises a
        // dialog, so every external change to any open file would prompt, and the silent CleanReload the
        // file-watcher capability requires would never be reached once.
        var module = TestHelpers.CreateModule(name: "Module1");
        module.UpdateCode("Option Explicit\r\n");
        var vm = CreateSut().Initialize(module);

        var detector = new DirtyDetector(new FileBaselineStore(), Substitute.For<IProjectService>());

        detector.Classify(new WatchedFileTarget("Module1.bas", null, module, vm, null))
            .Should().Be(ReloadDecision.CleanReload);
    }

    [AvaloniaFact]
    public void AnEditedOpenDocumentIsStillAConflict()
    {
        // The other side, and the one a careless split would break in the opposite direction: comparing
        // the wrong pair could just as easily make everything look clean, and a CleanReload over unsaved
        // work discards it silently.
        var module = TestHelpers.CreateModule(name: "Module1");
        module.UpdateCode("Option Explicit\r\n");
        var vm = CreateSut().Initialize(module);

        vm.Document.Text += "Dim x As Long\r\n";

        var detector = new DirtyDetector(new FileBaselineStore(), Substitute.For<IProjectService>());

        detector.Classify(new WatchedFileTarget("Module1.bas", null, module, vm, null))
            .Should().Be(ReloadDecision.Conflict);
    }

    [AvaloniaFact]
    public void EditingTheBodyFlushesTheBodyAloneBackToTheModel()
    {
        // The composition has to be invertible or the header ends up in Code, where the interpreter
        // compiles it as VB -- and it would be written back into the file below its real header on the
        // next save, doubling it.
        var module = TestHelpers.CreateModule(name: "Module1");
        module.UpdateCode("Option Explicit\r\n");
        var vm = CreateSut().Initialize(module);

        vm.Document.Text += "Dim x As Long\r\n";
        vm.Dispose();

        module.Code.Should().Be("Option Explicit\r\nDim x As Long\r\n");
        module.Code.Should().NotContain("Attribute VB_Name");
    }

    [AvaloniaFact]
    public void ReplacingTheBodyLeavesTheHeaderStanding()
    {
        // The write half of the same contract. Automation and the add-in surface both assign a bare body;
        // before ReplaceBody they assigned it straight over Document.Text, which since 3.2 would destroy
        // the header and leave the next flush splitting into the body.
        var module = TestHelpers.CreateModule(name: "Module1");
        module.UpdateCode("Option Explicit\r\n");
        var vm = CreateSut().Initialize(module);

        vm.ReplaceBody("Public Sub Main()\r\nEnd Sub\r\n");

        vm.Document.Text.Should().Be(
            "Attribute VB_Name = \"Module1\"\r\nPublic Sub Main()\r\nEnd Sub\r\n");
        vm.BufferBody.Should().Be("Public Sub Main()\r\nEnd Sub\r\n");
    }

    // -- The header is refreshed in place (#273 tasks 3.3/3.3a) -------

    [AvaloniaFact]
    public void ARefreshedHeaderReplacesTheHeaderAndLeavesTheBodyWhereItWas()
    {
        var module = TestHelpers.CreateModule(name: "Module1");
        module.UpdateCode("Option Explicit\r\nDim x As Long\r\n");
        var vm = CreateSut().Initialize(module);

        vm.RefreshPrefix("Attribute VB_Name = \"RenamedToSomethingMuchLonger\"\r\n");

        vm.Document.Text.Should().Be(
            "Attribute VB_Name = \"RenamedToSomethingMuchLonger\"\r\nOption Explicit\r\nDim x As Long\r\n");
        vm.BufferBody.Should().Be("Option Explicit\r\nDim x As Long\r\n");
    }

    [AvaloniaFact]
    public void ARefreshedHeaderCarriesTheCaretWithTheLineItWasOn()
    {
        // The whole reason this replaces the header REGION rather than assigning Document.Text: a
        // whole-document assignment collapses every anchor AvaloniaEdit holds -- the caret, the selection,
        // the LSP marker segments, the folds -- and the developer's cursor jumps to the top of the file
        // every time they nudge a control in the designer.
        var module = TestHelpers.CreateModule(name: "Module1");
        module.UpdateCode("Option Explicit\r\nDim x As Long\r\n");
        var vm = CreateSut().Initialize(module);

        var header = "Attribute VB_Name = \"Module1\"\r\n";
        vm.CaretOffset = header.Length + "Option Explicit\r\nDim x".Length;

        vm.RefreshPrefix("Attribute VB_Name = \"RenamedToSomethingMuchLonger\"\r\n");

        vm.Document.GetText(vm.CaretOffset - 5, 5).Should().Be("Dim x");
    }

    [AvaloniaFact]
    public void AnUnchangedHeaderIsNotWrittenAtAll()
    {
        // A committed designer change re-renders whether or not the render moved. Writing an identical
        // header anyway would put an undo entry and a didChange on the wire for every nudge.
        var module = TestHelpers.CreateModule(name: "Module1");
        module.UpdateCode("Option Explicit\r\n");
        var vm = CreateSut().Initialize(module);

        var changed = 0;
        vm.Document.TextChanged += (_, _) => changed++;

        vm.RefreshPrefix("Attribute VB_Name = \"Module1\"\r\n");

        changed.Should().Be(0);
    }

    [AvaloniaFact]
    public void AReloadReplacesBOTHHalvesSoTheNextFlushDoesNotCutIntoTheBody()
    {
        // The live defect 3.2 left on the reload path. FileReloader pushed the bare code section into a
        // buffer whose prefix was still the header read at open, so BufferBody split at the stale prefix's
        // length -- and when the code section is longer than the header, that silently truncates real
        // source on the next flush. It is data loss, not a display fault.
        var module = TestHelpers.CreateModule(name: "Module1");
        module.UpdateCode("Option Explicit\r\n");
        var vm = CreateSut().Initialize(module);

        module.UpdateCode("Public Sub Main()\r\n    Debug.Print 1\r\nEnd Sub\r\n");
        vm.ReloadFrom(FormCodeText.Prefix(module), module.Code);

        vm.Document.Text.Should().Be(
            "Attribute VB_Name = \"Module1\"\r\nPublic Sub Main()\r\n    Debug.Print 1\r\nEnd Sub\r\n");
        vm.BufferBody.Should().Be("Public Sub Main()\r\n    Debug.Print 1\r\nEnd Sub\r\n");

        vm.Dispose();
        module.Code.Should().Be("Public Sub Main()\r\n    Debug.Print 1\r\nEnd Sub\r\n");
    }

    [AvaloniaFact]
    public void AReloadThatChangesTheHeaderFollowsIt()
    {
        var module = TestHelpers.CreateModule(name: "Module1");
        module.UpdateCode("Option Explicit\r\n");
        var vm = CreateSut().Initialize(module);

        // What a reload of a .cls looks like: the file on disk carries an attribute block the canonical
        // literal does not, and the buffer has to follow the file rather than the file it was opened from.
        module.RecordOriginalHeader(
            "Attribute VB_Name = \"Module1\"\r\nAttribute VB_Description = \"Rewritten elsewhere\"\r\n");
        vm.ReloadFrom(FormCodeText.Prefix(module), module.Code);

        vm.Document.Text.Should().StartWith(
            "Attribute VB_Name = \"Module1\"\r\nAttribute VB_Description = \"Rewritten elsewhere\"\r\n");
        vm.BufferBody.Should().Be("Option Explicit\r\n");
    }

    [AvaloniaFact]
    public void AReloadThatGrowsTheHeaderKeepsTheCaretInTheCode()
    {
        // Measured live before the fix: a form whose designer block gained a longer caption on disk was
        // reloaded with the caret at the start of its first line of code, and the caret came back two lines up,
        // inside the header. The marks are moved by the header's change in length; so is the caret.
        var module = TestHelpers.CreateModule(name: "Module1");
        module.UpdateCode("Option Explicit\r\n");
        var vm = CreateSut().Initialize(module);
        vm.CaretOffset = vm.BufferPrefixLength + "Option".Length;

        module.RecordOriginalHeader(
            "Attribute VB_Name = \"Module1\"\r\nAttribute VB_Description = \"Rewritten elsewhere\"\r\n");
        vm.ReloadFrom(FormCodeText.Prefix(module), module.Code);

        vm.CaretOffset.Should().Be(vm.BufferPrefixLength + "Option".Length, "the caret is where it was in the code");
    }

    [AvaloniaFact]
    public void AReloadThatShrinksTheHeaderKeepsTheCaretInTheCode()
    {
        var module = TestHelpers.CreateModule(name: "Module1");
        module.RecordOriginalHeader(
            "Attribute VB_Name = \"Module1\"\r\nAttribute VB_Description = \"Long before\"\r\n");
        module.UpdateCode("Option Explicit\r\n");
        var vm = CreateSut().Initialize(module);
        vm.CaretOffset = vm.BufferPrefixLength + "Option".Length;

        module.RecordOriginalHeader("Attribute VB_Name = \"Module1\"\r\n");
        vm.ReloadFrom(FormCodeText.Prefix(module), module.Code);

        vm.CaretOffset.Should().Be(vm.BufferPrefixLength + "Option".Length);
    }

    // ── LSP delegation ───────────────────────────────────────────────

    [AvaloniaFact]
    public async Task RequestHoverAsync_DelegatesToLspClient()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);
        var pos = new Position(1, 5);
        var expected = new HoverResult(new MarkupContent("plaintext", "info"));
        _lspClient.RequestHoverAsync("untitled:TestProject/Form1.frm", pos, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await vm.RequestHoverAsync(pos);

        result.Should().BeSameAs(expected);
    }

    [AvaloniaFact]
    public async Task RequestFoldingRangesAsync_DelegatesToLspClient()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);
        var expected = new[] { new FoldingRange(0, 10) };
        _lspClient.RequestFoldingRangesAsync("untitled:TestProject/Form1.frm", Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await vm.RequestFoldingRangesAsync();

        result.Should().BeSameAs(expected);
    }

    [AvaloniaFact]
    public async Task RequestCompletionAsync_DelegatesToLspClient()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);
        var pos = new Position(0, 0);
        var expected = new[] { new CompletionItem("Dim", CompletionItemKind.Keyword) };
        _lspClient.RequestCompletionAsync("untitled:TestProject/Form1.frm", pos, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await vm.RequestCompletionAsync(pos);

        result.Should().BeSameAs(expected);
    }

    [AvaloniaFact]
    public async Task RequestSignatureHelpAsync_DelegatesToLspClient()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);
        var pos = new Position(0, 3);
        var expected = new SignatureHelp(
            new[] { new SignatureInformation("MsgBox", "Shows a message", Array.Empty<ParameterInformation>()) }, 0, 0);
        _lspClient.RequestSignatureHelpAsync("untitled:TestProject/Form1.frm", pos, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await vm.RequestSignatureHelpAsync(pos);

        result.Should().BeSameAs(expected);
    }

    [AvaloniaFact]
    public async Task RequestDefinitionAsync_DelegatesToLspClient()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);
        var pos = new Position(2, 0);
        var expected = new[] { new Location("untitled:TestProject/Form1.frm", new Lsp.Messages.Range(new Position(0, 0), new Position(0, 5))) };
        _lspClient.RequestDefinitionAsync("untitled:TestProject/Form1.frm", pos, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await vm.RequestDefinitionAsync(pos);

        result.Should().BeSameAs(expected);
    }

    [AvaloniaFact]
    public async Task RequestDocumentHighlightAsync_DelegatesToLspClient()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);
        var pos = new Position(0, 0);
        var expected = new[]
        {
            new DocumentHighlight(new Lsp.Messages.Range(new Position(0, 0), new Position(0, 5)), 1)
        };
        _lspClient.RequestDocumentHighlightAsync("untitled:TestProject/Form1.frm", pos, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await vm.RequestDocumentHighlightAsync(pos);

        result.Should().BeSameAs(expected);
    }

    [AvaloniaFact]
    public async Task RequestRenameAsync_DelegatesToLspClient()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);
        var pos = new Position(0, 12);
        _lspClient.RequestRenameAsync("untitled:TestProject/Form1.frm", pos, "newName", Arg.Any<CancellationToken>())
            .Returns((WorkspaceEdit?)null);

        var result = await vm.RequestRenameAsync(pos, "newName");

        await _lspClient.Received(1).RequestRenameAsync(
            "untitled:TestProject/Form1.frm", pos, "newName", Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task RequestFormattingAsync_DelegatesToLspClient()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);
        var expected = Array.Empty<TextEdit>();
        _lspClient.RequestFormattingAsync("untitled:TestProject/Form1.frm", Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await vm.RequestFormattingAsync();

        result.Should().BeSameAs(expected);
    }

    // ── Dispose ──────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Dispose_CallsCloseDocumentAsync()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);

        vm.Dispose();
        _sut = null; // prevent double-dispose in teardown

        _lspClient.Received(1).CloseDocumentAsync("untitled:TestProject/Form1.frm", Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public void Dispose_UpdatesFormCodeFromDocument()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);
        vm.Document.Text = "Dim x As Integer";

        vm.Dispose();
        _sut = null;

        form.Code.Should().Be("Dim x As Integer");
    }

    [AvaloniaFact]
    public void Dispose_WritesTheBufferBackBeforeClosingTheDocument()
    {
        // Order, not just outcome. Both halves happen in one disposal block, and nothing pinned their
        // sequence until now — Dispose_CallsCloseDocumentAsync and Dispose_UpdatesFormCodeFromDocument each
        // assert only their own effect, so an inversion was invisible to the suite.
        //
        // It became inversion-prone when the language lifecycle moved into a collaborator: registering that
        // collaborator with AutoDispose from Initialize is the obvious thing to write, and it would put the
        // close FIRST, because disposables run in reverse registration order. The server would then be told
        // the document closed while the buffer it belongs to had not yet been written back.
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);
        vm.Document.Text = "Dim x As Integer";

        string? codeWhenClosed = null;
        _lspClient.When(c => c.CloseDocumentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => codeWhenClosed = form.Code);

        vm.Dispose();
        _sut = null;

        codeWhenClosed.Should().Be(
            "Dim x As Integer",
            "the buffer must reach the definition before the language layer is told the document is gone");
    }

    // ── Announcing a save to the language layer ──────────────────────────────

    [AvaloniaFact]
    public async Task ASavedModuleIsAnnouncedToItsServers()
    {
        Action<DocumentSavedEvent>? saved = null;
        _eventBus.Subscribe(Arg.Do<Action<DocumentSavedEvent>>(h => saved = h))
            .Returns(Substitute.For<IDisposable>());
        var module = TestHelpers.CreateModule(name: "Module1");
        var vm = CreateSut().Initialize(module);
        _lspClient.ClearReceivedCalls();

        saved!(new DocumentSavedEvent(null, module));

        await _lspClient.Received(1).SaveDocumentAsync("untitled:TestProject/Module1.bas", Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task ASaveOfSomeOtherDocumentIsIgnored()
    {
        // Every editor hears every save. Without the match, saving one module would announce a save of
        // every open document to every server holding one.
        Action<DocumentSavedEvent>? saved = null;
        _eventBus.Subscribe(Arg.Do<Action<DocumentSavedEvent>>(h => saved = h))
            .Returns(Substitute.For<IDisposable>());
        var vm = CreateSut().Initialize(TestHelpers.CreateModule(name: "Module1"));
        _lspClient.ClearReceivedCalls();

        saved!(new DocumentSavedEvent(null, TestHelpers.CreateModule(name: "SomethingElse")));

        await _lspClient.DidNotReceive().SaveDocumentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task AUserControlIsAnnouncedOnceUnderItsModuleUri()
    {
        // A UserControl or PropertyPage is ONE file with two halves, and Initialize(ModuleDefinition) sets
        // both definition fields for that reason (#152), so the event names both. It is announced once,
        // under the module's URI — the same precedence GetDocumentUri applies.
        //
        // Worth saying what this does NOT prove, since an earlier version of this test claimed it did: it
        // is not a de-duplication test. One save publishes one event and this handler runs once, so
        // matching either half or preferring the module give the same answer. Mutation testing found the
        // difference unobservable, which is why the handler no longer pretends to guard against it.
        Action<DocumentSavedEvent>? saved = null;
        _eventBus.Subscribe(Arg.Do<Action<DocumentSavedEvent>>(h => saved = h))
            .Returns(Substitute.For<IDisposable>());

        var project = TestHelpers.CreateProject("P");
        var control = new ModuleDefinition(project, "UserControl1", ModuleKind.UserControl);
        var designerHalf = new HexIDE.Runtime.ProjectElements.FormDefinition(
            project,
            [new HexIDE.Runtime.Components.ComponentInstance(
                HexIDE.Runtime.Components.FormComponentClass.Instance, "UserControl1")],
            "");
        control.UpdateFormPart(designerHalf);
        CreateSut().Initialize(control);
        _lspClient.ClearReceivedCalls();

        saved!(new DocumentSavedEvent(designerHalf, control));

        await _lspClient.Received(1).SaveDocumentAsync(
            "untitled:P/UserControl1.ctl", Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public void Dispose_UpdatesModuleCodeFromDocument()
    {
        var module = TestHelpers.CreateModule(name: "Module1");
        var vm = CreateSut().Initialize(module);
        vm.Document.Text = "Public Sub Hello()\nEnd Sub";

        vm.Dispose();
        _sut = null;

        module.Code.Should().Be("Public Sub Hello()\nEnd Sub");
    }

    [AvaloniaFact]
    public void Dispose_UnsubscribesFromDiagnosticsPublished()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);

        vm.Dispose();
        _sut = null;

        // `Received()` before the `-=`, not a bare `-=`. Without it this line merely unsubscribes on the
        // substitute and asserts nothing, so the test could not fail — which it could not, until now.
        _lspClient.Received().DiagnosticsPublished -= Arg.Any<EventHandler<PublishDiagnosticsParams>>();
    }

    // ── A name change is announced, not assumed ──────────────────────

    [AvaloniaFact]
    public async Task AFirstSaveReopensTheDocumentUnderItsFileName()
    {
        // The trigger #489 made ordinary: a document has no file until the project is saved, so a first
        // save is how nearly every document acquires one -- and a document is named by its file.
        //
        // Close THEN open, both under the right names. The protocol's guidance for a rename, and for its
        // reason: more than the name can change, so the server is told to forget and told afresh.
        Action<DocumentSavedEvent>? saved = null;
        _eventBus.Subscribe(Arg.Do<Action<DocumentSavedEvent>>(h => saved = h))
            .Returns(Substitute.For<IDisposable>());
        var module = TestHelpers.CreateModule(name: "Module1");
        CreateSut().Initialize(module);
        _lspClient.ClearReceivedCalls();

        var path = OperatingSystem.IsWindows() ? @"C:\proj\Module1.bas" : "/proj/Module1.bas";
        module.AbsolutePath = path;
        saved!(new DocumentSavedEvent(null, module));

        Received.InOrder(() =>
        {
            _lspClient.CloseDocumentForRenameAsync(
                "untitled:TestProject/Module1.bas", Arg.Any<CancellationToken>());
            _lspClient.OpenDocumentAsync(
                LspDocumentUri.ForFile(path), Arg.Any<string>(), true, Arg.Any<CancellationToken>());
        });
    }

    [AvaloniaFact]
    public async Task AFirstSaveOfAPathlessFormReopensItUnderItsFrmFile()
    {
        // The form half of the same rule, and it is the half that had no event to act on until #273 task
        // 2.4a: SaveProjectToDirectory's module loop announced through SaveModuleCore while its form loop
        // wrote through SerializeFormToFile and announced nothing, so a form whose path that method had
        // just repointed went on being named untitled: forever. A server then held it under a name the
        // editor no longer used, and every later change reached it under a name it had never been told
        // about -- silently, because an unknown URI is answered with an empty result rather than an error.
        //
        // Written against a form rather than a module deliberately. The two are separate Initialize
        // overloads with separate subscriptions, and this suite's module tests passed throughout the
        // period the form path was broken.
        Action<DocumentSavedEvent>? saved = null;
        _eventBus.Subscribe(Arg.Do<Action<DocumentSavedEvent>>(h => saved = h))
            .Returns(Substitute.For<IDisposable>());
        var form = TestHelpers.CreateForm(name: "Form1");
        CreateSut().Initialize(form);
        _lspClient.ClearReceivedCalls();

        var path = OperatingSystem.IsWindows() ? @"C:\proj\Form1.frm" : "/proj/Form1.frm";
        form.AbsolutePath = path;
        saved!(new DocumentSavedEvent(form, null));

        Received.InOrder(() =>
        {
            _lspClient.CloseDocumentForRenameAsync(
                "untitled:TestProject/Form1.frm", Arg.Any<CancellationToken>());
            _lspClient.OpenDocumentAsync(
                LspDocumentUri.ForFile(path), Arg.Any<string>(), true, Arg.Any<CancellationToken>());
        });
    }

    [AvaloniaFact]
    public async Task TheSaveIsAnnouncedUnderTheNewNameNotTheOld()
    {
        // Ordering, and it is not cosmetic: announcing the save first tells a server about a write to a
        // document it is then immediately told to forget, and the server that matters never hears that the
        // document it now holds was saved at all.
        Action<DocumentSavedEvent>? saved = null;
        _eventBus.Subscribe(Arg.Do<Action<DocumentSavedEvent>>(h => saved = h))
            .Returns(Substitute.For<IDisposable>());
        var module = TestHelpers.CreateModule(name: "Module1");
        CreateSut().Initialize(module);
        _lspClient.ClearReceivedCalls();

        var path = OperatingSystem.IsWindows() ? @"C:\proj\Module1.bas" : "/proj/Module1.bas";
        module.AbsolutePath = path;
        saved!(new DocumentSavedEvent(null, module));

        await _lspClient.Received(1).SaveDocumentAsync(
            LspDocumentUri.ForFile(path), Arg.Any<CancellationToken>());
        await _lspClient.DidNotReceive().SaveDocumentAsync(
            "untitled:TestProject/Module1.bas", Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task AnOrdinarySaveThatChangesNoNameDoesNotReopenAnything()
    {
        // Most saves change nothing about the name, and a close-and-reopen on each would throw away the
        // server's state on the file the developer is actually working in.
        Action<DocumentSavedEvent>? saved = null;
        _eventBus.Subscribe(Arg.Do<Action<DocumentSavedEvent>>(h => saved = h))
            .Returns(Substitute.For<IDisposable>());
        var module = TestHelpers.CreateModule(name: "Module1");
        module.AbsolutePath = OperatingSystem.IsWindows() ? @"C:\proj\Module1.bas" : "/proj/Module1.bas";
        CreateSut().Initialize(module);
        _lspClient.ClearReceivedCalls();

        saved!(new DocumentSavedEvent(null, module));

        await _lspClient.DidNotReceive().CloseDocumentForRenameAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _lspClient.Received(1).SaveDocumentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public void RenamingAPathlessDocumentReopensItWithNoSaveInvolved()
    {
        // A document with no file is named untitled:<Project>/<Name>, so its own rename changes what
        // servers should call it with nothing written to disk at all. After #489 that is the ordinary
        // state of a new document, not a corner case -- and no save event will ever fire to catch it.
        var module = TestHelpers.CreateModule(name: "Module1");
        CreateSut().Initialize(module);
        _lspClient.ClearReceivedCalls();

        module.Name = "Utilities";

        _lspClient.Received(1).CloseDocumentForRenameAsync(
            "untitled:TestProject/Module1.bas", Arg.Any<CancellationToken>());
        _lspClient.Received(1).OpenDocumentAsync(
            "untitled:TestProject/Utilities.bas", Arg.Any<string>(), true, Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public void RenamingTheProjectReopensItsPathlessDocuments()
    {
        // The project name is half the untitled: spelling, so renaming the project renames every document
        // in it that has no file -- again with no save, and again invisibly without this.
        var project = TestHelpers.CreateProject("TestProject");
        var module = TestHelpers.CreateModule(owner: project, name: "Module1");
        project.AddModule(module);
        CreateSut().Initialize(module);
        _lspClient.ClearReceivedCalls();

        project.Name = "Renamed";

        _lspClient.Received(1).CloseDocumentForRenameAsync(
            "untitled:TestProject/Module1.bas", Arg.Any<CancellationToken>());
        _lspClient.Received(1).OpenDocumentAsync(
            "untitled:Renamed/Module1.bas", Arg.Any<string>(), true, Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public void ABuildSaysNothingAtAllAlthoughItRepointsEveryPath()
    {
        // Make EXE writes every form and module into a temporary directory, repointing each AbsolutePath
        // on the way in and restoring it in a finally -- so for the length of one build, the path a
        // document would be NAMED from is a file in TEMP that is about to be deleted.
        //
        // The wire name has to sit that out, and it does so structurally rather than by recognising a
        // temporary path: it is fixed when the document is opened and moves only when a rename or an
        // announced save says so. Nothing polls AbsolutePath, and this test is what stops one starting to
        // -- a reconcile driven off the path would rename the document into TEMP and back on every build,
        // twice, handing servers two names that never existed and discarding whatever state they held.
        //
        // The build's own silence is the other half, pinned separately: Make EXE passes announceSave:
        // false for its modules and writes its forms through SerializeFormToFile, which announces nothing
        // at all (DocumentSavedAnnouncementTests).
        var module = TestHelpers.CreateModule(name: "Module1");
        var real = OperatingSystem.IsWindows() ? @"C:\proj\Module1.bas" : "/proj/Module1.bas";
        module.AbsolutePath = real;
        CreateSut().Initialize(module);
        _lspClient.ClearReceivedCalls();

        // Composed rather than written out: this is a real filesystem path, so Path is the right tool
        // (the rule against it governs paths going into a VB6 file), and a literal one under a user
        // profile is indistinguishable to a scanner from somebody's actual machine.
        var staged = Path.Combine(Path.GetTempPath(), "hexide-make-1234", "Module1.bas");

        module.AbsolutePath = staged;   // into the build's temp directory
        module.AbsolutePath = real;     // and back, in Make EXE's finally

        _lspClient.DidNotReceive().CloseDocumentForRenameAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        _lspClient.DidNotReceive().OpenDocumentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    // ── The name a request goes out under ────────────────────────────

    [AvaloniaFact]
    public async Task ARequestAfterAFirstSaveStillNamesTheDocumentTheSessionOpened()
    {
        // The defect 2.5 closes, and it was silent. The session's name is fixed when it opens, but every
        // request used to be minted fresh from the identity -- which reads the path live. So from the
        // moment a document was first saved, the lifecycle notifications still said `untitled:` while
        // every request said `file:`. The bundled server answers an unknown URI with an empty array and no
        // error, so hover, completion, folding, Go To Definition, rename and formatting simply went quiet.
        //
        // Since #489 established that a document normally HAS no file until the project is saved, a first
        // save is the ordinary path into this, not a corner of it.
        var module = TestHelpers.CreateModule(name: "Module1");
        var vm = CreateSut().Initialize(module);
        _lspClient.ClearReceivedCalls();

        module.AbsolutePath = OperatingSystem.IsWindows() ? @"C:\proj\Module1.bas" : "/proj/Module1.bas";

        await vm.RequestHoverAsync(new Position(0, 0));

        await _lspClient.Received(1).RequestHoverAsync(
            "untitled:TestProject/Module1.bas", Arg.Any<Position>(), Arg.Any<CancellationToken>());
        await _lspClient.DidNotReceive().RequestHoverAsync(
            Arg.Is<string>(u => u.StartsWith("file:")), Arg.Any<Position>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task NoRequestIsSentOnceTheSessionHasClosed()
    {
        // A request naming a document no server was told about is not a question with a wrong answer; it
        // is one nobody can be expected to answer, and asking invites exactly the silence above. Disposal
        // is the reachable way to observe it -- the editor is gone, but a pending completion task or a
        // queued symbol refresh can still arrive.
        var module = TestHelpers.CreateModule(name: "Module1");
        var vm = CreateSut().Initialize(module);
        vm.Dispose();
        _sut = null;
        _lspClient.ClearReceivedCalls();

        var hover = await vm.RequestHoverAsync(new Position(0, 0));
        var folds = await vm.RequestFoldingRangesAsync();

        hover.Should().BeNull();
        folds.Should().BeEmpty();
        await _lspClient.DidNotReceive().RequestHoverAsync(
            Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>());
        await _lspClient.DidNotReceive().RequestFoldingRangesAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── LSP delegation with Module URI ───────────────────────────────

    [AvaloniaFact]
    public async Task RequestHoverAsync_Module_UsesModuleUri()
    {
        var module = TestHelpers.CreateModule(name: "Utils");
        var vm = CreateSut().Initialize(module);
        var pos = new Position(0, 0);

        await vm.RequestHoverAsync(pos);

        await _lspClient.Received(1).RequestHoverAsync(
            "untitled:TestProject/Utils.bas", pos, Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task RequestCompletionAsync_Module_UsesModuleUri()
    {
        var module = TestHelpers.CreateModule(name: "Utils");
        var vm = CreateSut().Initialize(module);
        var pos = new Position(0, 0);

        await vm.RequestCompletionAsync(pos);

        await _lspClient.Received(1).RequestCompletionAsync(
            "untitled:TestProject/Utils.bas", pos, Arg.Any<CancellationToken>());
    }

    // ── Constructor subscribes to events ─────────────────────────────

    [AvaloniaFact]
    public void Constructor_SubscribesToCreateOrNavigateToSubEvent()
    {
        CreateSut();

        _eventBus.Received(1).Subscribe<CreateOrNavigateToSubEvent>(
            Arg.Any<Action<CreateOrNavigateToSubEvent>>());
    }

    // ── The flush must survive being published off the UI thread (#334) ──────────────

    [AvaloniaFact]
    public async Task ApplyAllUnsavedChanges_PublishedOffTheUiThread_StillFlushesTheEditorIntoTheModel()
    {
        // The regression this exists for is SILENT. Document.Text throws "Call from invalid thread" off
        // the UI thread; EventBus logs that and moves on; the save that published the event then writes
        // the model's PREVIOUS code with a fresh timestamp and reports success. An MCP write tool awaited
        // its save on a pool thread and did exactly that, and nothing in the result said so.
        Action<ApplyAllUnsavedChangesEvent>? flush = null;
        _eventBus.Subscribe(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>())
            .Returns(ci =>
            {
                flush = ci.Arg<Action<ApplyAllUnsavedChangesEvent>>();
                return Substitute.For<IDisposable>();
            });

        var module = TestHelpers.CreateModule(name: "Module1");
        var vm = CreateSut().Initialize(module);
        vm.Document.Text = "Sub Edited()";

        flush.Should().NotBeNull("the view model subscribes in its constructor");

        // Awaited rather than waited on: the handler marshals with Dispatcher.UIThread.Invoke, so the UI
        // thread has to keep pumping while the pool thread blocks on it. Blocking the test thread here
        // would deadlock the very mechanism under test.
        var threw = await Task.Run(() =>
        {
            try { flush!(new ApplyAllUnsavedChangesEvent()); return (Exception?)null; }
            catch (Exception ex) { return ex; }
        });

        threw.Should().BeNull("a flush from a pool thread must marshal, not throw");
        module.Code.Should().Contain("Sub Edited()",
            "an unflushed editor means the next save serializes the previous code");
    }

    [AvaloniaFact]
    public void Constructor_SubscribesToApplyAllUnsavedChangesEvent()
    {
        CreateSut();

        _eventBus.Received(1).Subscribe<ApplyAllUnsavedChangesEvent>(
            Arg.Any<Action<ApplyAllUnsavedChangesEvent>>());
    }

    [AvaloniaFact]
    public void Constructor_SubscribesToFormUnloadedEvent()
    {
        CreateSut();

        _eventBus.Received(1).Subscribe<FormUnloadedEvent>(
            Arg.Any<Action<FormUnloadedEvent>>());
    }

    // ── Initialize returns self (fluent) ─────────────────────────────

    [AvaloniaFact]
    public void Initialize_Form_ReturnsSelf()
    {
        var form = TestHelpers.CreateForm();
        var vm = CreateSut();

        var result = vm.Initialize(form);

        result.Should().BeSameAs(vm);
    }

    [AvaloniaFact]
    public void Initialize_Module_ReturnsSelf()
    {
        var module = TestHelpers.CreateModule();
        var vm = CreateSut();

        var result = vm.Initialize(module);

        result.Should().BeSameAs(vm);
    }

    // ── Read-only gate (issues #21/#22) ─────────────────────────────────────────────────────────
    // A form's code lives inside the .frm, so a form HexIDE refuses to save discards code edits too.
    // Gating only the designer would leave the more likely loss — someone typing a procedure — unprotected.

    private static FormDefinition MakeForm(bool faithful)
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        // The binary cause, because that is what actually holds the six remaining corpus forms read-only now
        // that containers round-trip. The fixture used to inject the container sentence, which the loader no
        // longer produces — a synthetic string the product had stopped saying.
        if (!faithful)
            form.MarkUnfaithfulToSave(UnfaithfulSaveCause.UnreproducibleBinaryContent,
                "it references companion binary content HexIDE cannot re-emit (0 of 1 blob(s) reached the model)");
        return form;
    }

    [AvaloniaFact]
    public void IsReadOnly_IsTrue_ForAFormThatCannotBeSavedFaithfully()
    {
        var vm = CreateSut();
        vm.Initialize(MakeForm(faithful: false));

        vm.IsReadOnly.Should().BeTrue();
        vm.ReadOnlyReason.Should().Contain("companion binary content");
    }

    [AvaloniaFact]
    public void IsReadOnly_IsFalse_ForAnOrdinaryForm()
    {
        var vm = CreateSut();
        vm.Initialize(MakeForm(faithful: true));

        vm.IsReadOnly.Should().BeFalse("the gate must be narrow — an ordinary form stays editable");
        vm.ReadOnlyReason.Should().BeNull();
    }

    [AvaloniaFact]
    public void IsReadOnly_IsFalse_ForAStandaloneModule()
    {
        // .bas/.cls round-trip byte-identically since #18, so they are never gated.
        var vm = CreateSut();
        vm.Initialize(new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"),
                                           "Module1", ModuleKind.StandardModule));

        vm.IsReadOnly.Should().BeFalse();
    }

    // ── #152: a UserControl is ONE file, so its editor holds BOTH halves ──────────────

    /// <summary>A UserControl module with a designer half, as ProjectService builds one on load.</summary>
    private static ModuleDefinition CreateUserControl(string code = "Option Explicit\r\n", bool faithful = true)
    {
        var project = TestHelpers.CreateProject("UCProject");
        var module = new ModuleDefinition(project, "UserControl1", ModuleKind.UserControl);
        var formPart = TestHelpers.CreateForm(owner: project, name: "UserControl1");
        formPart.UpdateCode(code);
        if (!faithful)
            formPart.MarkUnfaithfulToSave(UnfaithfulSaveCause.UnreproducibleBinaryContent, "test");
        module.UpdateFormPart(formPart);
        module.UpdateCode(code);
        return module;
    }

    [AvaloniaFact]
    public void Initialize_Module_WithADesignerHalf_AdoptsIt()
    {
        // The whole of #152 in one assertion. This used to be null, so the module door and the designer
        // door built different editors over one .ctl, each flushing to its own buffer — and the two save
        // paths read different ones, so whichever the developer had NOT typed into reached disk.
        var module = CreateUserControl();
        var vm = CreateSut().Initialize(module);

        vm.ModuleDefinition.Should().BeSameAs(module);
        vm.FormDefinition.Should().BeSameAs(module.FormPart, "a UserControl's editor holds both halves");
    }

    [AvaloniaFact]
    public void Initialize_Module_WithADesignerHalf_ListsItsControls()
    {
        // Not cosmetic: FindComponent returns null without a form definition, so the event dropdown stays
        // empty and double-clicking a control in the designer cannot navigate to its handler.
        var module = CreateUserControl();
        var vm = CreateSut().Initialize(module);

        vm.ObjectNames.Should().Contain("(General)");
        vm.SelectedObject.Should().Be("(General)");
    }

    [AvaloniaFact]
    public void Initialize_Module_WithoutADesignerHalf_LeavesTheFormNull()
    {
        // A .bas or .cls has no designer half, and everything downstream keys off that rather than off a
        // kind check — so this is what keeps IsReadOnly, the object combo and the save path correct for them.
        var module = TestHelpers.CreateModule(name: "Module1", kind: ModuleKind.StandardModule);
        var vm = CreateSut().Initialize(module);

        vm.FormDefinition.Should().BeNull();
        vm.ObjectNames.Should().ContainSingle().Which.Should().Be("(General)");
        vm.SelectedObject.Should().Be("(General)");
        vm.IsReadOnly.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Initialize_Module_WithAnUnfaithfulDesignerHalf_IsReadOnly()
    {
        // #147's other half, which falls out of #152 rather than needing its own wiring: an unfaithful
        // UserControl is refused on save, and the editor must say so rather than accept typing that will
        // never reach disk.
        var module = CreateUserControl(faithful: false);
        var vm = CreateSut().Initialize(module);

        vm.IsReadOnly.Should().BeTrue("its designer half cannot be reproduced, so the save will refuse it");
        vm.ReadOnlyReason.Should().NotBeNullOrEmpty();
    }

    [AvaloniaFact]
    public void FlushingAUserControlsEditor_UpdatesBothHalves()
    {
        // The divergence itself. SaveModule serializes module.Code; SerializeFormToFile serializes
        // formPart.Code. If a flush reaches only one of them the other goes to disk stale, and IsDirty —
        // which reads module.Code — does not even report the file as changed.
        Action<ApplyAllUnsavedChangesEvent>? flush = null;
        _eventBus.Subscribe(Arg.Do<Action<ApplyAllUnsavedChangesEvent>>(a => flush = a))
                 .Returns(Substitute.For<IDisposable>());

        var module = CreateUserControl(code: "Option Explicit\r\n");
        var vm = CreateSut().Initialize(module);
        vm.Document.Text = "Private Sub UserControl_Initialize()\r\nEnd Sub\r\n";

        flush.Should().NotBeNull("the editor subscribes to the flush event");
        flush!(new ApplyAllUnsavedChangesEvent());

        module.Code.Should().Contain("UserControl_Initialize", "the module half is what SaveModule writes");
        module.FormPart!.Code.Should().Contain("UserControl_Initialize",
            "the designer half is what SerializeFormToFile writes — both must carry the edit");
    }
}
