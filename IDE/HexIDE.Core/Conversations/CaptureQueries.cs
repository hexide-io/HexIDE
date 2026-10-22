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
/// new since last time, and get only that. A request recorded before it and answered or ended since counts as
/// new, because its row is where its reply appears (#961); pass <see cref="EnvelopePage.LatestSequence"/> from the
/// last listing, not the highest row seen.
/// </param>
/// <param name="Limit">
/// The most to return. The <em>newest</em> matches are kept rather than the oldest, because a client that
/// has just exercised something is asking about the end of the conversation, not its beginning. A poll, with
/// <see cref="AfterSequence"/>, is the exception: it keeps the earliest changes, so that following
/// <see cref="EnvelopePage.LatestSequence"/> pages through the rest rather than skipping it. A poll also keeps requests
/// ended together together, since one ending takes one sequence for all of them, so it can return more than this.
/// </param>
public sealed record EnvelopeFilter(
    string? ConnectionId = null,
    string? Method = null,
    ConversationDirection? Direction = null,
    ConversationEntryKind? Kind = null,
    bool FailuresOnly = false,
    long? AfterSequence = null,
    int Limit = 200);

/// <summary>
/// Why a listing came back empty, as something other than a sentence.
/// </summary>
/// <remarks>
/// <b>The prose in <see cref="EnvelopePage.Note"/> is written for an automation client, and English is the
/// right answer there.</b> A window is a different audience: it has to say the same thing in whatever
/// language the person reading it chose. Carrying the reason as a value lets both surfaces render it, and
/// keeps the two from drifting into different explanations of the same state.
/// </remarks>
public enum CaptureEmptyReason
{
    /// <summary>Not empty.</summary>
    NotEmpty,

    /// <summary>No server has connected, so there is nothing to have recorded.</summary>
    NoConnections,

    /// <summary>Connections exist and hold nothing — nothing has crossed the wire, or it was cleared.</summary>
    NothingRecorded,

    /// <summary>Something is recorded and the filter excludes all of it.</summary>
    FilteredOut,

    /// <summary>The connection asked about is not one this capture knows.</summary>
    NoSuchConnection,
}

/// <summary>A page of envelopes, and how much it is a page of.</summary>
/// <param name="Entries">In the order they happened, oldest first, however few were asked for.</param>
/// <param name="Matched">How many matched the filter in total.</param>
/// <param name="Truncated">
/// True when the limit cut the answer short. Stated rather than inferred, for the same reason a truncated
/// message body states its true length: a list that quietly stops reads exactly like a complete one. It says whether
/// anything matched was left out, not whether more came back than the limit, which a poll can do (#1313).
/// </param>
/// <param name="Note">
/// Why the answer looks the way it does, when it would otherwise need guessing at. Present for an empty
/// answer and absent for an ordinary one.
///
/// <para>
/// <b>This exists because the alternative reintroduces the exact ambiguity the inspector removes.</b> A
/// bare <c>matched: 0</c> cannot be told apart from "nothing happened", "nothing was configured" and "the
/// tool is broken" — which is the confusion a developer chasing a silent language feature already has, and
/// the reason this whole capability was built. Handing it back to them inside the tool meant to dispel it
/// is the worst place to put it.
/// </para>
/// </param>
/// <param name="Reason">
/// The same fact as <paramref name="Note"/>, as a value a view can translate.
/// </param>
/// <param name="LatestSequence">
/// Where the listing is complete to, to pass as <see cref="EnvelopeFilter.AfterSequence"/> next time: the last
/// sequence recorded when it was read, or for a poll cut short by its limit, the last change it returned. Null when
/// nothing has been recorded and no cursor was given. A poller cannot take it from the rows (#961): a request
/// answered since the last poll comes back on its own, older row, and passing that row's sequence would return it
/// again on every poll.
/// </param>
public sealed record EnvelopePage(
    IReadOnlyList<ConversationEnvelope> Entries,
    int Matched,
    bool Truncated,
    string? Note = null,
    CaptureEmptyReason Reason = CaptureEmptyReason.NotEmpty,
    long? LatestSequence = null);

