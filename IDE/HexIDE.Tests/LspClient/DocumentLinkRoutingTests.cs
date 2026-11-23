using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;

namespace HexIDE.Tests.LspClient;

public sealed class DocumentLinkRoutingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HighestPriorityCapableClaimantWinsIncludingAnEmptyAnswer(bool noAnswer)
    {
        const string uri = "file:///main.txt";
        var clients = Enumerable.Range(0, 3).Select(_ => Substitute.For<ILspClient>()).ToArray();
        foreach (var client in clients) client.IsRunning.Returns(true);
        clients[1].SupportsCapability(uri, "documentLinkProvider").Returns(true);
        clients[2].SupportsCapability(uri, "documentLinkProvider").Returns(true);
        clients[1].RequestDocumentLinksAsync(uri, Arg.Any<CancellationToken>()).Returns(Task.FromResult<DocumentLink[]?>(noAnswer ? null : []));
        clients[2].RequestDocumentLinksAsync(uri, Arg.Any<CancellationToken>())
            .Returns([new DocumentLink(new(new(0, 0), new(0, 1)), "https://example.com")]);
        await using var registry = new LspClientRegistry([.. clients.Select((client, index) =>
            new LanguageServerRegistration("links" + index, "Links " + index, [".txt"], "plaintext", () => client, Priority: 30 - index * 10)).Reverse()],
            Substitute.For<ILogger<LspClientRegistry>>());
        var token = TestContext.Current.CancellationToken;
        await registry.OpenDocumentAsync(uri, "text", isProjectMember: false, cancellationToken: token);
        var answer = await registry.RequestDocumentLinksAsync(uri, token);
        if (noAnswer) answer.Should().ContainSingle();
        else answer.Should().BeEmpty();
        await clients[0].DidNotReceive().RequestDocumentLinksAsync(uri, Arg.Any<CancellationToken>());
        await clients[1].Received(1).RequestDocumentLinksAsync(uri, Arg.Any<CancellationToken>());
        await clients[2].Received(noAnswer ? 1 : 0).RequestDocumentLinksAsync(uri, Arg.Any<CancellationToken>());
    }
}
