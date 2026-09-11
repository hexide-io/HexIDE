using System.Text;
using System.Text.Json;
using HexIDE.Redaction;

namespace HexIDE.Conversations;

/// <summary>What an export produced, ready for a caller to write wherever it writes things.</summary>
/// <param name="Manifest">The description of the capture: limits, losses, and the envelope table.</param>
/// <param name="Messages">One line per envelope, in the order they happened.</param>
/// <param name="Lines">How many lines <paramref name="Messages"/> holds. One per envelope, always.</param>
/// <param name="BodiesPresent">Lines that are a real protocol message.</param>
/// <param name="BodiesAbsent">Lines standing in for an envelope whose body was never kept or has gone.</param>
/// <param name="BodiesReserialized">
/// Whole bodies that had to be compacted onto one line, so a reader knows those are not byte-exact.
/// </param>
public sealed record ConversationExport(
    string Manifest,
    string Messages,
    int Lines,
    int BodiesPresent,
    int BodiesAbsent,
    int BodiesReserialized);

/// <summary>
/// Writes a capture out as raw JSON-RPC, one message per line, plus a manifest.
/// </summary>
/// <remarks>
/// <b>Why this format and not a richer one.</b> It needs no decoder, it greps and it pipes, it survives a
/// message this client could not decode — which is exactly when an export matters — and it replays straight
/// into this project's own pipe-pair tests. The archived Microsoft LSP Inspector's envelope was considered
/// and rejected: the viewer no longer exists, and its published format and its reference implementation
/// disagree about the message-type strings, so adopting it would inherit an inconsistency that later reads
/// as a HexIDE defect. A SQLite schema whose own authors decline to stabilise it was rejected too.
///
/// <para>
/// <b>Two files rather than one, because a message alone is not the finding.</b> Latency, size, direction
/// and outcome live in the envelope, and for a server author latency is the difference between a bug report
/// and an anecdote. Folding that metadata into each line would stop the lines being protocol messages,
/// which is the one property that makes this format worth choosing. So the manifest carries the envelope
/// table and each row names the <em>line number</em> its body is on — exact, and greppable with
/// <c>sed -n '42p'</c>.
/// </para>
///
/// <para>
/// <b>Every envelope gets a line, including the ones with nothing to show.</b> A file that silently omitted
/// them would read exactly like a conversation that never contained them, which is the failure this design
/// refuses everywhere else. Those lines are valid JSON under a <c>hexide</c> key rather than a
/// <c>jsonrpc</c> one, so a replayer can tell them apart with a single field test and the counts in the
/// manifest say how many there were.
/// </para>
/// </remarks>
public static class ConversationExporter
{
    /// <summary>The format's own version, so a reader can tell what it is holding.</summary>
    public const int FormatVersion = 1;

    /// <param name="log">The capture. Drained first, so an export never races the pump.</param>
    /// <param name="redactor">
    /// Applied to every body and every URI on the way out. Its state is recorded in the manifest, because
    /// an export that does not say whether it was redacted is worse than one that never was: the reader
    /// cannot tell which they are holding.
    /// </param>
    /// <param name="connectionId">One connection, or null for the whole interleaved timeline.</param>
    /// <param name="unobservable">
    /// Per connection, what its transport cannot show — a named pipe HexIDE did not spawn has no process
    /// or standard error, a WebSocket has no stream at all. Carried here because the capture does not hold
    /// it and an export without it invites a reader to conclude nothing happened.
    /// </param>
    public static async Task<ConversationExport> ExportAsync(
        ConversationLog log,
        ConversationRedactor redactor,
        string? connectionId = null,
        IReadOnlyDictionary<string, string?>? unobservable = null,
        TimeProvider? clock = null)
    {
        await log.DrainAsync().ConfigureAwait(false);

        var envelopes = log.Snapshot(connectionId);
        var messages = new StringBuilder();
        var rows = new List<Row>(envelopes.Count);

        var present = 0;
        var absent = 0;
        var reserialized = 0;

        foreach (var envelope in envelopes)
        {
            var body = log.Body(envelope.ConnectionId, envelope.Sequence);
            string line;

            if (body is null)
            {
                line = """{"hexide":"no-body"}""";
                absent++;
            }
            else if (body.IsTruncated)
            {
                // A truncated body is not a protocol message and must not pretend to be one. Joining the
                // head to the tail would produce something that parses and is a lie about what crossed the
                // wire; stating both halves and the true length is the honest shape, and it is the
                // precedent the automation driver already sets for truncated output.
                line = Truncated(body, redactor);
                absent++;
            }
            else
            {
                var text = redactor.Body(Encoding.UTF8.GetString(body.Head));
                if (text.AsSpan().IndexOfAny('\n', '\r') >= 0)
                {
                    // A newline outside a string is insignificant whitespace in JSON, so compacting is
                    // lossless as far as the message is concerned — but it is no longer the exact bytes,
                    // and the manifest says so rather than letting a reader assume otherwise.
                    text = Compacted(text);
                    reserialized++;
                }

                line = text;
                present++;
            }

            messages.Append(line).Append('\n');
            rows.Add(new Row(envelope, rows.Count + 1));
        }

        return new ConversationExport(
            Manifest: WriteManifest(log, redactor, rows, unobservable, clock ?? TimeProvider.System,
                                    present, absent, reserialized),
            Messages: messages.ToString(),
            Lines: rows.Count,
            BodiesPresent: present,
            BodiesAbsent: absent,
            BodiesReserialized: reserialized);
    }

    private readonly record struct Row(ConversationEnvelope Envelope, int Line);

