using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;

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
    private readonly ILocalizationService _localization = Substitute.For<ILocalizationService>();
    private CodeEditorViewModel? _sut;

    public CodeEditorViewModelTests()
    {
        AvaloniaTestSetup.EnsureInitialized();
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
            _windowManager, _editorService, _projectService, _eventBus, _lspClient, _settingsService, _statusBarService, _bookmarkService, _breakpointService, _debugController, _localization);
        return _sut;
    }

    public void Dispose()
    {
        _sut?.Dispose();
    }

    // ── Edit-while-running reset prompt (VB6-faithful E&C affordance) ──

    [Fact]
    public async Task ConfirmResetWhileRunningAsync_Yes_RequestsProjectEnd()
    {
        _windowManager.MessageBox(Arg.Any<string>(), Arg.Any<string>(), MessageBoxButtons.YesNo, Arg.Any<MessageBoxIcon>())
            .Returns(MessageBoxResult.Yes);

        var reset = await CreateSut().ConfirmResetWhileRunningAsync();

        reset.Should().BeTrue();
        _eventBus.Received(1).Publish(Arg.Any<EndProjectRequestedEvent>());
    }

    [Fact]
    public async Task ConfirmResetWhileRunningAsync_No_KeepsRunning()
    {
        _windowManager.MessageBox(Arg.Any<string>(), Arg.Any<string>(), MessageBoxButtons.YesNo, Arg.Any<MessageBoxIcon>())
            .Returns(MessageBoxResult.No);

        var reset = await CreateSut().ConfirmResetWhileRunningAsync();

        reset.Should().BeFalse();
        _eventBus.DidNotReceive().Publish(Arg.Any<EndProjectRequestedEvent>());
    }

    [Fact]
    public void IsProjectRunning_ReflectsTheDebugController()
    {
        _debugController.IsSessionActive.Returns(true);
        CreateSut().IsProjectRunning.Should().BeTrue();
    }

    // ── Initialization — Form ────────────────────────────────────────

    [Fact]
    public void Initialize_Form_SetsFormDefinition()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);

        vm.FormDefinition.Should().BeSameAs(form);
    }

    [Fact]
    public void Initialize_Form_SetsDocumentTextFromFormCode()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);

        vm.Document.Text.Should().Be(form.Code);
    }

    [Fact]
    public void Initialize_Form_ObjectNamesContainsGeneral()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);

        vm.ObjectNames.Should().Contain("(General)");
    }

    [Fact]
    public void Initialize_Form_TitleContainsFormAndProjectName()
    {
        var project = TestHelpers.CreateProject("MyProject");
        var form = TestHelpers.CreateForm(owner: project, name: "MainForm");
        var vm = CreateSut().Initialize(form);

        vm.Title.Should().Contain("MyProject");
        vm.Title.Should().Contain("MainForm");
    }

    // ── Initialization — Module ──────────────────────────────────────

    [Fact]
    public void Initialize_Module_SetsModuleDefinition()
    {
        var module = TestHelpers.CreateModule(name: "Module1");
        var vm = CreateSut().Initialize(module);

        vm.ModuleDefinition.Should().BeSameAs(module);
    }

    [Fact]
    public void Initialize_Module_SetsDocumentTextFromModuleCode()
    {
        var module = TestHelpers.CreateModule(name: "Module1");
        var vm = CreateSut().Initialize(module);

        vm.Document.Text.Should().Be(module.Code);
    }

    [Fact]
    public void Initialize_Module_ObjectNamesContainsOnlyGeneral()
    {
        var module = TestHelpers.CreateModule(name: "Module1");
        var vm = CreateSut().Initialize(module);

        vm.ObjectNames.Should().ContainSingle()
            .Which.Should().Be("(General)");
    }

    [Fact]
    public void Initialize_Module_TitleContainsModuleAndProjectName()
    {
        var project = TestHelpers.CreateProject("MyProject");
        var module = TestHelpers.CreateModule(owner: project, name: "Utilities");
        var vm = CreateSut().Initialize(module);

        vm.Title.Should().Contain("MyProject");
        vm.Title.Should().Contain("Utilities");
    }

    // ── Document URI ─────────────────────────────────────────────────

    [Fact]
    public void GetDocumentUri_Form_ReturnsCorrectUri()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);

        vm.GetDocumentUriPublic().Should().Be("vb6://form/Form1");
    }

    [Fact]
    public void GetDocumentUri_Module_ReturnsCorrectUri()
    {
        var module = TestHelpers.CreateModule(name: "Module1");
        var vm = CreateSut().Initialize(module);

        vm.GetDocumentUriPublic().Should().Be("vb6://module/Module1");
    }

    // ── LSP open on init ─────────────────────────────────────────────

    [Fact]
    public void Initialize_Form_WhenLspRunning_CallsOpenDocument()
    {
        _lspClient.IsRunning.Returns(true);
        var form = TestHelpers.CreateForm(name: "Form1");

        CreateSut().Initialize(form);

        _lspClient.Received(1).OpenDocumentAsync(
            "vb6://form/Form1",
            form.Code,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Initialize_Form_WhenLspNotRunning_DoesNotCallOpenDocument()
    {
        _lspClient.IsRunning.Returns(false);
        var form = TestHelpers.CreateForm(name: "Form1");

        CreateSut().Initialize(form);

        _lspClient.DidNotReceive().OpenDocumentAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Initialize_Module_WhenLspRunning_CallsOpenDocument()
    {
        _lspClient.IsRunning.Returns(true);
        var module = TestHelpers.CreateModule(name: "Module1");

        CreateSut().Initialize(module);

        _lspClient.Received(1).OpenDocumentAsync(
            "vb6://module/Module1",
            module.Code,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Initialize_Module_WhenLspNotRunning_DoesNotCallOpenDocument()
    {
        _lspClient.IsRunning.Returns(false);
        var module = TestHelpers.CreateModule(name: "Module1");

        CreateSut().Initialize(module);

        _lspClient.DidNotReceive().OpenDocumentAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    // ── LSP delegation ───────────────────────────────────────────────

    [Fact]
    public async Task RequestHoverAsync_DelegatesToLspClient()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);
        var pos = new Position(1, 5);
        var expected = new HoverResult(new MarkupContent("plaintext", "info"));
        _lspClient.RequestHoverAsync("vb6://form/Form1", pos, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await vm.RequestHoverAsync(pos);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task RequestFoldingRangesAsync_DelegatesToLspClient()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);
        var expected = new[] { new FoldingRange(0, 10) };
        _lspClient.RequestFoldingRangesAsync("vb6://form/Form1", Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await vm.RequestFoldingRangesAsync();

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task RequestCompletionAsync_DelegatesToLspClient()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);
        var pos = new Position(0, 0);
        var expected = new[] { new CompletionItem("Dim", CompletionItemKind.Keyword) };
        _lspClient.RequestCompletionAsync("vb6://form/Form1", pos, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await vm.RequestCompletionAsync(pos);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task RequestSignatureHelpAsync_DelegatesToLspClient()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);
        var pos = new Position(0, 3);
        var expected = new SignatureHelp(
            new[] { new SignatureInformation("MsgBox", "Shows a message", Array.Empty<ParameterInformation>()) }, 0, 0);
        _lspClient.RequestSignatureHelpAsync("vb6://form/Form1", pos, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await vm.RequestSignatureHelpAsync(pos);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task RequestDefinitionAsync_DelegatesToLspClient()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);
        var pos = new Position(2, 0);
        var expected = new[] { new Location("vb6://form/Form1", new Lsp.Messages.Range(new Position(0, 0), new Position(0, 5))) };
        _lspClient.RequestDefinitionAsync("vb6://form/Form1", pos, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await vm.RequestDefinitionAsync(pos);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task RequestDocumentHighlightAsync_DelegatesToLspClient()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);
        var pos = new Position(0, 0);
        var expected = new[]
        {
            new DocumentHighlight(new Lsp.Messages.Range(new Position(0, 0), new Position(0, 5)), 1)
        };
        _lspClient.RequestDocumentHighlightAsync("vb6://form/Form1", pos, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await vm.RequestDocumentHighlightAsync(pos);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task RequestRenameAsync_DelegatesToLspClient()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);
        var pos = new Position(0, 12);
        _lspClient.RequestRenameAsync("vb6://form/Form1", pos, "newName", Arg.Any<CancellationToken>())
            .Returns((WorkspaceEdit?)null);

        var result = await vm.RequestRenameAsync(pos, "newName");

        await _lspClient.Received(1).RequestRenameAsync(
            "vb6://form/Form1", pos, "newName", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestFormattingAsync_DelegatesToLspClient()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);
        var expected = Array.Empty<TextEdit>();
        _lspClient.RequestFormattingAsync("vb6://form/Form1", Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await vm.RequestFormattingAsync();

        result.Should().BeSameAs(expected);
    }

    // ── Dispose ──────────────────────────────────────────────────────

    [Fact]
    public void Dispose_CallsCloseDocumentAsync()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);

        vm.Dispose();
        _sut = null; // prevent double-dispose in teardown

        _lspClient.Received(1).CloseDocumentAsync("vb6://form/Form1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Dispose_UpdatesFormCodeFromDocument()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);
        vm.Document.Text = "Dim x As Integer";

        vm.Dispose();
        _sut = null;

        form.Code.Should().Be("Dim x As Integer");
    }

    [Fact]
    public void Dispose_UpdatesModuleCodeFromDocument()
    {
        var module = TestHelpers.CreateModule(name: "Module1");
        var vm = CreateSut().Initialize(module);
        vm.Document.Text = "Public Sub Hello()\nEnd Sub";

        vm.Dispose();
        _sut = null;

        module.Code.Should().Be("Public Sub Hello()\nEnd Sub");
    }

    [Fact]
    public void Dispose_UnsubscribesFromDiagnosticsPublished()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = CreateSut().Initialize(form);

        vm.Dispose();
        _sut = null;

        _lspClient.DiagnosticsPublished -= Arg.Any<EventHandler<PublishDiagnosticsParams>>();
    }

    // ── LSP delegation with Module URI ───────────────────────────────

    [Fact]
    public async Task RequestHoverAsync_Module_UsesModuleUri()
    {
        var module = TestHelpers.CreateModule(name: "Utils");
        var vm = CreateSut().Initialize(module);
        var pos = new Position(0, 0);

        await vm.RequestHoverAsync(pos);

        await _lspClient.Received(1).RequestHoverAsync(
            "vb6://module/Utils", pos, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequestCompletionAsync_Module_UsesModuleUri()
    {
        var module = TestHelpers.CreateModule(name: "Utils");
        var vm = CreateSut().Initialize(module);
        var pos = new Position(0, 0);

        await vm.RequestCompletionAsync(pos);

        await _lspClient.Received(1).RequestCompletionAsync(
            "vb6://module/Utils", pos, Arg.Any<CancellationToken>());
    }

    // ── Constructor subscribes to events ─────────────────────────────

    [Fact]
    public void Constructor_SubscribesToCreateOrNavigateToSubEvent()
    {
        CreateSut();

        _eventBus.Received(1).Subscribe<CreateOrNavigateToSubEvent>(
            Arg.Any<Action<CreateOrNavigateToSubEvent>>());
    }

    [Fact]
    public void Constructor_SubscribesToApplyAllUnsavedChangesEvent()
    {
        CreateSut();

        _eventBus.Received(1).Subscribe<ApplyAllUnsavedChangesEvent>(
            Arg.Any<Action<ApplyAllUnsavedChangesEvent>>());
    }

    [Fact]
    public void Constructor_SubscribesToFormUnloadedEvent()
    {
        CreateSut();

        _eventBus.Received(1).Subscribe<FormUnloadedEvent>(
            Arg.Any<Action<FormUnloadedEvent>>());
    }

    // ── Initialize returns self (fluent) ─────────────────────────────

    [Fact]
    public void Initialize_Form_ReturnsSelf()
    {
        var form = TestHelpers.CreateForm();
        var vm = CreateSut();

        var result = vm.Initialize(form);

        result.Should().BeSameAs(vm);
    }

    [Fact]
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

    [Fact]
    public void IsReadOnly_IsTrue_ForAFormThatCannotBeSavedFaithfully()
    {
        var vm = CreateSut();
        vm.Initialize(MakeForm(faithful: false));

        vm.IsReadOnly.Should().BeTrue();
        vm.ReadOnlyReason.Should().Contain("companion binary content");
    }

    [Fact]
    public void IsReadOnly_IsFalse_ForAnOrdinaryForm()
    {
        var vm = CreateSut();
        vm.Initialize(MakeForm(faithful: true));

        vm.IsReadOnly.Should().BeFalse("the gate must be narrow — an ordinary form stays editable");
        vm.ReadOnlyReason.Should().BeNull();
    }

    [Fact]
    public void IsReadOnly_IsFalse_ForAStandaloneModule()
    {
        // .bas/.cls round-trip byte-identically since #18, so they are never gated.
        var vm = CreateSut();
        vm.Initialize(new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"),
                                           "Module1", ModuleKind.StandardModule));

        vm.IsReadOnly.Should().BeFalse();
    }
}
