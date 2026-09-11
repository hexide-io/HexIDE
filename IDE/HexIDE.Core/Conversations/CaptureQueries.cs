using System.Text;
using HexIDE.Redaction;

namespace HexIDE.Conversations;

/// <summary>Which envelopes an automation client is asking about.</summary>
/// <param name="ConnectionId">One connection, or null for the whole interleaved timeline.</param>
/// <param name="Method">An exact method name, which is how a client asks "was this even sent".</param>
/// <param name="Direction">Sent, received, or the local notes that are neither.</param>
/// <param name="Kind">Request, response, notification, and the entries that never crossed the wire.</param>
/// <param name="FailuresOnly">
/// Error responses and requests that failed, were cancelled, or were abandoned.
/// </param>
/// <param name="AfterSequence">
/// Everything newer than a sequence already seen. The cheap way to poll: exercise a feature, ask what is
/// new since last time, and get only that.
/// </param>
/// <param name="Limit">
/// The most to return. The <em>newest</em> matches are kept rather than the oldest, because a client that
/// has just exercised something is asking about the end of the conversation, not its beginning.
/// </param>
public sealed record EnvelopeFilter(
    string? ConnectionId = null,
    string? Method = null,
    ConversationDirection? Direction = null,
    ConversationEntryKind? Kind = null,
    bool FailuresOnly = false,
    long? AfterSequence = null,
    int Limit = 200);

/// <summary>A page of envelopes, and how much it is a page of.</summary>
/// <param name="Entries">In the order they happened, oldest first, however few were asked for.</param>
/// <param name="Matched">How many matched the filter in total.</param>
/// <param name="Truncated">
/// True when the limit cut the answer short. Stated rather than inferred, for the same reason a truncated
/// message body states its true length: a list that quietly stops reads exactly like a complete one.
/// </param>
public sealed record EnvelopePage(
    IReadOnlyList<ConversationEnvelope> Entries,
    int Matched,
    bool Truncated);

/// <summary>One message's content, as far as it was kept.</summary>
/// <param name="Sequence">Its place in the timeline, which is how it was asked for.</param>
/// <param name="TrueLength">What crossed the wire, whether or not all of it was kept.</param>
/// <param name="Head">The start of the body, or all of it when it fitted.</param>
/// <param name="Tail">The end, present only when the middle was dropped.</param>
/// <param name="Redacted">Whether paths and server configuration were pseudonymised on the way out.</param>
public sealed record PayloadView(
    long Sequence,
    string ConnectionId,
    string? Method,
    int TrueLength,
    string Head,
    string? Tail,
    bool Redacted);

/// <summary>
/// Reading a capture back, in the two tiers the record is already shaped for.
/// </summary>
/// <remarks>
/// <b>This lives here rather than in the automation server, and that is a requirement rather than a
/// preference.</b> The server is compiled out of distributed builds and the capture is not, so anything
/// the capture needs in order to be readable belongs on this side of that line. It also makes the
/// behaviour testable: nothing references the desktop executable, so logic left inside a tool body cannot
/// be reached by any test in the tree. The same split the interface automation driver already uses.
///
/// <para>
/// <b>Every entry point drains first, and that is the bug this type exists to prevent.</b> The tap hands
/// frames to a channel and never waits, because a language service must not be slowed by a diagnostic. So
/// a snapshot taken without draining is quietly short by exactly the frames a client has just provoked —
/// which are the only ones it is asking about. The exporter learned this already; anything else reading
/// the record has to learn it too, or it will report that a request was never sent when it was sent a
/// millisecond ago.
/// </para>
///
/// <para>
/// <b>Two tiers, not an optimisation.</b> A conversation runs to megabytes per minute of typing, so a
/// single call returning everything would be unusable, and the split is already in the record: envelopes
/// are kept always, bodies only for an armed connection. Listing is cheap and answers most questions;
/// fetching one body answers the rest.
/// </para>
/// </remarks>
public static class CaptureQueries
{
    /// <summary>Envelopes matching a filter, newest kept when there are more than asked for.</summary>
    public static async Task<EnvelopePage> ListAsync(ConversationLog log, EnvelopeFilter filter)
    {
        await log.DrainAsync().ConfigureAwait(false);

        var matched = new List<ConversationEnvelope>();
        foreach (var envelope in log.Snapshot(filter.ConnectionId))
        {
            if (Matches(envelope, filter)) matched.Add(envelope);
        }

        var limit = Math.Max(1, filter.Limit);
        if (matched.Count <= limit) return new EnvelopePage(matched, matched.Count, false);

        return new EnvelopePage(
            matched.GetRange(matched.Count - limit, limit), matched.Count, true);
    }

