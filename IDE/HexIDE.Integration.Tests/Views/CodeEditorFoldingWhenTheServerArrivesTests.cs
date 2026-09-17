using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Folding on the first document of a session, when the server that folds it is still starting.
/// </summary>
/// <remarks>
/// <para>
/// The view asks for folding ranges once, as it attaches. A language server starts on the first document
/// of a language it claims, so on that document the request is made while the server is still coming up:
/// <c>LspClientRegistry</c> picks among <em>started</em> claimants advertising the capability, finds none,
/// and answers with an empty set without reaching any connection. Nothing asked again, so a module opened
/// and read — rather than typed into — never folded at all (hexide-io/HexIDE#446).
/// </para>
/// <para>
/// Asserting the request rather than the drawn margin is deliberate here: "never asks" is the whole of the
/// defect, and a test that asserted sections would pass against a client that answered from a cache.
/// </para>
/// </remarks>
public class CodeEditorFoldingWhenTheServerArrivesTests : IDisposable
{
    private readonly ILspClient _lspClient = Substitute.For<ILspClient>();
    private readonly List<Window> _windows = [];

    /// <summary>What the server would answer, once it is up. Empty until then, as the registry answers.</summary>
    private FoldingRange[] _folds = [];

    /// <summary>
    /// How many times folding ranges have been asked for.
    /// </summary>
    /// <remarks>
    /// Counted around a settled baseline rather than asserted as a total, because attaching the editor
    /// raises <c>TextChanged</c> once as the document is bound, which schedules a fold of its own. An
    /// earlier version of these tests asserted "two requests" and passed with the fix reverted, on the
    /// strength of that one — a green test proving only that the editor had attached.
    /// </remarks>
    private int _foldRequests;

    public CodeEditorFoldingWhenTheServerArrivesTests()
    {
        _lspClient.IsRunning.Returns(true);
        _lspClient.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DocumentSymbol>());
        _lspClient.RequestFoldingRangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _foldRequests++;
                return Task.FromResult(_folds);
            });
    }

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        GC.SuppressFinalize(this);
    }

    [AvaloniaFact]
    public async Task AServerThatComesUpAfterTheEditorIsAskedAgain()
    {
        using var vm = MakeViewModel();
        vm.Initialize(AModule());
        Show(vm);

        // Everything the attach itself provokes, answered empty because nothing had started yet.
        await SettleFolding();
        var askedWhileStarting = _foldRequests;
        askedWhileStarting.Should().BeGreaterThan(0, "attaching asks at least once");

        _folds = [new FoldingRange(0, 1)];
        RaiseServerStateChanged();
        await SettleFolding();

        _foldRequests.Should().Be(askedWhileStarting + 1,
            "a server coming up is the second chance a document nobody types into depends on");
    }

    [AvaloniaFact]
    public async Task AnEditorThatIsNeverTypedIntoStillFolds()
    {
        // The same thing said the way a user would: the document is opened, read, and not edited. Before
        // the fix the only second chance was a keystroke.
        using var vm = MakeViewModel();
        vm.Initialize(AModule());
        Show(vm);
        await SettleFolding();
        var textAtStart = vm.Document.Text;
        var askedWhileStarting = _foldRequests;

        _folds = [new FoldingRange(0, 1)];
        RaiseServerStateChanged();
        await SettleFolding();

        vm.Document.Text.Should().Be(textAtStart, "nothing here edits the document");
        _foldRequests.Should().Be(askedWhileStarting + 1);
    }

    [AvaloniaFact]
    public async Task AStateChangeWithNoEditorAttachedDoesNotThrow()
    {
        // A connection can come up or drop while no code window is open, and the view model outlives the
        // view across a dock move. Nothing should notice.
        using var vm = MakeViewModel();
        vm.Initialize(AModule());

        RaiseServerStateChanged();
        await SettleFolding();

        _foldRequests.Should().Be(0, "there is no editor to fold");
    }

    private void RaiseServerStateChanged()
    {
        _lspClient.StateChanged += Raise.Event<EventHandler>(_lspClient, EventArgs.Empty);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Waits out the view's own settle before the request it schedules can be observed.
    /// </summary>
    /// <remarks>
    /// The view debounces folding by 500 ms, so this waits longer than that and then pumps, because the
    /// continuation posts back to the UI thread. A shorter wait here would assert that the request had not
    /// been made yet, which is true of the defect and of the fix alike.
    /// </remarks>
    private static async Task SettleFolding()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(900));
        Dispatcher.UIThread.RunJobs();
    }

    private CodeEditorViewModel MakeViewModel()
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(
            Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(
            Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(
            Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<DocumentSavedEvent>(
            Arg.Any<Action<DocumentSavedEvent>>()).Returns(Substitute.For<IDisposable>());

        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Document.CodeSuffix").Returns("Code");

        // A real value: the view pushes it into AvaloniaEdit, which rejects a non-positive indentation
        // size, so a substitute's default of 0 throws out of the attach handler.
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);

        return new CodeEditorViewModel(
            Substitute.For<IWindowManager>(),
            Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(),
            eventBus,
            _lspClient,
            settings,
            Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(),
            Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            localization);
    }

    private static ModuleDefinition AModule()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Sub Main()\r\n    Debug.Print 1\r\nEnd Sub\r\n");
        return module;
    }

    private CodeEditorView Show(CodeEditorViewModel vm)
    {
        var window = new Window { Width = 1200, Height = 800 };
        _windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var view = new CodeEditorView { DataContext = vm };
        window.Content = view;
        Dispatcher.UIThread.RunJobs();
        return view;
    }
}
