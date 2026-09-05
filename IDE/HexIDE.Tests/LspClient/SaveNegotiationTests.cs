using System.Text.Json;
using HexIDE.Lsp.Messages;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// Reading whether a server wants to be told about saves, and whether it wants the text.
///
/// <para>
/// The whole feature turns on this one reader. Answer "no" too readily and a server that defers its
/// analysis to save stays silent for a reason its author cannot see — which is the defect the change
/// exists to fix. Answer "yes" too readily and a server is sent a notification it never asked for, which
/// costs nothing but is not what it agreed to.
/// </para>
/// </summary>
public class SaveNegotiationTests
{
    private static JsonElement Capabilities(string json) => JsonDocument.Parse(json).RootElement;

    private static SaveNotification Read(string capabilitiesJson) =>
        ServerCapabilities.ReadSave(Capabilities(capabilitiesJson));

    // ── The object form: opt in explicitly ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("""{"textDocumentSync":{"openClose":true,"change":1,"save":true}}""")]
    [InlineData("""{"textDocumentSync":{"openClose":true,"change":1,"save":{}}}""")]
    [InlineData("""{"textDocumentSync":{"openClose":true,"change":1,"save":{"includeText":false}}}""")]
    public void AServerThatAsksForSavesWithoutTextGetsThem(string capabilities)
    {
        // An options object means yes and then says how — the same rule the other capabilities use, except
        // that here "how" is a question we have to answer rather than ignore.
        Read(capabilities).Should().Be(SaveNotification.WithoutText);
    }

    [Fact]
    public void AServerThatAsksForTheTextGetsIt()
    {
        Read("""{"textDocumentSync":{"openClose":true,"change":1,"save":{"includeText":true}}}""")
            .Should().Be(SaveNotification.WithText);
    }

    [Theory]
    [InlineData("""{"textDocumentSync":{"openClose":true,"change":1}}""")]
    [InlineData("""{"textDocumentSync":{"openClose":true,"change":1,"save":false}}""")]
    [InlineData("""{"textDocumentSync":{}}""")]
    public void AnObjectThatDoesNotMentionSaveGetsNone(string capabilities)
    {
        // DEFAULT-DENY, and the opposite polarity to both neighbouring readers: an object with no
        // `openClose` still gets opens, one with no `change` still gets changes, one with no `save` gets
        // no saves. Deliberate — open and change describe a default the server may narrow, while save is
        // an opt-in it has to state.
        Read(capabilities).Should().Be(SaveNotification.None);
    }

    // ── The bare number form: the deliberate divergence ───────────────────────────────────────────────

    [Theory]
    [InlineData("""{"textDocumentSync":1}""")]
    [InlineData("""{"textDocumentSync":2}""")]
    public void ABareSyncKindCountsAsAskingForSavesWithoutText(string capabilities)
    {
        // A divergence from a strict reading, and the argument for it is in the reader's own comment: a
        // number carries no options object, so literally there is no `save` — but the reference
        // implementation resolves a non-zero kind to `{openClose, change, save:{includeText:false}}`, and
        // that is what server authors test against. The strict reading would leave this issue's exact
        // symptom in place for every server that answers with a number.
        Read(capabilities).Should().Be(SaveNotification.WithoutText);
    }

    [Fact]
    public void SyncKindZeroMeansSendNothingAtAll()
    {
        // Kind 0 is the server saying it wants no document synchronization. A save is something.
        Read("""{"textDocumentSync":0}""").Should().Be(SaveNotification.None);
    }

    [Fact]
    public void TheNumberFormIsReadTheSameWayHereAsForOpenAndClose()
    {
        // The consistency that makes the divergence defensible rather than arbitrary. Taking the strict
        // reading here and the ecosystem one next door is the combination with no defence, so this pins
        // that they agree.
        var oneNumber = Capabilities("""{"textDocumentSync":1}""");

        ServerCapabilities.AcceptsOpenClose(oneNumber).Should().BeTrue();
        ServerCapabilities.ReadSave(oneNumber).Should().NotBe(SaveNotification.None);
    }

    // ── Nothing to read ───────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"hoverProvider":true}""")]
    [InlineData("""{"textDocumentSync":null}""")]
    [InlineData("""{"textDocumentSync":"full"}""")]
    public void AnythingElseMeansNoSaves(string capabilities)
    {
        // Including a shape the protocol does not define. An unrecognised answer is not consent, and
        // guessing at one is how a client ends up sending a server something it never agreed to.
        Read(capabilities).Should().Be(SaveNotification.None);
    }

    [Fact]
    public void NoCapabilitiesAtAllMeansNoSaves()
    {
        // Before the handshake completes, and for a server that never answered.
        ServerCapabilities.ReadSave(null).Should().Be(SaveNotification.None);
    }

    // ── The client's own half ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheClientDeclaresItCanSendSaveNotifications()
    {
        // The half that is easy to forget, because our own server ignores client capabilities entirely and
        // answers the same to everyone — so omitting this would break only against a server we did not
        // write, and would present as a server that simply never asks for saves.
        var capabilities = new ClientCapabilities(
            new TextDocumentClientCapabilities(
                Synchronization: new TextDocumentSyncClientCapabilities(DidSave: true)));

        var json = JsonDocument.Parse(JsonSerializer.Serialize(capabilities)).RootElement;

        json.GetProperty("textDocument").GetProperty("synchronization")
            .GetProperty("didSave").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void TheClientClaimsNothingItDoesNotImplement()
    {
        // willSave and willSaveWaitUntil are absent on purpose. Claiming a capability nothing implements
        // is the same defect as failing to claim one, pointed the other way — and a server may reasonably
        // wait on a willSaveWaitUntil response that will never come.
        var json = JsonDocument.Parse(JsonSerializer.Serialize(
            new TextDocumentSyncClientCapabilities(DidSave: true))).RootElement;

        json.TryGetProperty("willSave", out _).Should().BeFalse();
        json.TryGetProperty("willSaveWaitUntil", out _).Should().BeFalse();
    }
}