    /// <summary>
    /// One message's content, or null when the record has none for that sequence.
    /// </summary>
    /// <param name="redactor">
    /// When supplied, paths and server configuration are pseudonymised. <b>Absent means raw</b>, which is
    /// the same answer the live view gives for the same reason: this is the developer's own machine and
    /// their own files, and a redacted body would break the one affordance that proves what actually
    /// crossed the wire. Redaction governs egress, and an export is where that boundary sits.
    /// </param>
    public static async Task<PayloadView?> FetchAsync(
        ConversationLog log, string connectionId, long sequence, ConversationRedactor? redactor = null)
    {
        await log.DrainAsync().ConfigureAwait(false);

        if (log.Body(connectionId, sequence) is not { } body) return null;

        var envelope = FindEnvelope(log, connectionId, sequence);
        var head = Encoding.UTF8.GetString(body.Head);
        var tail = body.Tail is null ? null : Encoding.UTF8.GetString(body.Tail);

        return new PayloadView(
            Sequence: sequence,
            ConnectionId: connectionId,
            Method: envelope?.Method,
            TrueLength: body.TrueLength,
            Head: redactor is null ? head : redactor.Body(head),
            Tail: tail is null ? null : redactor is null ? tail : redactor.Body(tail),
            Redacted: redactor?.IsPseudonymising ?? false);
    }

    /// <summary>
    /// Why a sequence has no body, in the terms a reader needs rather than as an absence.
    /// </summary>
    /// <remarks>
    /// Three states are worth telling apart and one word cannot: the sequence is not in the record at all,
    /// it is there and nothing was kept for it, or it is there and what was kept has since been evicted.
    /// The capture knows the counts but not which happened to a given envelope, so this says what can
    /// honestly be said and no more — and says that it cannot say the rest.
    /// </remarks>
    public static async Task<string> ExplainMissingBodyAsync(
        ConversationLog log, string connectionId, long sequence)
    {
        await log.DrainAsync().ConfigureAwait(false);

        if (FindEnvelope(log, connectionId, sequence) is null)
        {
            return $"No envelope {sequence} for connection '{connectionId}'. It may have been discarded "
                 + "as the oldest entry, or it may never have existed.";
        }

        var (_, evicted, refused) = log.Losses(connectionId);
        var armed = log.IsArmed(connectionId) ? "armed" : "not armed";

        return $"Envelope {sequence} exists and has no retained body. The connection is {armed}; "
             + $"{refused} bodies were refused for want of arming and {evicted} have been evicted since. "
             + "Which of the two applies to this one is not recorded.";
    }

    private static ConversationEnvelope? FindEnvelope(
        ConversationLog log, string connectionId, long sequence)
    {
        foreach (var envelope in log.Snapshot(connectionId))
        {
            if (envelope.Sequence == sequence) return envelope;
        }

        return null;
    }

    private static bool Matches(ConversationEnvelope envelope, EnvelopeFilter filter)
    {
        if (filter.AfterSequence is { } after && envelope.Sequence <= after) return false;
        if (filter.Direction is { } direction && envelope.Direction != direction) return false;
        if (filter.Kind is { } kind && envelope.Kind != kind) return false;

        // Ordinal and exact. A method name is an identifier on the wire, and a client asking about
        // `textDocument/hover` must not be answered about `textDocument/hoverProvider`.
        if (filter.Method is { Length: > 0 } method
            && !string.Equals(envelope.Method, method, StringComparison.Ordinal))
        {
            return false;
        }

        if (filter.FailuresOnly && !IsFailure(envelope)) return false;

        return true;
    }

    /// <summary>
    /// Whether an entry is one a client chasing a problem would want.
    /// </summary>
    /// <remarks>
    /// An error response is a failure whatever its outcome says, and a request that was cancelled or
    /// abandoned is one too — a request that never came back is invisible in a record that only counts
    /// replies, which is exactly the case worth surfacing.
    /// </remarks>
    private static bool IsFailure(ConversationEnvelope envelope) =>
        envelope.Kind == ConversationEntryKind.ErrorResponse
        || envelope.Outcome is ConversationOutcome.Failed
                            or ConversationOutcome.Cancelled
                            or ConversationOutcome.Abandoned;
}
