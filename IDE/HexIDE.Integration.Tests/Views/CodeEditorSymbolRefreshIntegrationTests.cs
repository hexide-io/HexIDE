using System;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using LspRange = HexIDE.Lsp.Messages.Range;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The VB6 editor's procedure list refreshing when the server has evidently re-read the document.
///
/// <para>
/// Written because a mutation sweep found nothing noticed when the wiring was deleted: the whole suite
/// stayed green with the procedure list frozen after its initial load. That gap predates the move onto a
/// shared document session — the same piggyback existed inline and was equally untested — but moving an
/// untested mechanism and leaving it untested is how the vacuous assertion in this editor's own dispose
/// test survived for as long as it did.
/// </para>
///
/// <para>
/// At the integration level because the refresh is triggered from a UI-thread post, and only the thread
/// that initialised Avalonia may pump that. Under <c>[AvaloniaFact]</c> the test body is on that thread.
/// </para>
/// </summary>
public class CodeEditorSymbolRefreshIntegrationTests
{
    private readonly ILspClient _lspClient = Substitute.For<ILspClient>();

    private CodeEditorViewModel MakeViewModel()
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(
            Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(
            Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(
            Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());

        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Document.CodeSuffix").Returns("Code");

        _lspClient.IsRunning.Returns(true);
        _lspClient.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DocumentSymbol>());

        return new CodeEditorViewModel(
            Substitute.For<IWindowManager>(),
            Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(),
            eventBus,
            _lspClient,
            Substitute.For<ISettingsService>(),
            Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(),
            Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            localization);
    }

    private static ModuleDefinition AModule()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        return new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
    }

    [AvaloniaFact]
    public void FreshDiagnosticsProvokeAFreshLookAtTheDocumentSymbols()
    {
        // A new diagnostic set is the cheapest available signal that the server has just re-read the
        // document, so it is when its symbols are worth asking for again. Without this the procedure
        // dropdown is populated once at open and then never changes — a procedure the developer adds is
        // simply absent from it, with nothing to suggest why.
        using var vm = MakeViewModel();
        vm.Initialize(AModule());
        var uri = vm.GetDocumentUriPublic();
        _lspClient.ClearReceivedCalls();

        _lspClient.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(
            _lspClient, new PublishDiagnosticsParams(uri, [new Diagnostic(
                new LspRange(new Position(0, 0), new Position(0, 1)), "something changed")]));
        Dispatcher.UIThread.RunJobs();

        _lspClient.Received().RequestDocumentSymbolsAsync(uri, Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public void DiagnosticsForAnotherDocumentProvokeNothing()
    {
        // Otherwise every editor re-requests symbols whenever any document anywhere is analysed, which on
        // a project of any size is a request storm triggered by typing in an unrelated file.
        using var vm = MakeViewModel();
        vm.Initialize(AModule());
        _lspClient.ClearReceivedCalls();

        _lspClient.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(
            _lspClient, new PublishDiagnosticsParams("vb6://module/SomethingElse", []));
        Dispatcher.UIThread.RunJobs();

        _lspClient.DidNotReceive().RequestDocumentSymbolsAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
