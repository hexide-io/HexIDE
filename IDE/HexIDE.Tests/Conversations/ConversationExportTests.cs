using System.Text;
using System.Text.Json;
using HexIDE.Conversations;
using HexIDE.Redaction;

namespace HexIDE.Tests.Conversations;

/// <summary>
/// The export format: raw JSON-RPC one message per line, plus a manifest that accounts for everything.
/// </summary>
/// <remarks>
/// <b>Two claims are being tested, and the second is the easy one to lose.</b> That the file contains the
/// right bytes, and that it never quietly contains fewer than it should. An export that omitted the
/// envelopes whose bodies were not kept would read exactly like a shorter conversation — which is the
/// failure this whole design refuses, so it is asserted here rather than assumed from the code.
/// </remarks>
public class ConversationExportTests : IAsyncDisposable
{
    private readonly ConversationLog _log = new();

    public async ValueTask DisposeAsync()
    {
        await _log.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private static ConversationRedactor Redactor(bool pseudonymise = true) =>
        new(new Pseudonymiser(new Random(21)), pseudonymise);

    private static readonly TimeProvider FixedClock =
        new FrozenClock(new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero));

    private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>
    /// Records one notification exactly as the tap does, arming gate included.
    /// </summary>
    /// <remarks>
    /// <b>The gate is asked here rather than assumed, and that matters.</b> The tap calls
    /// <see cref="ConversationLog.ShouldKeepBody"/> before handing bytes over, so a helper that always
    /// passed them would exercise a path production never takes — and every assertion about an envelope
    /// with no body would be unreachable.
    /// </remarks>
    private void Note(string connectionId, string method, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var keep = _log.ShouldKeepBody(connectionId);

        _log.Record(connectionId, ConversationDirection.Sent, ConversationEntryKind.Notification,
            method, null, bytes.Length, keep ? bytes : null);
    }

    private static JsonDocument Manifest(ConversationExport export) => JsonDocument.Parse(export.Manifest);

    // ── The lines ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EachBodyIsOneLineOfRealJsonRpc()
    {
        _log.Arm("vb6", true);
        Note("vb6", "a", """{"jsonrpc":"2.0","method":"a","params":{}}""");
        Note("vb6", "b", """{"jsonrpc":"2.0","method":"b","params":{}}""");

        var export = await ConversationExporter.ExportAsync(_log, Redactor(), clock: FixedClock);

        var lines = export.Messages.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);

