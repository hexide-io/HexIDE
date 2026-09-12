using HexIDE.Conversations;

namespace HexIDE.Tools.ProtocolInspector;

/// <summary>
/// One envelope, as a grid row.
/// </summary>
/// <remarks>
/// <b>A projection taken once, not a live view of the envelope.</b> An envelope is already a value — the
/// capture hands out immutable records and a snapshot of them — so a row that re-read its source would gain
/// nothing and could show a state and a latency that never coexisted. That is the same defect the
/// connection list fixed one layer down, and it is worth not reintroducing here.
///
/// <para>
/// Everything on it is machine text the localisation rules already exempt: method names, ids, directions
/// and byte counts are what crossed the wire. Only the column headings are translated.
/// </para>
/// </remarks>
public sealed class ProtocolInspectorRowViewModel(ConversationEnvelope envelope, bool hasBody)
{
    public long Sequence { get; } = envelope.Sequence;

    public string ConnectionId { get; } = envelope.ConnectionId;

    /// <summary>Wall clock, to the millisecond. The date is not shown: a capture dies with the session.</summary>
    public string At { get; } = envelope.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");

    /// <summary>
    /// An arrow rather than a word, because this column is read down rather than across.
    /// </summary>
    /// <remarks>
    /// <c>Local</c> is neither direction: it is the capture's own note about the process, its standard
    /// error, a request declined before it was sent, or a capability advertised and unused. A dot says
    /// "this did not cross the wire", which is the distinction those entries exist to make.
    /// </remarks>
    public string Direction { get; } = envelope.Direction switch
    {
        ConversationDirection.Sent => "→",
        ConversationDirection.Received => "←",
        _ => "•",
    };

    public string Kind { get; } = envelope.Kind.ToString();

    /// <summary>
    /// The method, or the note for the entries that have none.
    /// </summary>
    /// <remarks>
    /// A response carries no method of its own — it is identified by the id it answers — so its row would
    /// otherwise be blank in the column a reader scans first. The detail takes that space for the entries
    /// that are not messages at all, which is where the lifecycle and never-sent notes belong.
    /// </remarks>
    public string Method { get; } = string.IsNullOrEmpty(envelope.Method)
        ? envelope.Detail ?? ""
        : envelope.Method;

    public string CorrelationId { get; } = envelope.CorrelationId ?? "";

    public string Size { get; } = envelope.SizeBytes > 0 ? Format(envelope.SizeBytes) : "";

    /// <summary>
    /// Request to response, blank when there is nothing to say.
    /// </summary>
    /// <remarks>
    /// Blank for a notification, which has no answer to wait for, and blank for a request still
    /// outstanding — which reads as a gap and should: a request that never came back is invisible in a log
    /// that only records replies, and this column is where that shows.
    /// </remarks>
    public string Elapsed { get; } = envelope.Elapsed is { } elapsed
        ? elapsed.TotalMilliseconds < 1
            ? "<1 ms"
            : $"{elapsed.TotalMilliseconds:N0} ms"
        : "";

    public string Outcome { get; } =
        envelope.Outcome == ConversationOutcome.None ? "" : envelope.Outcome.ToString();

    /// <summary>Whether a body was retained, which decides whether opening the row shows anything.</summary>
    public bool HasBody { get; } = hasBody;

    /// <summary>
    /// Whether this row is one a reader chasing a problem wants.
    /// </summary>
    /// <remarks>
    /// Marked in place rather than split into a second view, because a failure's meaning is almost always
    /// in what preceded it. A request that was cancelled or abandoned counts: never coming back is a
    /// failure even though nothing said so.
    /// </remarks>
    public bool IsFailure { get; } =
        envelope.Kind == ConversationEntryKind.ErrorResponse
        || envelope.Outcome is ConversationOutcome.Failed
                            or ConversationOutcome.Cancelled
                            or ConversationOutcome.Abandoned;

    /// <summary>True for the capture's own notes, which are not traffic and should not read as traffic.</summary>
    public bool IsLocal { get; } = envelope.Direction == ConversationDirection.Local;

    private static string Format(int bytes) => bytes < 1024
        ? $"{bytes} B"
        : bytes < 1024 * 1024
            ? $"{bytes / 1024.0:N1} KB"
            : $"{bytes / (1024.0 * 1024):N1} MB";
}
