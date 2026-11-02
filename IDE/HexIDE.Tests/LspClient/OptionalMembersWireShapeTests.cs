using System.Globalization;
using System.Text;
using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class OptionalMembersWireShapeTests
{
    private static readonly HexIDE.Lsp.Messages.Range Span = new(new Position(0, 0), new Position(0, 1));

    // LSP 3.17's pinned metaModel (LspSpecificationModel), including inherited properties.
    [Theory]
    [InlineData("Diagnostic", "severity")]
    [InlineData("Diagnostic", "source")]
    [InlineData("Diagnostic", "code")]
    [InlineData("Diagnostic", "codeDescription")]
    [InlineData("InitializeParams", "trace")]
    [InlineData("InitializeParams", "locale")]
    [InlineData("LogTraceParams", "verbose")]
    [InlineData("ClientCapabilities", "textDocument")]
    [InlineData("ClientCapabilities", "workspace")]
    [InlineData("ClientCapabilities", "window")]
    [InlineData("TextDocumentClientCapabilities", "publishDiagnostics")]
    [InlineData("TextDocumentClientCapabilities", "hover")]
    [InlineData("TextDocumentClientCapabilities", "synchronization")]
    [InlineData("TextDocumentClientCapabilities", "codeLens")]
    [InlineData("TextDocumentClientCapabilities", "diagnostic")]
    [InlineData("TextDocumentClientCapabilities", "documentSymbol")]
    [InlineData("TextDocumentClientCapabilities", "foldingRange")]
    [InlineData("TextDocumentClientCapabilities", "semanticTokens")]
    [InlineData("WorkspaceClientCapabilities", "executeCommand")]
    [InlineData("WorkspaceClientCapabilities", "workspaceFolders")]
    [InlineData("WorkspaceClientCapabilities", "diagnostics")]
    [InlineData("WorkspaceClientCapabilities", "fileOperations")]
    [InlineData("DocumentDiagnosticParams", "identifier")]
    [InlineData("DocumentDiagnosticParams", "previousResultId")]
    [InlineData("ServerCapabilities", "textDocumentSync")]
    [InlineData("ServerCapabilities", "hoverProvider")]
    [InlineData("ServerCapabilities", "documentSymbolProvider")]
    [InlineData("ServerCapabilities", "foldingRangeProvider")]
    [InlineData("ServerCapabilities", "completionProvider")]
    [InlineData("ServerCapabilities", "signatureHelpProvider")]
    [InlineData("ServerCapabilities", "definitionProvider")]
    [InlineData("ServerCapabilities", "documentHighlightProvider")]
    [InlineData("ServerCapabilities", "renameProvider")]
    [InlineData("ServerCapabilities", "documentFormattingProvider")]
    [InlineData("ServerCapabilities", "codeLensProvider")]
    [InlineData("ServerCapabilities", "diagnosticProvider")]
    [InlineData("ServerCapabilities", "executeCommandProvider")]
    [InlineData("CompletionItem", "detail")]
    [InlineData("CompletionItem", "insertText")]
    [InlineData("SymbolInformation", "containerName")]
    [InlineData("HoverClientCapabilities", "contentFormat")]
    [InlineData("HoverResult", "range")]
    [InlineData("DocumentHighlight", "kind")]
    [InlineData("WorkspaceEdit", "changes")]
    [InlineData("FormattingOptions", "trimTrailingWhitespace")]
    [InlineData("FoldingRange", "startCharacter")]
    [InlineData("FoldingRange", "endCharacter")]
    [InlineData("FoldingRange", "kind")]
    [InlineData("ParameterInformation", "documentation")]
    [InlineData("SignatureInformation", "documentation")]
    [InlineData("SignatureInformation", "parameters")]
    [InlineData("SignatureHelp", "activeSignature")]
    [InlineData("SignatureHelp", "activeParameter")]
    [InlineData("WorkDoneProgressValue", "message")]
    [InlineData("WorkDoneProgressValue", "percentage")]
    public async Task UnsetOptionalMemberIsAbsent(string contract, string member)
    {
        object value = contract switch
        {
            "Diagnostic" => new Diagnostic(Span, "message"),
            "InitializeParams" => new InitializeParams(null, null, new ClientCapabilities()),
            "LogTraceParams" => new LogTraceParams("message"),
            "ClientCapabilities" => new ClientCapabilities(),
            "TextDocumentClientCapabilities" => new TextDocumentClientCapabilities(),
            "WorkspaceClientCapabilities" => new WorkspaceClientCapabilities(),
            "DocumentDiagnosticParams" => new DocumentDiagnosticParams(new TextDocumentIdentifier("file:///probe.bas")),
            "ServerCapabilities" => new ServerCapabilities(),
            "CompletionItem" => new CompletionItem("item", CompletionItemKind.Text),
            "SymbolInformation" => new SymbolInformation("symbol", SymbolKind.Module, new Location("file:///probe.bas", Span)),
            "HoverClientCapabilities" => new HoverClientCapabilities(),
            "HoverResult" => new HoverResult(new MarkupContent("plaintext", "hover")),
            "DocumentHighlight" => new DocumentHighlight(Span),
            "WorkspaceEdit" => new WorkspaceEdit(),
            "FormattingOptions" => new FormattingOptions(4, true),
            "FoldingRange" => new FoldingRange(0, 1),
            "ParameterInformation" => new ParameterInformation("parameter"),
            "SignatureInformation" => new SignatureInformation("signature"),
            "SignatureHelp" => new SignatureHelp([]),
            "WorkDoneProgressValue" => new WorkDoneProgressValue("report"),
            _ => throw new ArgumentOutOfRangeException(nameof(contract))
        };
        using var frame = await FrameAsync(value);
        frame.RootElement.GetProperty("params").TryGetProperty(member, out _).Should().BeFalse(
            $"{contract}.{member} is optional but not nullable in LSP 3.17");
    }

    [Fact]
    public async Task InitializePreservesExplicitlyNullableMembers()
    {
        using var frame = await FrameAsync(new InitializeParams(null, null, new ClientCapabilities()));
        var parameters = frame.RootElement.GetProperty("params");
        foreach (var member in new[] { "processId", "rootUri", "workspaceFolders" })
            parameters.GetProperty(member).ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FormattingPreservesExplicitBoolean(bool trim)
    {
        using var frame = await FrameAsync(new FormattingOptions(4, true, trim));
        frame.RootElement.GetProperty("params").GetProperty("trimTrailingWhitespace").GetBoolean().Should().Be(trim);
    }

    [Fact]
    public async Task FoldingCapabilityPreservesExplicitObject()
    {
        using var frame = await FrameAsync(new TextDocumentClientCapabilities(FoldingRange: new(true)));
        frame.RootElement.GetProperty("params").GetProperty("foldingRange")
            .GetProperty("lineFoldingOnly").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExperimentalPreservesExplicitNull()
    {
        using var frame = await FrameAsync(new ServerCapabilities(Experimental: JsonSerializer.SerializeToElement<object?>(null)));
        frame.RootElement.GetProperty("params").GetProperty("experimental").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private static async Task<JsonDocument> FrameAsync(object parameters)
    {
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        using var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(clientSide, clientSide,
            new SystemTextJsonFormatter { JsonSerializerOptions = new JsonSerializerOptions(LspJsonContext.Default.Options) }));
        await using var peer = serverSide;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var reading = ReadFrameAsync(peer, timeout.Token);
        await rpc.NotifyWithParameterObjectAsync("probe", parameters);
        return JsonDocument.Parse((await reading)!);
    }

    private static async Task<string?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = new List<byte>(64);
        var one = new byte[1];

        while (true)
        {
            if (await stream.ReadAsync(one.AsMemory(0, 1), ct) == 0) return null;
            header.Add(one[0]);
            if (header.Count >= 4
                && header[^4] == (byte)'\r' && header[^3] == (byte)'\n'
                && header[^2] == (byte)'\r' && header[^1] == (byte)'\n')
            {
                break;
            }
        }

        var length = 0;
        foreach (var line in Encoding.ASCII.GetString([.. header]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(line["Content-Length:".Length..].Trim(), CultureInfo.InvariantCulture);
            }
        }

        if (length <= 0) return null;

        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await stream.ReadAsync(body.AsMemory(read, length - read), ct);
            if (n == 0) return null;
            read += n;
        }

        return Encoding.UTF8.GetString(body);
    }

}
