using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

namespace HexIDE.Tests.LspClient;

public class DiagnosticMetadataTests
{
    private static readonly JsonSerializerOptions ClientOptions =
        new(LspJsonContext.Default.Options) { PropertyNameCaseInsensitive = true };

    private const string Range = """{"start":{"line":1,"character":2},"end":{"line":3,"character":4}}""";
    private const string Related = """{"location":{"uri":"file:///other.bas","range":""" + Range + """},"message":"first defined here"}""";

    private static JsonElement Read(bool push, string members)
    {
        var items = "[{\"range\":" + Range + ",\"message\":\"problem\"," + members + "}]";
        Diagnostic diagnostic;
        using (var document = JsonDocument.Parse(items))
        {
            diagnostic = push
                ? JsonSerializer.Deserialize<PublishDiagnosticsParams>(
                    "{\"uri\":\"file:///main.bas\",\"diagnostics\":" + items + "}", ClientOptions)!.Diagnostics.Single()
                : VBLspClient.ReadDiagnostics(document.RootElement).Single();
        }
        return JsonSerializer.SerializeToElement(diagnostic, ClientOptions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothMembersSurviveTheReply(bool push)
    {
        var diagnostic = Read(push, "\"tags\":[1,2],\"relatedInformation\":[" + Related + "," + Related + "]");
        diagnostic.GetProperty("tags").EnumerateArray().Select(x => x.GetInt32()).Should().Equal(1, 2);
        var entries = diagnostic.GetProperty("relatedInformation").EnumerateArray().ToArray();
        entries.Should().HaveCount(2);
        entries[0].GetProperty("location").GetProperty("uri").GetString().Should().Be("file:///other.bas");
        entries[0].GetProperty("location").GetProperty("range").GetProperty("end").GetProperty("line").GetInt32().Should().Be(3);
        entries[1].GetProperty("message").GetString().Should().Be("first defined here");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownTagsAndMalformedRelatedEntriesCostOnlyTheEntry(bool push)
    {
        var diagnostic = Read(push, "\"tags\":[7,1],\"relatedInformation\":[{\"message\":\"no location\"}," + Related + "]");
        diagnostic.GetProperty("tags").EnumerateArray().Select(x => x.GetInt32()).Should().Equal(1);
        diagnostic.GetProperty("relatedInformation").GetArrayLength().Should().Be(1);
        diagnostic.GetProperty("message").GetString().Should().Be("problem");
    }

    [Theory]
    [InlineData(false, "{}")]
    [InlineData(true, "{}")]
    [InlineData(false, "[null,7,{},true]")]
    [InlineData(true, "[null,7,{},true]")]
    public void MalformedMetadataDoesNotLoseTheDiagnostic(bool push, string metadata)
    {
        var diagnostic = Read(push, "\"tags\":" + metadata + ",\"relatedInformation\":" + metadata);
        diagnostic.GetProperty("message").GetString().Should().Be("problem");
        var tags = diagnostic.GetProperty("tags");
        if (tags.ValueKind != JsonValueKind.Null) tags.GetArrayLength().Should().Be(0);
        var related = diagnostic.GetProperty("relatedInformation");
        if (related.ValueKind != JsonValueKind.Null) related.GetArrayLength().Should().Be(0);
    }

}
