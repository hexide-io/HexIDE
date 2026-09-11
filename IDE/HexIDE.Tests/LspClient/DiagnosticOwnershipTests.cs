using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;

using LspRange = HexIDE.Lsp.Messages.Range;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

public class DiagnosticOwnershipTests
{
    private const string Form1 = "vb6://form/Form1";

    private const string FullCapabilities = """
        {"textDocumentSync":{"openClose":true,"change":1}}
        """;

    private static ILspClient FakeServer()
    {
        var c = Substitute.For<ILspClient>();
        c.IsRunning.Returns(true);
        c.AdvertisedCapabilities.Returns(JsonDocument.Parse(FullCapabilities).RootElement.Clone());
        return c;
    }

    private static LanguageServerRegistration Registration(string id, ILspClient client) =>
        new(id, id, DocumentLanguage.Vb6Extensions, DocumentLanguage.Vb6, () => client, 0);

    private static LspClientRegistry Registry(params LanguageServerRegistration[] r) =>
        new(r, Substitute.For<ILogger<LspClientRegistry>>());

    private static Diagnostic Say(string message) =>
        new(new LspRange(new Position(0, 0), new Position(0, 1)), message, DiagnosticSeverity.Error);

    [Fact]
    public async Task ABuildsEmptyClearDoesNotTakeTheServersDiagnosticsWithIt()
    {
        var server = FakeServer();
        var sut = Registry(Registration("vb6", server));
        await sut.OpenDocumentAsync(Form1, "code", TestContext.Current.CancellationToken);

        // An addin reading the cache is the observable consequence — asserting that the call returned
        // says nothing about what reached a consumer.
        using var seen = new AddinDiagnosticsService(sut);

        server.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(
            server, new PublishDiagnosticsParams(Form1, [Say("Syntax error: unexpected 'End Sub'")]));

        await sut.InjectDiagnosticsAsync(Form1, [], DiagnosticOwner.Vb6Compiler);

        seen.GetAll().Should().ContainSingle()
            .Which.Message.Should().Be("Syntax error: unexpected 'End Sub'");
    }

    [Fact]
    public async Task TwoServersOnOneDocumentDoNotOverwriteEachOther()
    {
        var a = FakeServer();
        var b = FakeServer();
        var sut = Registry(Registration("a", a), Registration("b", b));
        await sut.OpenDocumentAsync(Form1, "code", TestContext.Current.CancellationToken);

        using var seen = new AddinDiagnosticsService(sut);

        a.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(
            a, new PublishDiagnosticsParams(Form1, [Say("from a")]));
        b.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(
            b, new PublishDiagnosticsParams(Form1, [Say("from b")]));

        seen.GetAll().Select(d => d.Message).Should().BeEquivalentTo(["from a", "from b"]);
    }

    [Fact]
    public async Task AServerThatFallsSilentDoesNotEraseTheCompilersErrors()
    {
        var server = FakeServer();
        var sut = Registry(Registration("vb6", server));
        await sut.OpenDocumentAsync(Form1, "code", TestContext.Current.CancellationToken);

        using var seen = new AddinDiagnosticsService(sut);

        await sut.InjectDiagnosticsAsync(
            Form1, [Say("Compile error: Sub or Function not defined")], DiagnosticOwner.Vb6Compiler);
        server.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(
            server, new PublishDiagnosticsParams(Form1, []));

        seen.GetAll().Should().ContainSingle()
            .Which.Message.Should().Be("Compile error: Sub or Function not defined");
    }

    [Fact]
    public async Task OneSourceWithdrawingLeavesTheOtherSourcesAlone()
    {
        // On a single connection rather than the router, because both are ILspClient and either can be
        // what the toolchain is handed. Neither has a server here — injection deliberately does not need
        // one.
        var sut = new VBLspClient(
            Substitute.For<ILspTransport>(), Substitute.For<ILogger<VBLspClient>>(), DocumentLanguage.Vb6);
        using var seen = new AddinDiagnosticsService(sut);

        await sut.InjectDiagnosticsAsync(Form1, [Say("from the compiler")], DiagnosticOwner.Vb6Compiler);
        await sut.InjectDiagnosticsAsync(Form1, [Say("from a linter")], "linter");

        seen.GetAll().Select(d => d.Message)
            .Should().BeEquivalentTo(["from the compiler", "from a linter"]);

        await sut.ClearDiagnosticsFromAsync(DiagnosticOwner.Vb6Compiler);

        seen.GetAll().Should().ContainSingle().Which.Message.Should().Be("from a linter");
    }

    [Fact]
    public async Task WithdrawingTheLastSourceClearsTheDocument()
    {
        // The behaviour the erasing clear existed for, and the one that must survive scoping it: a fixed
        // error has to disappear.
        var sut = new VBLspClient(
            Substitute.For<ILspTransport>(), Substitute.For<ILogger<VBLspClient>>(), DocumentLanguage.Vb6);
        using var seen = new AddinDiagnosticsService(sut);

        await sut.InjectDiagnosticsAsync(Form1, [Say("from the compiler")], DiagnosticOwner.Vb6Compiler);
        await sut.ClearDiagnosticsFromAsync(DiagnosticOwner.Vb6Compiler);

        seen.GetAll().Should().BeEmpty();
    }

    [Fact]
    public async Task WithdrawingASourceThatPublishedNothingSaysNothing()
    {
        // A build on a project with no errors last time must not raise a publish per document it has never
        // touched — every one of those is a whole-document replacement at the far end.
        var sut = new VBLspClient(
            Substitute.For<ILspTransport>(), Substitute.For<ILogger<VBLspClient>>(), DocumentLanguage.Vb6);
        var raised = 0;
        sut.DiagnosticsPublished += (_, _) => raised++;

        await sut.InjectDiagnosticsAsync(Form1, [Say("from a linter")], "linter");
        await sut.ClearDiagnosticsFromAsync(DiagnosticOwner.Vb6Compiler);

        raised.Should().Be(1, "only the linter's own publish should have reached anyone");
    }
}
