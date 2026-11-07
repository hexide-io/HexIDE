using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;

namespace HexIDE.Tests.LspClient;

public class ClosedDiagnosticsCacheTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryCloseAcceptsLaterPublications(bool useRegistry)
    {
        await using var server = new VBLspClient(Substitute.For<ILspTransport>(),
            Substitute.For<ILogger<VBLspClient>>(), "vb6");
        await using var registry = new LspClientRegistry(
            [new LanguageServerRegistration("vb6", "VB6", DocumentLanguage.Vb6Extensions,
                DocumentLanguage.Vb6, () => server, 0)], Substitute.For<ILogger<LspClientRegistry>>());
        ILspClient client = useRegistry ? registry : server;
        using var cache = new DiagnosticsCache(client);
        const string uri = "file:///project/Module1.bas";
        await client.OpenDocumentAsync(uri, "", TestContext.Current.CancellationToken);
        server.PublishServerDiagnostics(new PublishDiagnosticsParams(uri, []));
        await client.CloseDocumentAsync(uri, TestContext.Current.CancellationToken);
        var received = new List<PublishDiagnosticsParams>();
        client.DiagnosticsPublished += (_, p) => received.Add(p);
        Diagnostic[] diagnostics = [new(new(new(0, 0), new(0, 1)), "late error", DiagnosticSeverity.Error)];
        server.PublishServerDiagnostics(new PublishDiagnosticsParams(uri, diagnostics));
        cache.GetAll().Single().Diagnostics.Should().BeEquivalentTo(diagnostics);
        received.Should().ContainSingle().Which.Diagnostics.Should().BeEquivalentTo(diagnostics);
    }

    [Fact]
    public async Task PublicationHandlerCanRenameFromAnotherThread()
    {
        await using var client = new VBLspClient(Substitute.For<ILspTransport>(),
            Substitute.For<ILogger<VBLspClient>>(), "vb6");
        const string uri = "untitled:Project1/Module1.bas";
        await client.OpenDocumentAsync(uri, "", TestContext.Current.CancellationToken);
        var renamed = false;
        client.DiagnosticsPublished += (_, p) =>
        {
            if (renamed) return;
            renamed = true;
            Task.Run(() => client.CloseDocumentForRenameAsync(uri, TestContext.Current.CancellationToken))
                .Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("events must run outside the lifecycle lock");
        };
        using var cache = new DiagnosticsCache(client);
        client.PublishServerDiagnostics(new PublishDiagnosticsParams(uri, []));
        cache.GetAll().Should().BeEmpty("a publish accepted before rename must be evicted afterwards");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LatePublicationCannotRestoreRenamedAnalysis(bool hasDiagnostics)
    {
        await using var client = new VBLspClient(Substitute.For<ILspTransport>(),
            Substitute.For<ILogger<VBLspClient>>(), "vb6");
        using var cache = new DiagnosticsCache(client);
        const string uri = "untitled:Project1/Module1.bas";
        await client.OpenDocumentAsync(uri, "", TestContext.Current.CancellationToken);
        client.PublishServerDiagnostics(new PublishDiagnosticsParams(uri, []));
        await client.CloseDocumentForRenameAsync(uri, TestContext.Current.CancellationToken);

        Diagnostic[] diagnostics = hasDiagnostics
            ? [new(new(new(0, 0), new(0, 1)), "late error", DiagnosticSeverity.Error)] : [];
        client.PublishServerDiagnostics(new PublishDiagnosticsParams(uri, diagnostics));

        cache.GetAll().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReopeningAllowsAnalysisAgain(bool useRegistry)
    {
        await using var server = new VBLspClient(Substitute.For<ILspTransport>(),
            Substitute.For<ILogger<VBLspClient>>(), "vb6");
        await using var registry = new LspClientRegistry(
            [new LanguageServerRegistration("vb6", "VB6", DocumentLanguage.Vb6Extensions,
                DocumentLanguage.Vb6, () => server, 0)], Substitute.For<ILogger<LspClientRegistry>>());
        ILspClient client = useRegistry ? registry : server;
        using var cache = new DiagnosticsCache(client);
        const string uri = "untitled:Project1/Module1.bas";
        await client.OpenDocumentAsync(uri, "", TestContext.Current.CancellationToken);
        await client.CloseDocumentForRenameAsync(uri, TestContext.Current.CancellationToken);
        server.PublishServerDiagnostics(new PublishDiagnosticsParams(uri, []));
        cache.GetAll().Should().BeEmpty();

        await client.OpenDocumentAsync(uri, "", TestContext.Current.CancellationToken);
        server.PublishServerDiagnostics(new PublishDiagnosticsParams(uri, []));

        cache.GetAll().Select(p => p.Uri).Should().Equal(uri);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task OnlyRenameForgetsCachedAnalysis(bool useRegistry, bool rename)
    {
        await using ILspClient client = useRegistry
            ? new LspClientRegistry([], Substitute.For<ILogger<LspClientRegistry>>())
            : new VBLspClient(Substitute.For<ILspTransport>(), Substitute.For<ILogger<VBLspClient>>(), "vb6");
        using var cache = new DiagnosticsCache(client);
        const string uri = "untitled:Project1/Module1.bas";
        await client.InjectDiagnosticsAsync(uri, [], DiagnosticOwner.LanguageServer);
        cache.GetAll().Should().ContainSingle();

        if (rename) await client.CloseDocumentForRenameAsync(uri, TestContext.Current.CancellationToken);
        else await client.CloseDocumentAsync(uri, TestContext.Current.CancellationToken);

        cache.GetAll().Count.Should().Be(rename ? 0 : 1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task SavingUntitledModulesForgetsTheirOldAnalysedUris(int count)
    {
        var server = Substitute.For<ILspClient>();
        server.IsRunning.Returns(true);
        await using var registry = new LspClientRegistry(
            [new LanguageServerRegistration("vb6", "VB6", DocumentLanguage.Vb6Extensions,
                DocumentLanguage.Vb6, () => server, 0)], Substitute.For<ILogger<LspClientRegistry>>());
        using var cache = new DiagnosticsCache(registry);
        var oldUris = Enumerable.Range(1, count).Select(i => $"untitled:Project1/Module{i}.bas").ToArray();
        var newUris = Enumerable.Range(1, count).Select(i => $"file:///project/Module{i}.bas").ToArray();
        foreach (var uri in oldUris)
        {
            await registry.OpenDocumentAsync(uri, "", TestContext.Current.CancellationToken);
            server.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(
                server, new PublishDiagnosticsParams(uri, []));
        }
        cache.GetAll().Select(p => p.Uri).Should().BeEquivalentTo(oldUris);

        for (var i = 0; i < count; i++)
        {
            await registry.CloseDocumentForRenameAsync(oldUris[i], TestContext.Current.CancellationToken);
            await registry.OpenDocumentAsync(newUris[i], "", TestContext.Current.CancellationToken);
            server.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(
                server, new PublishDiagnosticsParams(newUris[i], []));
            foreach (var diagnostics in new Diagnostic[][] { [], [new(new(new(0, 0), new(0, 1)), "late error", DiagnosticSeverity.Error)] })
                server.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(
                    server, new PublishDiagnosticsParams(oldUris[i], diagnostics));
        }

        // GetDiagnosticsAsync builds 'analysed' from this same snapshot, including clean documents.
        cache.GetAll().Select(p => p.Uri).Should().BeEquivalentTo(newUris);
        registry.Diagnostics.Should().BeEmpty("late diagnostics for retired names must not return to the Diagnostics panel");
    }
}
