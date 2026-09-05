using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// What a save notification actually looks like on the wire.
///
/// <para>
/// Serialized through the real generated context rather than a default <see cref="JsonSerializer"/>,
/// because that context is what the client uses and its resolver ends in <c>return null</c> — a type
/// missing from it throws rather than falling back to reflection, under plain JIT as much as under
/// ahead-of-time compilation. That throw lands in a debug-level catch, so the omission would present as
/// a server that connects, initializes and answers nothing: this issue's own symptom, reproduced by a
/// fix that appeared to work.
/// </para>
/// </summary>
public class DidSaveWireShapeTests
{
    private static JsonElement Serialize(DidSaveTextDocumentParams p) =>
        JsonDocument.Parse(JsonSerializer.Serialize(
            p, typeof(DidSaveTextDocumentParams), LspJsonContext.Default)).RootElement;

    [Fact]
    public void ItSerializesThroughTheExactOptionsTheClientPutsOnTheWire()
    {
        // Built the way VBLspClient builds them — a copy of the context's options with case-insensitive
        // reading — rather than by asking the context whether it knows the type. That distinction is not
        // pedantry: asking `LspJsonContext.Default.GetTypeInfo(...)` still answers for an UNREGISTERED
        // type, so a test written that way passes with the registration deleted. Verified by deleting it.
        var options = new JsonSerializerOptions(LspJsonContext.Default.Options)
        {
            PropertyNameCaseInsensitive = true,
        };

        var serialize = () => JsonSerializer.Serialize(
            new DidSaveTextDocumentParams(new TextDocumentIdentifier("file:///a.md")), options);

        serialize.Should().NotThrow(
            "the client's options resolve through the generated context alone, with no reflection "
          + "fallback chained behind it, so an unregistered type throws — into a catch that logs at "
          + "debug level and leaves a server that connects, initializes and answers nothing");
    }

    [Fact]
    public void WithNoTextTheFieldIsAbsentEntirely()
    {
        // THE assertion this file exists for. A server tests whether `text` is present to choose between
        // reading the file itself and using what it was handed; `"text":null` is present, so it takes the
        // has-text branch and finds nothing there. Serializer options ignore nothing by default, so
        // without the per-property condition this is exactly what would be sent.
        var json = Serialize(new DidSaveTextDocumentParams(new TextDocumentIdentifier("file:///a.md")));

        json.TryGetProperty("text", out _).Should().BeFalse();
        json.GetProperty("textDocument").GetProperty("uri").GetString().Should().Be("file:///a.md");
    }

    [Fact]
    public void WithTextTheFieldCarriesItVerbatim()
    {
        var json = Serialize(new DidSaveTextDocumentParams(
            new TextDocumentIdentifier("file:///a.md"), "# hello\r\n"));

        json.GetProperty("text").GetString().Should().Be("# hello\r\n");
    }

    [Fact]
    public void AnEmptyDocumentStillSendsItsTextRatherThanOmittingIt()
    {
        // Empty is a value, not an absence: a server told a document was saved with no text field will go
        // and read the file, and would find the emptiness anyway — but one told `"text":""` knows the
        // buffer is empty without looking. Only null means "not supplied".
        var json = Serialize(new DidSaveTextDocumentParams(
            new TextDocumentIdentifier("file:///a.md"), ""));

        json.TryGetProperty("text", out var text).Should().BeTrue();
        text.GetString().Should().BeEmpty();
    }

    [Fact]
    public void TheIdentifierCarriesNoVersion()
    {
        // The unversioned identifier, deliberately. A save changes no content, so there is no new version
        // to report, and inventing one would let a server believe it had missed a change.
        var json = Serialize(new DidSaveTextDocumentParams(new TextDocumentIdentifier("file:///a.md")));

        json.GetProperty("textDocument").TryGetProperty("version", out _).Should().BeFalse();
    }
}
