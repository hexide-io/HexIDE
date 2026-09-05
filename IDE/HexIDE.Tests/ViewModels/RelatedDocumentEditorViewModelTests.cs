using HexIDE.Controls;
using HexIDE.Forms.ViewModels;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.ProjectElements;
using LspRange = HexIDE.Lsp.Messages.Range;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// The editor for a file the project carries but does not compile, and its conversation with whatever
/// language server claims it.
///
/// <para>
/// This is what the configurable-server work was for. A carried file — a README, a changelog, a
/// <c>.sql</c> — is the one thing HexIDE opens that it has no opinion about: no grammar, no interpreter,
/// no designer. So it is exactly the file a server attached by configuration exists to serve, and until
/// now it was the one editor that never spoke to the language layer at all, which made the whole
/// configuration inert for precisely the file types it was built for.
/// </para>
/// </summary>
public class RelatedDocumentEditorViewModelTests : IDisposable
{
    private readonly ILspClient _lspClient = Substitute.For<ILspClient>();

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "hexide-reldoc-" + Guid.NewGuid().ToString("N"));

    private readonly List<RelatedDocumentEditorViewModel> _created = [];

    public RelatedDocumentEditorViewModelTests()
    {
        Directory.CreateDirectory(_dir);
        _lspClient.IsRunning.Returns(true);
    }

    public void Dispose()
    {
        foreach (var vm in _created) vm.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>A carried file that really exists, since the editor reads it on open.</summary>
    private RelatedDocumentDefinition Carried(string fileName, string content = "# hi\n")
    {
        var path = Path.Combine(_dir, fileName);
        File.WriteAllText(path, content);
        return new RelatedDocumentDefinition(TestHelpers.CreateProject("P"), fileName, path);
    }

    private RelatedDocumentEditorViewModel Open(RelatedDocumentDefinition document)
    {
        var vm = new RelatedDocumentEditorViewModel(_lspClient).Initialize(document);
        _created.Add(vm);
        return vm;
    }

    private string? OpenedUri()
    {
        foreach (var call in _lspClient.ReceivedCalls())
            if (call.GetMethodInfo().Name == nameof(ILspClient.OpenDocumentAsync))
                return (string?)call.GetArguments()[0];
        return null;
    }

    // ── Reaching the language layer at all ────────────────────────────────────────────────────────────

    [Fact]
    public async Task OpeningACarriedFileOffersItToTheLanguageLayer()
    {
        var document = Carried("README.md", "# hello\n");

        Open(document);

        await _lspClient.Received(1).OpenDocumentAsync(
            Arg.Any<string>(), "# hello\n", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ItIsNamedByAFileUriCarryingItsExtension()
    {
        // The extension is the ENTIRE basis on which a server claims this file — routing keys on it, and
        // this editor's documents carry no scheme that names a language the way `vb6://` does. A URI that
        // loses the extension routes nowhere, and that failure is a document that opens with no language
        // features and no error at all.
        Open(Carried("README.md"));

        var uri = OpenedUri();

        uri.Should().StartWith("file:///");
        DocumentLanguage.ExtensionOf(uri).Should().Be(".md");
    }

    [Fact]
    public async Task ClosingTheEditorClosesTheDocument()
    {
        var vm = Open(Carried("README.md"));

        vm.Dispose();

        await _lspClient.Received(1).CloseDocumentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── When there is nothing to offer ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ADocumentWithNoPathIsNotOffered()
    {
        // A project that has never been saved has no directory yet (#260), so its carried files have no
        // path. Inventing one would have a server index a file that is not there.
        Open(new RelatedDocumentDefinition(TestHelpers.CreateProject("P"), "README.md", null));

        await _lspClient.DidNotReceive().OpenDocumentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFileThatCouldNotBeReadIsNotOffered()
    {
        // The editor opens empty and read-only rather than lying about the content. Offering that empty
        // buffer would have a server publish diagnostics about a document nobody has, drawn over a banner
        // saying the file could not be read.
        var missing = new RelatedDocumentDefinition(
            TestHelpers.CreateProject("P"), "gone.md", Path.Combine(_dir, "gone.md"));

        var vm = Open(missing);

        vm.LoadError.Should().NotBeNull();
        await _lspClient.DidNotReceive().OpenDocumentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AnUnofferedDocumentStillOpensAsAnEditor()
    {
        // Not offering it to the language layer must not cost the developer the file. The editor is still
        // a working text editor for something no server will ever look at, which is most carried files.
        var vm = Open(new RelatedDocumentDefinition(TestHelpers.CreateProject("P"), "notes.txt", null));

        vm.Title.Should().Contain("notes.txt");
    }

    // ── Saving ────────────────────────────────────────────────────────────────────────────────────────
    //
    // Announcing a save is covered at the INTEGRATION level, not here. The announcement follows an
    // asynchronous file write, so the continuation lands wherever the synchronization context sends it —
    // the UI thread in the running IDE, an arbitrary pool thread in a plain unit test. Reading the editor
    // buffer from there throws "call from invalid thread", which is a fact about the test host rather
    // than about the code. See CarriedFileDiagnosticsIntegrationTests.

    // ── Diagnostics ───────────────────────────────────────────────────────────────────────────────────
    //
    // Deliberately not tested here. Converting a published diagnostic into markers is the session's job
    // and has twenty-odd tests of its own, including the clamping; what this class adds is one forwarding
    // lambda. Driving it from here would mean pumping the Avalonia dispatcher, which only the thread that
    // initialised it may do — and a test that "passes" because the posted work never ran is worse than no
    // test, which is precisely what the negative case would have done. The end-to-end proof is §6.2: a
    // real foreign server, a real carried file, squiggles in the running IDE.

}

/// <summary>
/// The <c>file:</c> URI a document on disk is named by.
///
/// <para>
/// Its own fixture because the rules are the routing contract, not an implementation detail: the
/// extension decides which server sees the file, and the escaping decides whether that server's reply is
/// recognised as being about it.
/// </para>
/// </summary>
public class FileDocumentUriTests
{
    [Fact]
    public void TheExtensionSurvives()
    {
        var uri = LspDocumentUri.ForFile(Path.Combine(Path.GetTempPath(), "README.md"));

        DocumentLanguage.ExtensionOf(uri).Should().Be(".md");
    }

    [Fact]
    public void ASpaceInThePathIsEncodedAndStillMatchesTheLiteralForm()
    {
        // Percent-encoding is what Uri does to a space, and a server may hand back either spelling. The
        // comparison unescapes before matching, so both name the same document — which is the property
        // that stops a path with a space in it silently losing every diagnostic.
        var uri = LspDocumentUri.ForFile(Path.Combine(Path.GetTempPath(), "read me.md"));

        uri.Should().Contain("%20");
        LspDocumentUri.AreSame(uri, uri.Replace("%20", " ")).Should().BeTrue();
        DocumentLanguage.ExtensionOf(uri).Should().Be(".md");
    }

    [Fact]
    public void ItIsAWellFormedAbsoluteFileUri()
    {
        var uri = LspDocumentUri.ForFile(Path.Combine(Path.GetTempPath(), "notes.txt"));

        uri.Should().StartWith("file:///");
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed).Should().BeTrue();
        parsed!.IsFile.Should().BeTrue();
    }

    [Fact]
    public void TheSamePathAlwaysProducesTheSameUri()
    {
        // Open, change and close must agree, and they only do because this is a function of the path.
        var path = Path.Combine(Path.GetTempPath(), "a", "..", "README.md");

        LspDocumentUri.ForFile(path).Should().Be(
            LspDocumentUri.ForFile(Path.Combine(Path.GetTempPath(), "README.md")),
            "a path is normalised before it becomes a URI, so two spellings of one file are one document");
    }
}