    private static string Truncated(StoredBody body, ConversationRedactor redactor)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("hexide", "truncated");
            writer.WriteNumber("trueLength", body.TrueLength);
            writer.WriteString("head", redactor.Body(Encoding.UTF8.GetString(body.Head)));
            writer.WriteString("tail", redactor.Body(Encoding.UTF8.GetString(body.Tail!)));
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string Compacted(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer)) document.WriteTo(writer);
            return Encoding.UTF8.GetString(buffer.ToArray());
        }
        catch (JsonException)
        {
            // A body this client could not decode is precisely when an export earns its keep, so it still
            // goes out — flattened rather than dropped, and still on one line.
            return json.Replace('\r', ' ').Replace('\n', ' ');
        }
    }

    private static string WriteManifest(
        ConversationLog log,
        ConversationRedactor redactor,
        List<Row> rows,
        IReadOnlyDictionary<string, string?>? unobservable,
        TimeProvider clock,
        int present,
        int absent,
        int reserialized)
    {
        var buffer = new MemoryStream();
        var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();

        writer.WriteStartObject("hexide");
        writer.WriteString("export", "language-server-conversation");
        writer.WriteNumber("formatVersion", FormatVersion);
        writer.WriteString("exportedAt", clock.GetUtcNow().ToString("O"));
        writer.WriteEndObject();

        writer.WriteStartObject("redaction");
        writer.WriteBoolean("pseudonymised", redactor.IsPseudonymising);
        writer.WriteNumber("distinctValuesNamed", redactor.NamedValues);
        writer.WriteString("note", redactor.IsPseudonymising
            ? "Paths, workspace folders and server launch configuration have been replaced with stable "
            + "session pseudonyms. Two names differing only in capitalisation were two different strings."
            : "NOT REDACTED. Real paths and real server launch configuration are present, including "
            + "anything a command-line argument carried.");
        writer.WriteEndObject();

        writer.WriteStartObject("limits");
        writer.WriteNumber("envelopeEntries", log.Limits.EnvelopeEntries);
        writer.WriteNumber("prologueEntries", log.Limits.PrologueEntries);
        writer.WriteNumber("payloadBytesPerConnection", log.Limits.PayloadBytesPerConnection);
        writer.WriteNumber("globalPayloadBytes", log.Limits.GlobalPayloadBytes);
        writer.WriteNumber("frameBytes", log.Limits.FrameBytes);
        writer.WriteEndObject();

        writer.WriteStartObject("messages");
        writer.WriteNumber("lines", rows.Count);
        writer.WriteNumber("bodiesPresent", present);
        writer.WriteNumber("bodiesAbsent", absent);
        writer.WriteNumber("bodiesReserialized", reserialized);
        writer.WriteEndObject();

        // Losses, stated rather than left to be inferred from a short file. Per connection, because one
        // number across several servers reports a loss nobody can attribute.
        writer.WriteStartArray("connections");
        foreach (var id in rows.Select(r => r.Envelope.ConnectionId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var (droppedEnvelopes, droppedBodies, refusedBodies) = log.Losses(id);

            var limits = log.LimitsFor(id);

            writer.WriteStartObject();
            writer.WriteString("id", id);

            // Per connection, because a server may have its own budget. A reader looking at a short
            // timeline cannot otherwise tell whether little happened or the budget was small.
            writer.WriteStartObject("limits");
            writer.WriteNumber("envelopeEntries", limits.EnvelopeEntries);
            writer.WriteNumber("prologueEntries", limits.PrologueEntries);
            writer.WriteNumber("payloadBytes", limits.PayloadBytesPerConnection);
            writer.WriteNumber("frameBytes", limits.FrameBytes);
            writer.WriteEndObject();

            writer.WriteNumber("envelopesDropped", droppedEnvelopes);
            writer.WriteNumber("bodiesEvicted", droppedBodies);
            writer.WriteNumber("bodiesRefused", refusedBodies);

            if (unobservable is not null && unobservable.TryGetValue(id, out var note) && note is not null)
                writer.WriteString("unobservable", note);

            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteNumber("framesTheCaptureCouldNotKeepUpWith", log.QueueDropped);

        // Per-row attribution of a missing body is not available yet: the capture knows how many bodies
        // were evicted and how many were refused for want of arming, but not which of the two happened to
        // any one envelope. Said here rather than guessed, and it is one of the open questions the design
        // record names.
        writer.WriteString("missingBodyAttribution",
            "Aggregate only. A line reading no-body was either never armed for or has since been evicted; "
          + "the counts above say how many of each, not which.");

        writer.WriteStartArray("envelopes");
        foreach (var (envelope, line) in rows)
        {
            writer.WriteStartObject();
            writer.WriteNumber("sequence", envelope.Sequence);
            writer.WriteNumber("line", line);
            writer.WriteString("connection", envelope.ConnectionId);
            writer.WriteString("at", envelope.Timestamp.ToString("O"));
            writer.WriteString("direction", envelope.Direction.ToString());
            writer.WriteString("kind", envelope.Kind.ToString());

            if (envelope.Method is { } method) writer.WriteString("method", method);
            if (envelope.CorrelationId is { } id) writer.WriteString("id", id);

            writer.WriteNumber("bytes", envelope.SizeBytes);

            if (envelope.Outcome != ConversationOutcome.None)
                writer.WriteString("outcome", envelope.Outcome.ToString());

            if (envelope.Elapsed is { } elapsed)
                writer.WriteNumber("elapsedMs", elapsed.TotalMilliseconds);

            // A detail is a short human-readable note: an exit code, a capability name, a line of
            // standard error. The redactor catches a `file:` URI in it and nothing else — a bare path in a
            // crash stack is not recognised, because recognising one would mean the content-agnostic sweep
            // this design rejects. A real limit, and the preview is what is meant to catch it.
            if (envelope.Detail is { } detail) writer.WriteString("detail", redactor.Body(detail));

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
