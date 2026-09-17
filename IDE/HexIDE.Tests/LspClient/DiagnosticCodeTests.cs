using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// A diagnostic's <c>code</c>, on both of the paths a diagnostic arrives by.
/// </summary>
/// <remarks>
/// <para>
/// The field was discarded for every server on every path, so a server naming the rule that fired — ruff's
/// <c>F401</c>, rumdl's <c>MD012</c> — handed us an identifier and a documentation URL that nothing in
/// HexIDE could see (hexide-io/HexIDE#426).
/// </para>
/// <para>
/// <b>Both paths are tested because they lose it differently.</b> The pull path reads named properties, so
/// it discarded the member explicitly. The push path never reaches that reader: a notification is
/// deserialized straight into <see cref="PublishDiagnosticsParams"/>, so the member was dropped by the
/// record's shape. A fix to either alone leaves the other exactly as it was.
/// </para>
/// </remarks>
public class DiagnosticCodeTests
{
    /// <summary>The options the client actually reads notifications with.</summary>
    private static readonly JsonSerializerOptions ClientOptions =
        new(LspJsonContext.Default.Options) { PropertyNameCaseInsensitive = true };

    private static Diagnostic[] Pull(string itemsJson) =>
        VBLspClient.ReadDiagnostics(JsonDocument.Parse(itemsJson).RootElement);

    private static PublishDiagnosticsParams Push(string paramsJson) =>
        JsonSerializer.Deserialize<PublishDiagnosticsParams>(paramsJson, ClientOptions)!;

    /// <summary>A well-formed range, so nothing here fails for an unrelated reason.</summary>
    /// <remarks>
    /// Concatenated rather than interpolated into a raw literal: these payloads end in several consecutive
    /// closing braces, which an interpolated raw literal reads as its own escape.
    /// </remarks>
    private const string ARange = @"{""start"":{""line"":0,""character"":0},""end"":{""line"":0,""character"":9}}";

    private static string OneItem(string members) => "[{\"range\":" + ARange + "," + members + "}]";

    private static string OneParams(string members) =>
        "{\"uri\":\"file:///c:/x.bas\",\"diagnostics\":" + OneItem(members) + "}";

    [Fact]
    public void ThePullPathKeepsAStringCode()
    {
        var read = Pull(OneItem(
            @"""message"":""'os' imported but unused"",""severity"":2,""source"":""Ruff"",""code"":""F401"","
          + @"""codeDescription"":{""href"":""https://docs.astral.sh/ruff/rules/unused-import""}"));

        var diagnostic = read.Should().ContainSingle().Subject;
        diagnostic.CodeText.Should().Be("F401");
        diagnostic.CodeDescription!.Href.Should().Be("https://docs.astral.sh/ruff/rules/unused-import");
    }

    [Fact]
    public void ThePushPathKeepsAStringCode()
    {
        var published = Push(OneParams(
            @"""message"":""Multiple consecutive blank lines"",""source"":""rumdl"",""code"":""MD012"","
          + @"""codeDescription"":{""href"":""https://rumdl.dev/md012/""}"));

        var diagnostic = published.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.CodeText.Should().Be("MD012");
        diagnostic.CodeDescription!.Href.Should().Be("https://rumdl.dev/md012/");
    }

    [Theory]
    [InlineData("1042")]
    [InlineData("0")]
    public void AnIntegerCodeIsKeptAsItsDigits(string code)
    {
        // `integer | string` in the specification, which is why this is a JsonElement and not a string. A
        // server using numeric codes must be as usable as one using names, and — the sharper half — must
        // not make the whole notification throw on the way in.
        var pulled = Pull(OneItem(@"""message"":""x"",""code"":" + code));
        var pushed = Push(OneParams(@"""message"":""x"",""code"":" + code));

        pulled.Should().ContainSingle().Which.CodeText.Should().Be(code);
        pushed.Diagnostics.Should().ContainSingle().Which.CodeText.Should().Be(code);
    }

    [Fact]
    public void ADiagnosticWithNoCodeIsStillADiagnostic()
    {
        // Most servers send none, and the bundled VB6 server sends none, so this is the common path.
        var pulled = Pull(OneItem(@"""message"":""Syntax error"",""severity"":1"));

        var diagnostic = pulled.Should().ContainSingle().Subject;
        diagnostic.CodeText.Should().BeNull();
        diagnostic.CodeDescription.Should().BeNull();
        diagnostic.Message.Should().Be("Syntax error");
    }

    [Theory]
    [InlineData(@"{""nested"":""object""}")]
    [InlineData(@"[""an"",""array""]")]
    [InlineData("null")]
    public void ACodeInAShapeNobodyCanRenderReadsAsNoCode(string code)
    {
        // Field by field, as every foreign reply is read here: a server must not be able to cost us the
        // whole diagnostic by sending a member in a shape we did not anticipate.
        var pulled = Pull(OneItem(@"""message"":""still a diagnostic"",""code"":" + code));
        var pushed = Push(OneParams(@"""message"":""still a diagnostic"",""code"":" + code));

        pulled.Should().ContainSingle().Which.CodeText.Should().BeNull();
        pulled.Should().ContainSingle().Which.Message.Should().Be("still a diagnostic");
        pushed.Diagnostics.Should().ContainSingle().Which.CodeText.Should().BeNull();
    }

    [Fact]
    public void ACodeOutlivesTheReplyItArrivedIn()
    {
        // THE ONE THAT PINS THE CLONE. A JsonElement belongs to the JsonDocument it was parsed from, and
        // the pull path's document is disposed when the reply has been read. Without a clone this throws
        // ObjectDisposedException on the first read by whoever kept the diagnostic — which is everyone,
        // since diagnostics are held by the ledger for the life of the document.
        Diagnostic diagnostic;
        using (var document = JsonDocument.Parse(OneItem(@"""message"":""x"",""code"":""VBC00001""")))
        {
            diagnostic = VBLspClient.ReadDiagnostics(document.RootElement).Single();
        }

        diagnostic.CodeText.Should().Be("VBC00001");
    }
}
