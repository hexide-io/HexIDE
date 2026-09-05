using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using HexIDE.Bookmarks;
using HexIDE.Controls;
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
using LspRange = HexIDE.Lsp.Messages.Range;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Diagnostics surviving the view being re-materialised, which is what a dock move does.
///
/// <para>
/// <c>MarkersChanged</c> is a notification, not a state. The view subscribes when it attaches and there
/// is nothing to read back, so a view built after the last publication starts with empty renderers — and
/// neither source feeding this channel has any reason to publish again for a document that has not
/// changed. The result is squiggles that vanish when a document is dragged to another dock and stay gone
/// until the next edit.
/// </para>
///
/// <para>
/// Worse for the second source: compiling with the real VB6 toolchain injects its errors here too, and a
/// build is a far rarer thing to have to wait for again than a keystroke.
/// </para>
/// </summary>
public class CodeEditorMarkerReattachTests : IDisposable
{
    private readonly ILspClient _lspClient = Substitute.For<ILspClient>();
    private readonly List<Window> _windows = [];

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        GC.SuppressFinalize(this);
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

        // A real value, because the view pushes it straight into AvaloniaEdit, which rejects a
        // non-positive indentation size. A substitute's default of 0 therefore throws out of the ATTACH
        // handler — which reads as the view being untestable in a window rather than as a fixture that
        // has not been told what a tab is.
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);

        _lspClient.IsRunning.Returns(true);
        _lspClient.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DocumentSymbol>());

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
        module.UpdateCode("Sub Main()\r\nEnd Sub\r\n");
        return module;
    }

    /// <summary>Puts the view in a real visual tree — which is what raises the attach the wiring hangs off.</summary>
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

    private static TextView TextViewOf(CodeEditorView view) =>
        view.FindControl<TextEditor>("TextEditor")!.TextArea.TextView;

    private void PublishOneDiagnostic(string uri)
    {
        _lspClient.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(
            _lspClient, new PublishDiagnosticsParams(uri, [new Diagnostic(
                new LspRange(new Position(0, 0), new Position(0, 3)), "something is wrong")]));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void AViewBuiltAfterTheDiagnosticsArrivedStillDrawsThem()
    {
        // The dock move. The second view is a different object around the same view model, built after the
        // publication, so the only way it can draw anything is by catching up from the view model.
        using var vm = MakeViewModel();
        vm.Initialize(AModule());
        var first = Show(vm);
        PublishOneDiagnostic(vm.GetDocumentUriPublic());
        TextViewOf(first).BackgroundRenderers.OfType<LspTextMarkerService>()
            .Should().ContainSingle().Which.Markers.Should().ContainSingle();

        var reattached = Show(vm);

        first.Should().NotBeSameAs(reattached);
        TextViewOf(reattached).BackgroundRenderers.OfType<LspTextMarkerService>()
            .Should().ContainSingle()
            .Which.Markers.Should().ContainSingle(
                "the renderer being attached is not enough — it has to be HOLDING the diagnostics, and "
              + "nothing is going to publish again for a document that has not changed");
    }

    [AvaloniaFact]
    public void AViewAttachingBeforeAnythingIsPublishedDrawsNothing()
    {
        // The ordinary case, and the one that stops the catch-up from inventing state: a fresh editor has
        // no diagnostics, and must not act as though it had been handed an empty set by a server.
        using var vm = MakeViewModel();
        vm.Initialize(AModule());

        var view = Show(vm);

        vm.Markers.Should().BeEmpty();
        TextViewOf(view).BackgroundRenderers.OfType<LspTextMarkerService>()
            .Should().ContainSingle().Which.Markers.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void ResolvedProblemsDoNotComeBackOnAReattach()
    {
        // The catch-up must replay the LATEST set, not the last non-empty one. An empty set is how a
        // server says the problems are resolved, and a dock move must not resurrect them.
        using var vm = MakeViewModel();
        vm.Initialize(AModule());
        Show(vm);
        PublishOneDiagnostic(vm.GetDocumentUriPublic());

        _lspClient.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(
            _lspClient, new PublishDiagnosticsParams(vm.GetDocumentUriPublic(), []));
        Dispatcher.UIThread.RunJobs();

        var reattached = Show(vm);

        TextViewOf(reattached).BackgroundRenderers.OfType<LspTextMarkerService>()
            .Should().ContainSingle().Which.Markers.Should().BeEmpty();
    }
}
