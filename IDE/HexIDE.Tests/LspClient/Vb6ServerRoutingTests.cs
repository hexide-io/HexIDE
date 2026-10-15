using System.Text.Json;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// A user's own VB6 server receiving the IDE's own documents.
///
/// <para>
/// This was the headline capability of making the server list configuration, and it silently did nothing
/// unless the user guessed one undocumented string. The IDE's documents are <c>vb6://</c> URIs, routing
/// short-circuited on the scheme and compared only <c>languageId</c>, and the loader meanwhile
/// <em>requires</em> every entry to declare <c>extensions</c> — so a server attached with
/// <c>languageId: "vba"</c>, which is at least as natural a choice, launched, initialized, looked
/// healthy, and was never sent a document (hexide-io/HexIDE#277).
/// </para>
///
/// <para>
/// The separation the configurable list already established for files applies here too: <b>an entry's
/// extensions are what it claims to serve; its language identifier is what it wants that thing called.</b>
/// Routing should read the first and the wire should carry the second, whichever shape the URI takes.
/// </para>
/// </summary>
public class Vb6ServerRoutingTests
{
    // A project's standard module, as it is actually named since #273 task 2.1.
    private const string Vb6Doc = "untitled:Project1/Module1.bas";

    private const string Capabilities = """{"textDocumentSync":{"openClose":true,"change":1}}""";

    private static ILspClient FakeServer()
    {
        var c = Substitute.For<ILspClient>();
        c.IsRunning.Returns(true);
        c.AdvertisedCapabilities.Returns(JsonDocument.Parse(Capabilities).RootElement.Clone());
        return c;
    }

    private static LspClientRegistry RegistryOf(params LanguageServerRegistration[] registrations) =>
        new(registrations, Substitute.For<ILogger<LspClientRegistry>>());

