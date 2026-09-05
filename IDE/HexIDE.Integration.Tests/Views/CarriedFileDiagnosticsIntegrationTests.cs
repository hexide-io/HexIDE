using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using HexIDE.Controls;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using LspRange = HexIDE.Lsp.Messages.Range;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A language server's diagnostics rendering in the editor for a file the project carries but does not
/// compile — the last step of giving such a file language support at all.
///
/// <para>
/// Here rather than in the view-model tests for two reasons. Attaching the renderers is view work and is
/// not observable from a view model. And the conversion from a published diagnostic to a drawn marker
/// hops the UI thread, which only the thread that initialised Avalonia may pump — so a plain unit test
/// either cannot see the result, or "passes" because the posted work never ran. Under
/// <c>[AvaloniaFact]</c> the test body IS on that thread, so the whole path can be driven for real.
/// </para>
/// </summary>
public class CarriedFileDiagnosticsIntegrationTests : IDisposable
{
    private readonly ILspClient _lspClient = Substitute.For<ILspClient>();

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "hexide-carried-" + Guid.NewGuid().ToString("N"));

    private readonly List<Window> _windows = [];

    public CarriedFileDiagnosticsIntegrationTests()
    {
        Directory.CreateDirectory(_dir);
        _lspClient.IsRunning.Returns(true);
    }

    public void Dispose()
    {
        foreach (var w in _windows) w.Close();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private RelatedDocumentEditorViewModel OpenCarried(string fileName, string content)
    {
        var path = Path.Combine(_dir, fileName);
        File.WriteAllText(path, content);
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        return new RelatedDocumentEditorViewModel(_lspClient)
            .Initialize(new RelatedDocumentDefinition(project, fileName, path));
    }

    /// <summary>Puts the view in a real visual tree, which is what raises the attach the wiring hangs off.</summary>
    private RelatedDocumentEditorView Show(RelatedDocumentEditorViewModel vm)
    {
        var view = new RelatedDocumentEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 700 };
        _windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return view;
    }

    private string OpenedUri() =>
        (string)_lspClient.ReceivedCalls()
            .First(c => c.GetMethodInfo().Name == nameof(ILspClient.OpenDocumentAsync))
            .GetArguments()[0]!;

    private void Publish(string uri, params (int Line, int Start, int End, string Message)[] diagnostics)
    {
        _lspClient.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(
            _lspClient,
            new PublishDiagnosticsParams(uri, [.. diagnostics.Select(d => new Diagnostic(
                new LspRange(new Position(d.Line, d.Start), new Position(d.Line, d.End)), d.Message))]));

        // The conversion is posted to this thread rather than run inline, so nothing has happened yet.
        Dispatcher.UIThread.RunJobs();
    }

    private static TextView TextViewOf(RelatedDocumentEditorView view) =>
        view.FindControl<TextEditor>("TextEditor")!.TextArea.TextView;

    // ── The whole path, end to end ────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ADiagnosticFromAServerIsDrawnInACarriedFile()
    {
        // The single assertion this entire change exists to make true: a file HexIDE has no opinion about,
        // a server it did not write, and a squiggle in the editor.
        var vm = OpenCarried("README.md", "hello world");
        var view = Show(vm);

        Publish(OpenedUri(), (0, 0, 5, "spelling"));

        vm.Markers.Should().ContainSingle().Which.Message.Should().Be("spelling");
        TextViewOf(view).BackgroundRenderers.OfType<LspTextMarkerService>()
            .Should().ContainSingle("the renderer must be attached, or the diagnostic is computed and never drawn")
            .Which.Markers.Should().ContainSingle();

        // Both renderers, because a mutation sweep found nothing noticed the colorizer being absent: the
        // detach test asserts the transformer list is empty afterwards, which is equally true of one that
        // was never added. A diagnostic should look the same in a carried file as in VB6 source.
        TextViewOf(view).LineTransformers.OfType<LspDiagnosticsColorizer>().Should().ContainSingle();
    }

    [AvaloniaFact]
    public void ADiagnosticForAnotherDocumentIsNotDrawnHere()
    {
        // Every editor hears every publication — there is one channel — so this filtering is the only
        // thing stopping one file's problems appearing in another's buffer.
        var vm = OpenCarried("README.md", "hello world");
        Show(vm);

        Publish("file:///somewhere/else.md", (0, 0, 5, "not yours"));

        vm.Markers.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void ResolvingTheProblemsClearsTheSquiggles()
    {
        var vm = OpenCarried("README.md", "hello world");
        Show(vm);
        Publish(OpenedUri(), (0, 0, 5, "spelling"));

        Publish(OpenedUri());

        vm.Markers.Should().BeEmpty("an empty set is how a server says the problems are resolved");
    }

    [AvaloniaFact]
    public void ADiagnosticEchoedUnderADifferentlySpelledUriIsStillDrawn()
    {
        // #236 in its natural habitat. A server is under no obligation to echo a URI back byte for byte,
        // and this is where that bites hardest: a carried file is named by a `file:` URI built from a real
        // path, so any character needing an escape is a spelling the two sides can disagree about. A
        // literal space for our %20 is the commonest, and comparing with `==` would drop every diagnostic
        // the server publishes — silently, looking exactly like a server with nothing to say.
        var vm = OpenCarried("read me.md", "hello world");
        Show(vm);
        var asWeNamedIt = OpenedUri();
        asWeNamedIt.Should().Contain("%20", "the URI we send is the escaped spelling");

        Publish(asWeNamedIt.Replace("%20", " "), (0, 0, 5, "spelling"));

        vm.Markers.Should().ContainSingle().Which.Message.Should().Be("spelling");
    }

    // ── Attaching and detaching ───────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void AViewAttachingAfterTheDiagnosticsArrivedStillDrawsThem()
    {
        // Moving this document to another dock detaches and re-materialises the view, and a server has no
        // reason to re-publish for a document that has not changed. MarkersChanged is a notification, not
        // a state, so without the view catching up on attach the squiggles would vanish on a dock move
        // and stay gone until the next edit.
        var vm = OpenCarried("README.md", "hello world");
        var first = Show(vm);
        Publish(OpenedUri(), (0, 0, 5, "spelling"));

        var reattached = Show(vm);

        first.Should().NotBeSameAs(reattached, "a dock move builds a new view over the same view model");
        TextViewOf(reattached).BackgroundRenderers.OfType<LspTextMarkerService>()
            .Should().ContainSingle().Which.Markers.Should().ContainSingle(
                "the renderer being attached is not enough — it has to be HOLDING the diagnostics, and no "
              + "further publication is coming for a document that has not changed");
    }

    [AvaloniaFact]
    public void DetachingRemovesTheRenderersRatherThanLeavingThemStacked()
    {
        // A re-attach on the same view would otherwise add a second pair on top of the first, and every
        // diagnostic would be drawn twice over — which looks like a rendering artifact rather than a leak.
        var vm = OpenCarried("README.md", "hello world");
        var view = Show(vm);
        var textView = TextViewOf(view);
        textView.BackgroundRenderers.OfType<LspTextMarkerService>().Should().ContainSingle();

        _windows[^1].Close();
        Dispatcher.UIThread.RunJobs();

        textView.BackgroundRenderers.OfType<LspTextMarkerService>().Should().BeEmpty();
        textView.LineTransformers.OfType<LspDiagnosticsColorizer>().Should().BeEmpty();
    }

    [AvaloniaFact]
    public void ACarriedFileNoServerClaimsRendersNormally()
    {
        // The common case, and it must cost nothing. Most carried files are plain text no server will ever
        // look at; they open as an ordinary editor with no diagnostics and no error.
        var vm = OpenCarried("notes.txt", "just some notes");
        var view = Show(vm);

        vm.Markers.Should().BeEmpty();
        vm.LoadError.Should().BeNull();
        view.FindControl<TextEditor>("TextEditor")!.Document.Text.Should().Be("just some notes");
    }
}
