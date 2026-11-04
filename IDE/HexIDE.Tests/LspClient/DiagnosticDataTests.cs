using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

namespace HexIDE.Tests.LspClient;

public class DiagnosticDataTests
{
    private static readonly JsonSerializerOptions ClientOptions =
        new(LspJsonContext.Default.Options) { PropertyNameCaseInsensitive = true };

    [Theory]
    [InlineData(false, "{\"token\": [1, true, null], \"escaped\":\"\\u0041\", \"number\":1.00}")]
    [InlineData(true, "{\"token\": [1, true, null], \"escaped\":\"\\u0041\", \"number\":1.00}")]
    [InlineData(false, "[1,\"token\",false]")]
    [InlineData(true, "[1,\"token\",false]")]
    [InlineData(false, "\"opaque\"")]
    [InlineData(true, "\"opaque\"")]
    [InlineData(false, "42")]
    [InlineData(true, "42")]
    [InlineData(false, "true")]
    [InlineData(true, "true")]
    [InlineData(false, "null")]
    [InlineData(true, "null")]
    public void DataOutlivesTheReplyAndSerializesUnchanged(bool push, string data)
    {
        var items = "[{\"range\":{\"start\":{\"line\":0,\"character\":0},\"end\":{\"line\":0,\"character\":1}},\"message\":\"problem\",\"data\":" + data + "}]";
        Diagnostic diagnostic;
        using (var document = JsonDocument.Parse(items))
        {
            diagnostic = push
                ? JsonSerializer.Deserialize<PublishDiagnosticsParams>(
                    "{\"uri\":\"file:///main.bas\",\"diagnostics\":" + items + "}", ClientOptions)!.Diagnostics.Single()
                : VBLspClient.ReadDiagnostics(document.RootElement).Single();
        }

        // Assert the serialized payload, not a typed interpretation of the server's token.
        var serialized = JsonSerializer.Serialize(diagnostic, ClientOptions);
        using var roundTrip = JsonDocument.Parse(serialized);
        roundTrip.RootElement.GetProperty("data").GetRawText().Should().Be(data);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAbsentTokenRemainsAbsent(bool push)
    {
        const string items = """[{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}},"message":"problem"}]""";
        using var document = JsonDocument.Parse(items);
        var diagnostic = push
            ? JsonSerializer.Deserialize<PublishDiagnosticsParams>(
                "{\"uri\":\"file:///main.bas\",\"diagnostics\":" + items + "}", ClientOptions)!.Diagnostics.Single()
            : VBLspClient.ReadDiagnostics(document.RootElement).Single();
        var serialized = JsonSerializer.SerializeToElement(diagnostic, ClientOptions);
        serialized.TryGetProperty("data", out _).Should().BeFalse();
    }

}
