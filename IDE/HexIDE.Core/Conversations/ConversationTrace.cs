using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HexIDE.Conversations;

/// <summary>
/// One message as the text trace a language-server author already reads.
/// </summary>
/// <remarks>
/// <b>Borrowed deliberately from Visual Studio Code's LSP trace, because it is the lingua franca.</b> An
/// author who has ever turned on <c>"trace.server": "verbose"</c> recognises these lines on sight, and
/// recognising them is the whole value: the thing being copied is going into a bug report addressed to
/// somebody who does not have HexIDE and is not going to install it. Inventing a shape here would cost
/// them a paragraph of explanation on every report. It costs us a format string.
///
/// <para>
/// <b>One deviation, and it is forced.</b> Visual Studio Code names the server in the output channel and
/// so leaves it off the line. HexIDE offers a document to every server that claims it and shows one
/// interleaved timeline, so a line with no server on it is ambiguous the moment two are attached — which
/// is the case this window exists for. The id goes in a second bracket after the timestamp, where it is
/// obvious and where it does not disturb anything a reader greps for.
/// </para>
///
/// <para>
/// <b>This is the egress shape, so the caller redacts before calling.</b> The formatter takes the body it
/// is given and does not reach for the record, which keeps the redaction decision at the one boundary that
/// makes it: the live pane is raw, and anything leaving the machine is not.
/// </para>
/// </remarks>
public static class ConversationTrace
{
    /// <summary>The trace block for one envelope, with whatever body the caller has for it.</summary>
    /// <param name="envelope">The envelope. Its kind decides the verb and the payload's label.</param>
    /// <param name="body">
    /// The message as text, already redacted if it is going anywhere. Null when none was retained, which
    /// is said rather than left as an empty payload: an absent body and an empty one are different facts.
    /// </param>
    /// <param name="unavailable">
    /// Why there is no body, when the caller knows. Written in place of the payload so a reader of the
    /// pasted text is not left to conclude the message was empty.
    /// </param>
    public static string ToTraceText(
        ConversationEnvelope envelope, string? body, string? unavailable = null)
    {
        var text = new StringBuilder();

        text.Append("[Trace - ")
            .Append(envelope.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append("] [")
            .Append(envelope.ConnectionId)
            .Append("] ")
            .Append(Headline(envelope))
            .Append('\n');

        if (body is { Length: > 0 })
        {
            text.Append(PayloadLabel(envelope)).Append(": ").Append(Pretty(body)).Append('\n');
        }
        else if (unavailable is { Length: > 0 })
        {
            // A trace whose payload is simply missing reads as a message that carried nothing. Naming the
            // reason is the same distinction the pane and the export both make.
            text.Append("No body: ").Append(unavailable).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Several envelopes as one trace, blank-line separated the way a real trace is.</summary>
    public static string ToTraceText(IEnumerable<(ConversationEnvelope Envelope, string? Body)> messages)
    {
        var text = new StringBuilder();
        foreach (var (envelope, body) in messages)
        {
            if (text.Length > 0) text.Append('\n');
            text.Append(ToTraceText(envelope, body));
        }

        return text.ToString();
    }

    /// <summary>
    /// The verb line: what happened, to what, and how long it took.
    /// </summary>
    /// <remarks>
    /// The entries that never crossed the wire have no counterpart in the borrowed vocabulary, so they say
    /// what they are rather than being forced into "Sending" or "Received" — a lifecycle note dressed as a
    /// notification would be a lie about the protocol, in a document whose whole purpose is to be
    /// quotable.
    /// </remarks>
    private static string Headline(ConversationEnvelope envelope)
    {
        var name = envelope.Method is { Length: > 0 } method ? method : "(no method)";
        var id = envelope.CorrelationId is { Length: > 0 } correlation ? $" - ({correlation})" : "";

        return envelope.Kind switch
        {
            ConversationEntryKind.Request => $"{Verb(envelope)} request '{name}{id}'.",

            ConversationEntryKind.Response or ConversationEntryKind.ErrorResponse =>
                $"{Verb(envelope)} response '{name}{id}'{Took(envelope)}.",

            ConversationEntryKind.Notification => $"{Verb(envelope)} notification '{name}'.",

            ConversationEntryKind.NeverSent =>
                $"Did not send '{name}' — {Detail(envelope, "the server advertised no such capability")}.",

            ConversationEntryKind.Unconsumed =>
                $"Advertised and unused: {Detail(envelope, name)}.",

            ConversationEntryKind.Lifecycle => $"Process: {Detail(envelope, name)}.",

            ConversationEntryKind.StandardError => $"stderr: {Detail(envelope, name)}",

            _ => Detail(envelope, name),
        };
    }

    private static string Verb(ConversationEnvelope envelope) =>
        envelope.Direction == ConversationDirection.Sent ? "Sending" : "Received";

    /// <summary>
    /// Latency, for the messages that have any.
    /// </summary>
    /// <remarks>
    /// Present because for a server author it is the difference between a bug report and an anecdote, and
    /// because the shape being borrowed already carries it. Absent rather than zero when a request never
    /// came back — a reported <c>in 0ms</c> would be a claim, and the claim would be false.
    /// </remarks>
    private static string Took(ConversationEnvelope envelope) =>
        envelope.Elapsed is { } elapsed ? $" in {elapsed.TotalMilliseconds:N0}ms" : "";

    private static string PayloadLabel(ConversationEnvelope envelope) => envelope.Kind switch
    {
        ConversationEntryKind.Response => "Result",
        ConversationEntryKind.ErrorResponse => "Error",
        _ => "Params",
    };

    private static string Detail(ConversationEnvelope envelope, string fallback) =>
        envelope.Detail is { Length: > 0 } detail ? detail : fallback;

    /// <summary>
    /// The body, indented if it parses and verbatim if it does not.
    /// </summary>
    /// <remarks>
    /// Indented because a trace is read by a person and a 40KB frame on one line is not read at all. It
    /// falls back to the exact text on any parse failure, which is the case that matters most: a body this
    /// client could not decode is precisely what an author needs to see, and reformatting is never allowed
    /// to become a reason it cannot be shown.
    /// </remarks>
    private static string Pretty(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                document.WriteTo(writer);
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
