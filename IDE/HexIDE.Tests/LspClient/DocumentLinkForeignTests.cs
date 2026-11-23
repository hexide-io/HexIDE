using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;

namespace HexIDE.Tests.LspClient;

public sealed class DocumentLinkForeignTests
{
    [ForeignServerFact("cpp")]
    public Task ClangdIncludeLinksToHeader() => CheckAsync(ForeignServer.Cpp, "main.c", "#include \"other.h\"\n",
        "other.h", "", "other.h");

    [ForeignServerFact("latex")]
    public Task TexlabInputLinksToChapter() => CheckAsync(ForeignServer.Latex, "main.tex", "\\input{chapter}\n",
        "chapter.tex", "Chapter\n", "chapter");

    [ForeignServerFact("json")]
    public async Task JsonLocalReferenceLinksToDefinition()
    {
        var links = await ReadAsync(ForeignServer.Json, "main.json",
            "{\"definitions\":{\"a\":{\"type\":\"string\"}},\"$ref\":\"#/definitions/a\"}", null, null);
        // Observed from the pinned 4.10.0 server in #5446: a source range and a line/column fragment.
        links.Should().ContainSingle();
        links[0].Range.Should().Be(new HexIDE.Lsp.Messages.Range(new(0, 47), new(0, 62)));
        new Uri(links[0].Target!).Fragment.Should().Be("#1,21");
    }

    private static async Task CheckAsync(ForeignServer server, string name, string source, string other,
        string otherSource, string covered)
    {
        var links = await ReadAsync(server, name, source, other, otherSource);
        links.Should().ContainSingle();
        links[0].Range.Start.Line.Should().Be(0);
        links[0].Range.End.Line.Should().Be(0);
        source[links[0].Range.Start.Character..links[0].Range.End.Character].Should().Contain(covered);
        new Uri(links[0].Target!).LocalPath.Should().EndWith(Path.DirectorySeparatorChar + other);
    }

    private static async Task<DocumentLink[]> ReadAsync(ForeignServer server, string name, string source,
        string? other, string? otherSource)
    {
        var token = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), "hexide-links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, source);
            if (other is not null) File.WriteAllText(Path.Combine(directory, other), otherSource);
            using var logging = LoggerFactory.Create(_ => { });
            var transport = new StdioProcessLspTransport(new LspServerInfo(server.Find()!, server.LaunchArguments, directory),
                logging.CreateLogger<StdioProcessLspTransport>());
            await using var client = new VBLspClient(transport, logging.CreateLogger<VBLspClient>(), server.LanguageId);
            await client.StartAsync(token);
            await client.OpenDocumentAsync(LspDocumentUri.ForFile(path), source, isProjectMember: false, cancellationToken: token);
            var links = await client.RequestDocumentLinksAsync(LspDocumentUri.ForFile(path), token) ?? [];
            if (other is not null)
            {
                var expected = LspDocumentUri.ForFile(Path.Combine(directory, other));
                links.Should().Contain(link => link.Target != null && LspDocumentUri.AreSame(link.Target, expected));
            }
            if (other is null && links.Length > 0)
            {
                links[0].Target.Should().NotBeNull();
                LspDocumentUri.AreSame(links[0].Target!, LspDocumentUri.ForFile(path)).Should().BeTrue();
            }
            return links;
        }
        finally { Directory.Delete(directory, true); }
    }
}