        foreach (var line in lines)
        {
            using var parsed = JsonDocument.Parse(line);
            parsed.RootElement.GetProperty("jsonrpc").GetString().Should().Be("2.0",
                "a line is a protocol message, which is the entire reason for choosing this format");
        }
    }

    [Fact]
    public async Task AnEnvelopeWithNoBodyStillGetsALine()
    {
        // THE assertion about honesty. Nothing was armed, so no body was kept — and a file that simply
        // omitted these rows would be indistinguishable from a quieter conversation.
        for (var i = 0; i < ConversationLog.OpeningFrames + 3; i++)
        {
            Note("vb6", "note", $$"""{"jsonrpc":"2.0","method":"note","params":{{i}}}""");
        }

        var export = await ConversationExporter.ExportAsync(_log, Redactor(), clock: FixedClock);

        export.Lines.Should().Be(ConversationLog.OpeningFrames + 3, "one line per envelope, always");
        export.BodiesAbsent.Should().BeGreaterThan(0, "past the opening allowance nothing was kept");

        var absent = export.Messages.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last();
        using var parsed = JsonDocument.Parse(absent);
        parsed.RootElement.GetProperty("hexide").GetString().Should().Be("no-body",
            "valid JSON under a hexide key, so a replayer can tell it from a protocol message with one "
          + "field test rather than by guessing");
    }

    [Fact]
    public async Task ATruncatedBodyIsNotPassedOffAsAMessage()
    {
        // Joining a head to a tail produces something that parses and is a lie about what crossed the
        // wire. Stating both halves and the true length is the honest shape.
        await using var log = new ConversationLog(new CaptureLimits(FrameBytes: 1024));
        log.Arm("vb6", true);

        var big = Encoding.UTF8.GetBytes($"{{\"jsonrpc\":\"2.0\",\"result\":\"{new string('x', 4000)}\"}}");
        log.Record("vb6", ConversationDirection.Received, ConversationEntryKind.Response,
            "big", "1", big.Length, big);

        var export = await ConversationExporter.ExportAsync(log, Redactor(), clock: FixedClock);

        using var parsed = JsonDocument.Parse(export.Messages.Trim());
        parsed.RootElement.GetProperty("hexide").GetString().Should().Be("truncated");
        parsed.RootElement.GetProperty("trueLength").GetInt32().Should().Be(big.Length,
            "the true length is the one fact a truncated record must carry");
        parsed.RootElement.TryGetProperty("head", out _).Should().BeTrue();
        parsed.RootElement.TryGetProperty("tail", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ABodyTheServerPrettyPrintedIsFlattenedAndSaidToHaveBeen()
    {
        _log.Arm("vb6", true);
        Note("vb6", "pretty", "{\n  \"jsonrpc\": \"2.0\",\n  \"method\": \"pretty\"\n}");

        var export = await ConversationExporter.ExportAsync(_log, Redactor(), clock: FixedClock);

        export.Messages.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(1,
            "one message per line is the format's only hard promise");
        export.BodiesReserialized.Should().Be(1,
            "it is no longer the exact bytes, and a reader must be told rather than left to assume");

        using var parsed = JsonDocument.Parse(export.Messages.Trim());
        parsed.RootElement.GetProperty("method").GetString().Should().Be("pretty");
    }

    [Fact]
    public async Task ABodyThisClientCouldNotDecodeStillLeaves()
    {
        // Which is exactly when an export earns its keep. A format that needed a decoder would drop the
        // one message somebody is trying to ask about.
        _log.Arm("vb6", true);
        Note("vb6", "broken", "{\"jsonrpc\":\"2.0\",\n\"method\":\"broken\",,,");

        var export = await ConversationExporter.ExportAsync(_log, Redactor(), clock: FixedClock);

        export.Messages.Should().Contain("broken");
        export.Messages.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(1);
    }

    // ── The manifest ─────────────────────────────────────────────────────────

    [Fact]
    public async Task EveryEnvelopeNamesTheLineItsBodyIsOn()
    {
        _log.Arm("vb6", true);
        Note("vb6", "a", """{"jsonrpc":"2.0","method":"a"}""");
        Note("vb6", "b", """{"jsonrpc":"2.0","method":"b"}""");
        Note("vb6", "c", """{"jsonrpc":"2.0","method":"c"}""");

        var export = await ConversationExporter.ExportAsync(_log, Redactor(), clock: FixedClock);
        var lines = export.Messages.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        using var manifest = Manifest(export);
        var envelopes = manifest.RootElement.GetProperty("envelopes").EnumerateArray().ToList();
        envelopes.Should().HaveCount(3);

        foreach (var envelope in envelopes)
        {
            var line = envelope.GetProperty("line").GetInt32();
            line.Should().BeInRange(1, lines.Length);
            lines[line - 1].Should().Contain($"\"method\":\"{envelope.GetProperty("method").GetString()}\"",
                "the correlation between the table and the file has to be exact, or neither is usable");
        }
    }

    [Fact]
    public async Task LatencyReachesTheManifest()
    {
        // For an author this is the difference between a bug report and an anecdote, so it is version one
        // and it has to survive the export rather than living only in the window.
        _log.Arm("vb6", true);

        var request = Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":1,"method":"hover"}""");
        _log.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Request,
            "hover", "1", request.Length, request);

        var response = Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":1,"result":null}""");
        _log.Record("vb6", ConversationDirection.Received, ConversationEntryKind.Response,
            null, "1", response.Length, response);

        var export = await ConversationExporter.ExportAsync(_log, Redactor(), clock: FixedClock);

        using var manifest = Manifest(export);
        var envelope = manifest.RootElement.GetProperty("envelopes").EnumerateArray().Single();

        envelope.GetProperty("method").GetString().Should().Be("hover");
        envelope.GetProperty("outcome").GetString().Should().Be(nameof(ConversationOutcome.Answered));
        envelope.TryGetProperty("elapsedMs", out _).Should().BeTrue();
    }

    [Fact]
    public async Task TheManifestSaysWhetherItWasRedacted()
    {
        _log.Arm("vb6", true);
        Note("vb6", "a", """{"jsonrpc":"2.0","method":"a"}""");

        var on = await ConversationExporter.ExportAsync(_log, Redactor(), clock: FixedClock);
        var off = await ConversationExporter.ExportAsync(_log, Redactor(pseudonymise: false), clock: FixedClock);

        using var redacted = Manifest(on);
        redacted.RootElement.GetProperty("redaction").GetProperty("pseudonymised").GetBoolean()
            .Should().BeTrue();

        using var verbatim = Manifest(off);
        verbatim.RootElement.GetProperty("redaction").GetProperty("pseudonymised").GetBoolean()
            .Should().BeFalse();
        verbatim.RootElement.GetProperty("redaction").GetProperty("note").GetString()
            .Should().Contain("NOT REDACTED",
                "an export that does not say whether it was redacted is worse than one that never was, "
              + "because the reader cannot tell which they are holding");
    }

    [Fact]
    public async Task PathsDoNotSurviveIntoTheExport()
    {
        _log.Arm("vb6", true);
        Note("vb6", "didOpen",
            """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///C:/Users/quintana/Ledger/Form1.frm"}}}""");

        var export = await ConversationExporter.ExportAsync(_log, Redactor(), clock: FixedClock);

        export.Messages.Should().NotContain("quintana").And.NotContain("Ledger");
        export.Messages.Should().Contain(".frm", "the extension is a diagnosis, not a name");
    }

    [Fact]
    public async Task LossesAreStatedPerConnectionRatherThanInferredFromAShortFile()
    {
        _log.Arm("vb6", true);
        Note("vb6", "a", """{"jsonrpc":"2.0","method":"a"}""");
        Note("latex", "b", """{"jsonrpc":"2.0","method":"b"}""");

        var export = await ConversationExporter.ExportAsync(_log, Redactor(), clock: FixedClock);

        using var manifest = Manifest(export);
        var connections = manifest.RootElement.GetProperty("connections").EnumerateArray().ToList();

        connections.Should().HaveCount(2, "one number across several servers reports a loss nobody can "
                                        + "attribute to anything");
        connections.Select(c => c.GetProperty("id").GetString()).Should().BeEquivalentTo(["latex", "vb6"]);

        foreach (var connection in connections)
        {
            connection.TryGetProperty("envelopesDropped", out _).Should().BeTrue();
            connection.TryGetProperty("bodiesEvicted", out _).Should().BeTrue();
            connection.TryGetProperty("bodiesRefused", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task WhatATransportCannotShowIsCarriedThrough()
    {
        // The capture does not hold this, and an export without it invites a reader to conclude nothing
        // happened — when the truth is that nothing could have been seen.
        Note("socket", "a", """{"jsonrpc":"2.0","method":"a"}""");

        var export = await ConversationExporter.ExportAsync(
            _log, Redactor(),
            unobservable: new Dictionary<string, string?> { ["socket"] = "A WebSocket has no process." },
            clock: FixedClock);

        using var manifest = Manifest(export);
        manifest.RootElement.GetProperty("connections").EnumerateArray().Single()
            .GetProperty("unobservable").GetString().Should().Be("A WebSocket has no process.");
    }

    [Fact]
    public async Task TheCountsInTheManifestAgreeWithTheFile()
    {
        // A manifest that disagrees with its own file is worse than no manifest, because it is the thing a
        // reader trusts when the file is too large to count by hand.
        _log.Arm("vb6", true);
        for (var i = 0; i < 5; i++) Note("vb6", "a", $$"""{"jsonrpc":"2.0","method":"a","params":{{i}}}""");

        var export = await ConversationExporter.ExportAsync(_log, Redactor(), clock: FixedClock);

        using var manifest = Manifest(export);
        var messages = manifest.RootElement.GetProperty("messages");

        var actual = export.Messages.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        messages.GetProperty("lines").GetInt32().Should().Be(actual);
        (messages.GetProperty("bodiesPresent").GetInt32() + messages.GetProperty("bodiesAbsent").GetInt32())
            .Should().Be(actual, "every line is one or the other, and nothing is in neither");
    }

    [Fact]
    public async Task OneConnectionCanBeExportedOnItsOwn()
    {
        _log.Arm("vb6", true);
        _log.Arm("latex", true);
        Note("vb6", "a", """{"jsonrpc":"2.0","method":"a"}""");
        Note("latex", "b", """{"jsonrpc":"2.0","method":"b"}""");

        var export = await ConversationExporter.ExportAsync(
            _log, Redactor(), connectionId: "vb6", clock: FixedClock);

        export.Lines.Should().Be(1);
        export.Messages.Should().Contain("\"a\"").And.NotContain("\"b\"");
    }

    [Fact]
    public async Task TheFormatSaysWhatItIs()
    {
        var export = await ConversationExporter.ExportAsync(_log, Redactor(), clock: FixedClock);

        using var manifest = Manifest(export);
        var header = manifest.RootElement.GetProperty("hexide");

        header.GetProperty("export").GetString().Should().Be("language-server-conversation");
        header.GetProperty("formatVersion").GetInt32().Should().Be(ConversationExporter.FormatVersion);
        header.GetProperty("exportedAt").GetString().Should().StartWith("2026-09-11T12:00:00");
    }

    [Fact]
    public async Task OneSessionNamesTheSamePathTheSameWayInEveryExport()
    {
        // Why the pseudonym mapping is a session singleton rather than made per export. Two exports of one
        // conversation have to agree about what each path is called, or comparing them — which is most of
        // what somebody exports twice for — is impossible.
        _log.Arm("vb6", true);
        Note("vb6", "didOpen",
            """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"uri":"file:///C:/Users/quintana/Ledger/Form1.frm"}}""");

        var shared = new Pseudonymiser();

        var first = await ConversationExporter.ExportAsync(
            _log, new ConversationRedactor(shared), clock: FixedClock);
        var second = await ConversationExporter.ExportAsync(
            _log, new ConversationRedactor(shared), clock: FixedClock);

        second.Messages.Should().Be(first.Messages,
            "one mapping for the session is what makes two exports comparable");

        var separate = await ConversationExporter.ExportAsync(
            _log, new ConversationRedactor(new Pseudonymiser()), clock: FixedClock);

        separate.Messages.Should().NotBe(first.Messages,
            "and a different session must NOT agree, or a pseudonym would correlate across captures — "
          + "which is the weakness the session scope exists to bound");
    }

    [Fact]
    public async Task AnEmptyCaptureExportsAnEmptyFileAndAValidManifest()
    {
        var export = await ConversationExporter.ExportAsync(_log, Redactor(), clock: FixedClock);

        export.Messages.Should().BeEmpty();
        export.Lines.Should().Be(0);

        using var manifest = Manifest(export);
        manifest.RootElement.GetProperty("envelopes").GetArrayLength().Should().Be(0);
    }
}