/// <summary>One message's content, as far as it was kept.</summary>
/// <param name="Sequence">Its place in the timeline, which is how it was asked for.</param>
/// <param name="TrueLength">What crossed the wire, whether or not all of it was kept.</param>
/// <param name="Head">The start of the body, or all of it when it fitted.</param>
/// <param name="Tail">The end, present only when the middle was dropped.</param>
/// <param name="Redacted">Whether paths and server configuration were pseudonymised on the way out.</param>
/// <param name="IsAnswer">
/// Whether this is the reply half of an exchange rather than the request.
/// </param>
/// <remarks>
/// <b>An answer is fetched by its own sequence and carries the request's method</b>, because on the wire a
/// response has no method of its own — only an id. A view that left the method blank would be honest about
/// the frame and useless to a reader, who is looking at it precisely to find out what <em>was</em> answered.
/// <paramref name="AnswerTo"/> names the request so the pairing is stated rather than inferred.
/// </remarks>
/// <param name="AnswerTo">The request this answers, when it is an answer.</param>
public sealed record PayloadView(
    long Sequence,
    string ConnectionId,
    string? Method,
    int TrueLength,
    string Head,
    string? Tail,
    bool Redacted,
    bool IsAnswer = false,
    long? AnswerTo = null);

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
    /// <summary>
    /// Envelopes matching a filter. When there are more than asked for, the newest are kept, except in a poll, which
    /// keeps the earliest changes so that its <see cref="EnvelopePage.LatestSequence"/> pages on without losing any.
    /// </summary>
    /// <remarks>
    /// <b>The record is read as it stood at the drain's watermark (#961 review).</b> The snapshot reads one ring at a
    /// time while the pump carries on, so a ring read late holds what an earlier one missed. A cursor taken from what
    /// was read would skip that for good: measured, hundreds of rows in two hundred thousand across two connections.
    /// Everything at or below the watermark is in the record, so a row above it is left for the next poll, and an
    /// outcome above it is not shown yet.
    /// </remarks>
    public static async Task<EnvelopePage> ListAsync(ConversationLog log, EnvelopeFilter filter)
    {
        var watermark = await log.DrainAsync().ConfigureAwait(false);

        // A cursor beyond anything recorded is from an earlier run, since sequences start again with the IDE. Ignoring
        // it lists this run from its start; keeping it would list nothing until the count passed it.
        var stale = filter.AfterSequence > watermark;
        var asked = stale ? filter with { AfterSequence = 0 } : filter;

        var matched = new List<ConversationEnvelope>();
        foreach (var recorded in log.Snapshot(filter.ConnectionId))
        {
            if (recorded.Sequence > watermark) continue;
            var envelope = recorded.CompletedSequence > watermark ? Waiting(recorded) : recorded;
            if (Matches(envelope, asked)) matched.Add(envelope);
        }

        var limit = Math.Max(1, filter.Limit);
        var reason = matched.Count == 0 ? WhyEmpty(log, asked) : CaptureEmptyReason.NotEmpty;
        var note = matched.Count == 0 ? ExplainEmpty(log, asked) : null;
        if (stale)
        {
            note = $"afterSequence {filter.AfterSequence} is beyond the last sequence recorded, {watermark}: sequences "
                   + "start again when the IDE does, so this lists the record from its start." + (note is null ? "" : " " + note);
        }

        long? latest = watermark > 0 || filter.AfterSequence is not null ? watermark : null;
        if (matched.Count <= limit) return new EnvelopePage(matched, matched.Count, false, note, reason, latest);

        if (asked.AfterSequence is null)
            return new EnvelopePage(matched.GetRange(matched.Count - limit, limit), matched.Count, true, note, reason, latest);

        // A poll pages forward: the earliest changes, and a cursor at the last of them, so the next poll takes up the
        // rest. An ending shares its sequence among the requests it ends, so they are kept or left together, which can
        // return more than the limit; truncated says whether anything was left out, not whether the limit was passed
        // (#1313).
        var byChange = matched.OrderBy(LastChanged).ToList();
        var through = LastChanged(byChange[limit - 1]);
        var kept = byChange.TakeWhile(e => LastChanged(e) <= through).ToHashSet();
        return new EnvelopePage([.. matched.Where(kept.Contains)], matched.Count, kept.Count < matched.Count, note, reason, through);
    }

    /// <summary>A request as it stood before its outcome was recorded.</summary>
    private static ConversationEnvelope Waiting(ConversationEnvelope request) => request with
    {
        Outcome = ConversationOutcome.None,
        Elapsed = null,
        AnswerSequence = null,
        AnswerSizeBytes = null,
        CompletedSequence = null,
    };

    /// <summary>The last sequence that changed an envelope: its own, or the one its outcome was recorded under.</summary>
    private static long LastChanged(ConversationEnvelope envelope) =>
        Math.Max(envelope.Sequence, envelope.CompletedSequence ?? 0);

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

        // Two ways a sequence can be known: it is an entry, or it is the answer an entry points at. The
        // second has no envelope of its own by design, so the request's is what describes it.
        var envelope = FindEnvelope(log, connectionId, sequence);
        var answered = envelope is null ? FindByAnswer(log, connectionId, sequence) : null;
        var describing = envelope ?? answered;

        var head = Encoding.UTF8.GetString(body.Head);
        var tail = body.Tail is null ? null : Encoding.UTF8.GetString(body.Tail);

        return new PayloadView(
            Sequence: sequence,
            ConnectionId: connectionId,
            Method: describing?.Method,
            TrueLength: body.TrueLength,
            Head: redactor is null ? head : redactor.Body(head),
            Tail: tail is null ? null : redactor is null ? tail : redactor.Body(tail),
            Redacted: redactor?.IsPseudonymising ?? false,
            IsAnswer: answered is not null,
            AnswerTo: answered?.Sequence);
    }

    /// <summary>
    /// Why a listing came back empty, in the terms a first-time caller needs.
    /// </summary>
    /// <remarks>
    /// Four states, and a count of zero collapses all of them. The one that matters most is the first: a
    /// language server starts on the first document of a language it claims, so a caller who has not opened
    /// anything gets an empty record and no hint that the record is not the problem.
    /// </remarks>
    /// <summary>
    /// The same four states <see cref="ExplainEmpty"/> describes, decided once.
    /// </summary>
    /// <remarks>
    /// Deliberately beside the prose rather than derived from it. Two functions answering the same
    /// question from the same inputs can be read side by side; a parser of one's output could not.
    /// </remarks>
    private static CaptureEmptyReason WhyEmpty(ConversationLog log, EnvelopeFilter filter)
    {
        var connections = log.ConnectionIds;

        if (connections.Count == 0) return CaptureEmptyReason.NoConnections;

        if (filter.ConnectionId is { Length: > 0 } asked
            && !connections.Contains(asked, StringComparer.Ordinal))
        {
            return CaptureEmptyReason.NoSuchConnection;
        }

        return log.Snapshot(filter.ConnectionId).Count == 0
            ? CaptureEmptyReason.NothingRecorded
            : CaptureEmptyReason.FilteredOut;
    }

    private static string ExplainEmpty(ConversationLog log, EnvelopeFilter filter)
    {
        var connections = log.ConnectionIds;

        if (connections.Count == 0)
        {
            return "No language server has connected yet, so there is nothing recorded. Servers start on "
                 + "the first document of a language they claim — open a file and ask again. Nothing needs "
                 + "arming for envelopes to be recorded.";
        }

        if (filter.ConnectionId is { Length: > 0 } asked
            && !connections.Contains(asked, StringComparer.Ordinal))
        {
            return $"No connection is called '{asked}'. Known connections: "
                 + $"{string.Join(", ", connections.Order(StringComparer.Ordinal))}.";
        }

        var total = log.Snapshot(filter.ConnectionId).Count;
        if (total == 0)
        {
            return $"{connections.Count} connection(s) are known and hold no envelopes. Either nothing has "
                 + "crossed the wire yet, or the record was cleared.";
        }

        // A quiet poll, the ordinary end of the polling loop: the filter would match without its cursor, so nothing is
        // wrong with it but the time. Told to drop its filters, a poller re-listed what it had already seen (#1313).
        if (filter.AfterSequence is { } after
            && log.Snapshot(filter.ConnectionId).Any(e => Matches(e, filter with { AfterSequence = null })))
        {
            return $"Nothing was recorded, answered or ended after {after}. Poll again from latestSequence.";
        }

        return $"{total} envelope(s) are recorded, and none match this filter. Drop the filters to see "
             + "what is there.";
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

        if (FindByAnswer(log, connectionId, sequence) is { } request)
        {
            // Reachable only if the body went between the listing and the fetch, since the request stops
            // naming an answer the moment there is nothing to name. Worth saying plainly rather than
            // falling through to "no envelope anywhere", which would be wrong twice over.
            return $"Sequence {sequence} is the answer to request {request.Sequence} "
                 + $"({request.Method ?? "no method"}) and its body has been evicted since that request "
                 + "was listed. Answers have no entry of their own; the request names where theirs is kept.";
        }

        // A reply never kept, or an ending, uses up a sequence that has no entry and no body: often the latestSequence
        // a poll hands back, so a client may well ask for it (#961 review).
        if (FindByCompletion(log, connectionId, sequence) is { } completed)
        {
            return completed.Outcome is ConversationOutcome.Answered or ConversationOutcome.Failed
                ? $"Sequence {sequence} is where request {completed.Sequence} ({completed.Method ?? "no method"}) was "
                  + "answered, and no body was kept for the answer, so there is nothing to fetch; its size is on the "
                  + "request's row."
                :$"Sequence {sequence} is where request {completed.Sequence} ({completed.Method ?? "no method"}) was "
                  + $"{completed.Outcome.ToString().ToLowerInvariant()}. An ending has no body.";
        }

        if (FindEnvelope(log, connectionId, sequence) is null)
        {
            // A sequence is unique across the whole record, not per connection, so the likeliest mistake
            // is naming the wrong connection for a real sequence. Saying which one it belongs to costs one
            // pass and turns a dead end into an answer.
            foreach (var other in log.ConnectionIds)
            {
                if (string.Equals(other, connectionId, StringComparison.Ordinal)) continue;
                if (FindEnvelope(log, other, sequence) is not null)
                {
                    return $"Envelope {sequence} belongs to connection '{other}', not '{connectionId}'. "
                         + "Sequence numbers are unique across the whole record rather than per connection.";
                }

                // A reply or an ending has no row of its own, so it is found by the request it belongs to: a poll scoped
                // to one connection hands back the record's one counter, which can be another connection's (#1313). A
                // kept answer's sequence is the one its request was completed under, so one search finds both.
                if (FindByCompletion(log, other, sequence) is { } owner)
                {
                    var what = owner.AnswerSequence == sequence
                        ? $"the answer to request {owner.Sequence} ({owner.Method ?? "no method"})"
                        : $"where request {owner.Sequence} ({owner.Method ?? "no method"}) was "
                          + (owner.Outcome is ConversationOutcome.Answered or ConversationOutcome.Failed
                              ? "answered" : owner.Outcome.ToString().ToLowerInvariant());
                    return $"Sequence {sequence} belongs to connection '{other}', not '{connectionId}': it is {what}. "
                         + "Sequence numbers are unique across the whole record rather than per connection.";
                }
            }

            return $"No envelope {sequence} anywhere in the record. It may have been discarded as the "
                 + "oldest entry, or it may never have existed.";
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

    /// <summary>The request whose outcome was recorded under <paramref name="sequence"/>, if one was.</summary>
    private static ConversationEnvelope? FindByCompletion(
        ConversationLog log, string connectionId, long sequence)
    {
        foreach (var envelope in log.Snapshot(connectionId))
        {
            if (envelope.CompletedSequence == sequence) return envelope;
        }

        return null;
    }

    /// <summary>The request whose answer is held under <paramref name="sequence"/>, if one is.</summary>
    private static ConversationEnvelope? FindByAnswer(
        ConversationLog log, string connectionId, long sequence)
    {
        foreach (var envelope in log.Snapshot(connectionId))
        {
            if (envelope.AnswerSequence == sequence) return envelope;
        }

        return null;
    }

    private static bool Matches(ConversationEnvelope envelope, EnvelopeFilter filter)
    {
        // A request recorded before the poll's sequence and answered or ended since is new too (#961): its row is the
        // only place its reply appears, so a filter on the row's own sequence alone never showed it answered.
        if (filter.AfterSequence is { } after && envelope.Sequence <= after && !(envelope.CompletedSequence > after))
            return false;
        if (filter.Direction is { } direction && envelope.Direction != direction) return false;
        if (filter.Kind is { } kind && envelope.Kind != kind) return false;

        // Ordinal and exact. A method name is an identifier on the wire, and a client asking about
        // `textDocument/hover` must not be answered about `textDocument/hoverProvider`.
        if (filter.Method is { Length: > 0 } method
            && !string.Equals(envelope.Method, method, StringComparison.Ordinal))
        {
            return false;
        }

        if (filter.FailuresOnly && !envelope.IsFailure()) return false;

        return true;
    }
}