    [Fact]
    public async Task AVb6ServerAttachedUnderADifferentLanguageIdStillGetsTheDocuments()
    {
        // #277. `vba` is at least as natural as `vb6` — it is what the wider ecosystem calls the language
        // and what the grammar this project vendors is named for — and nothing anywhere told the user that
        // only one exact string works.
        var server = FakeServer();
        var sut = RegistryOf(new LanguageServerRegistration(
            "my-vb", "My VB server", [".bas", ".cls", ".frm"], "vba", () => server));

        await sut.OpenDocumentAsync(Vb6Doc, "Sub Main()\r\nEnd Sub\r\n", TestContext.Current.CancellationToken);

        await server.Received(1).OpenDocumentAsync(Vb6Doc, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ItIsStillToldTheIdentifierItAskedFor()
    {
        // Routing on what a server SERVES does not change what it is CALLED. That separation is the whole
        // point of the per-server identifier, and broadening the match must not quietly undo it.
        var server = FakeServer();
        var registration = new LanguageServerRegistration(
            "my-vb", "My VB server", [".bas", ".cls", ".frm"], "vba", () => server);
        var sut = RegistryOf(registration);

        await sut.OpenDocumentAsync(Vb6Doc, "Sub Main()\r\n", TestContext.Current.CancellationToken);

        sut.Connections.Single().LanguageId.Should().Be("vba");
    }

    [Fact]
    public async Task DeclaringTheLanguageWithoutClaimingAnExtensionNoLongerRoutesAnything()
    {
        // INVERTED by #273 task 2.6, deliberately, and it is the one behavioural loss in retiring the
        // scheme. `languageId` used to double as a claim on `vb6://` documents, which carried no extension
        // for an entry to match on. They carry one now, so routing reads the extension for every document
        // and the identifier names the language a server is TOLD rather than what it is SENT.
        //
        // Nothing real regresses: an entry claiming a made-up extension and nothing else was never going
        // to be handed a `.bas` on disk either, so this removes an inconsistency rather than a capability.
        var server = FakeServer();
        var sut = RegistryOf(new LanguageServerRegistration(
            "hexide.vb6", "Bundled", [".nothing-familiar"], "vb6", () => server));

        await sut.OpenDocumentAsync(Vb6Doc, "Sub Main()\r\n", TestContext.Current.CancellationToken);

        await server.DidNotReceive().OpenDocumentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeclaringTheLanguageIsStillWhatLetsAnEntryBeOfferedAProjectsClassModules()
    {
        // The half the identifier KEPT. It no longer routes, but it still establishes that an entry serves
        // VB6 -- which is what distinguishes a VB6 server claiming `.cls` from a LaTeX one claiming the
        // same extension (#279). Without this the inversion above would read as "the identifier means
        // nothing now", and someone would delete the gate.
        var server = FakeServer();
        var sut = RegistryOf(new LanguageServerRegistration(
            "cls-only", "Classes only", [".cls"], "vb6", () => server));

        await sut.OpenDocumentAsync(
            "untitled:Project1/Class1.cls", "Option Explicit", isProjectMember: true,
            TestContext.Current.CancellationToken);

        await server.Received(1).OpenDocumentAsync(
            "untitled:Project1/Class1.cls", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AServerForSomeOtherLanguageIsStillNotOfferedVb6Documents()
    {
        // Broadening must not become "everyone gets everything". A Markdown server has said nothing that
        // could be read as a claim on VB6 source.
        var markdown = FakeServer();
        var sut = RegistryOf(new LanguageServerRegistration(
            "md", "Markdown", [".md", ".markdown"], "markdown", () => markdown));

        await sut.OpenDocumentAsync(Vb6Doc, "Sub Main()\r\n", TestContext.Current.CancellationToken);

        await markdown.DidNotReceive().OpenDocumentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnEntryClaimingOnlyTheAmbiguousExtensionIsNotOfferedVb6Documents()
    {
        // `.cls` is a VB6 class module AND a LaTeX class file (#279). A server claiming only `.cls` has
        // not said which it means, so reading that as a claim on every VB6 module in the project would
        // hand a LaTeX server the developer's source — a worse failure than the one being fixed, and one
        // this change would have introduced.
        //
        // Nothing is lost: a real VB6 server claims `.bas` and `.frm` as well, and one that genuinely
        // serves only class modules can still say `languageId: "vb6"`.
        var latex = FakeServer();
        var sut = RegistryOf(new LanguageServerRegistration(
            "latex", "LaTeX", [".cls", ".sty", ".tex"], "latex", () => latex));

        await sut.OpenDocumentAsync(Vb6Doc, "Sub Main()\r\n", TestContext.Current.CancellationToken);

        await latex.DidNotReceive().OpenDocumentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnEntryClaimingOnlyTheAmbiguousExtensionIsNotOfferedARealProjectClassModule()
    {
        // The same guarantee as the test above, asserted against the URI a class module actually carries
        // since #273 task 2.1. The test above opens `vb6://module/Module1`, which nothing mints any more:
        // that string has no extension, so routing returns no claimants and the assertion passes without
        // testing anything. This one names the document the way the IDE now does.
        var latex = FakeServer();
        var sut = RegistryOf(new LanguageServerRegistration(
            "latex", "LaTeX", [".cls", ".sty", ".tex"], "latex", () => latex));

        await sut.OpenDocumentAsync(
            "untitled:Project1/Class1.cls", "Option Explicit", isProjectMember: true,
            TestContext.Current.CancellationToken);

        await latex.DidNotReceive().OpenDocumentAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TwoVb6ServersUnderDifferentNamesBothGetTheDocument()
    {
        // The same plurality that already holds for files: two servers may serve one language and
        // disagree about what to call it, and both are right.
        var ours = FakeServer();
        var theirs = FakeServer();
        var sut = RegistryOf(
            new LanguageServerRegistration("hexide.vb6", "Bundled", [".bas", ".frm"], "vb6", () => ours),
            new LanguageServerRegistration("theirs", "Theirs", [".bas", ".frm"], "vba", () => theirs));

        await sut.OpenDocumentAsync(Vb6Doc, "Sub Main()\r\n", TestContext.Current.CancellationToken);

        await ours.Received(1).OpenDocumentAsync(Vb6Doc, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await theirs.Received(1).OpenDocumentAsync(Vb6Doc, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
