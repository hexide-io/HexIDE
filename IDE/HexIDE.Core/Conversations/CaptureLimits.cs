namespace HexIDE.Conversations;

/// <summary>
/// How much a capture is allowed to hold. Defaults measured rather than guessed.
/// </summary>
/// <remarks>
/// <b>Envelopes are capped in entries and payloads in bytes, and the asymmetry is deliberate.</b> An
/// envelope is near enough fixed size, so entries are the honest unit and a count means something. Message
/// bodies span four orders of magnitude — measured from 38 bytes to well over 200 KB in one ordinary
/// session — so a message-count cap on payloads swings memory several-fold at one setting, which is not a
/// limit anyone can reason about.
///
/// <para>
/// <b>Per connection, not one shared pool.</b> Measured on an identical twenty-edit script, one server
/// returned roughly a hundred times another's inbound bytes, almost entirely completion replies. Under a
/// single budget the noisy server silently evicts the quiet one's history and a single drop count reports
/// a loss nobody can attribute to anything. The global ceiling exists so that several connections cannot
/// add up to an unbounded total.
/// </para>
/// </remarks>
/// <param name="EnvelopeEntries">Per connection. At the measured rate this is roughly twenty minutes of continuous typing.</param>
/// <param name="PrologueEntries">
/// Envelopes at the start of a connection that are never evicted.
///
/// <para>
/// Drop-oldest discards the handshake first, which is precisely the thing that must never go: several of
/// the costliest defects in this project's history live in or immediately after <c>initialize</c>. A
/// reserved prologue costs a fixed, tiny amount and is the difference between an always-on record that can
/// explain a startup failure and one that cannot.
/// </para>
/// </param>
/// <param name="PayloadBytesPerConnection">Retained message bodies, once a connection is armed.</param>
/// <param name="GlobalPayloadBytes">The ceiling across every connection together.</param>
/// <param name="FrameBytes">
/// The most of any single body that is retained, as a head and a tail with the true length recorded.
///
/// <para>
/// Both ends rather than a prefix: a truncated JSON document is unreadable from the front alone, and the
/// interesting part of a large body is as often the closing shape as the opening one.
/// </para>
/// </param>
public sealed record CaptureLimits(
    int EnvelopeEntries = 25_000,
    int PrologueEntries = 200,
    long PayloadBytesPerConnection = 16L * 1024 * 1024,
    long GlobalPayloadBytes = 64L * 1024 * 1024,
    int FrameBytes = 64 * 1024)
{
    /// <summary>What a capture holds when nobody has said otherwise.</summary>
    public static CaptureLimits Default { get; } = new();

    /// <summary>
    /// These limits, forced into a range the capture can honour.
    /// </summary>
    /// <remarks>
    /// <b>Clamped rather than rejected, and the caller is told what it lost.</b> These arrive from a file
    /// somebody edits by hand, so a zero, a negative, or a number with an extra three digits are all
    /// ordinary typing accidents. Refusing the whole configuration over one of them would cost a working
    /// capture; silently accepting a payload budget of two gigabytes would cost the machine.
    ///
    /// <para>
    /// The floors are not cosmetic. A prologue longer than the ring would evict nothing and grow forever,
    /// and a per-connection budget above the global ceiling makes the ceiling a lie, so both are bounded by
    /// their neighbour rather than by a constant.
    /// </para>
    /// </remarks>
    public CaptureLimits Clamped(out IReadOnlyList<string> adjustments)
    {
        var notes = new List<string>();
        var limits = this;

        limits = limits with { EnvelopeEntries = Bound(EnvelopeEntries, 100, 1_000_000, "envelope entries", notes) };
        limits = limits with { FrameBytes = Bound(FrameBytes, 1024, 8 * 1024 * 1024, "frame bytes", notes) };
        limits = limits with
        {
            GlobalPayloadBytes = Bound(GlobalPayloadBytes, 1024 * 1024, 4L * 1024 * 1024 * 1024,
                "global payload bytes", notes),
        };
        limits = limits with
        {
            PayloadBytesPerConnection = Bound(PayloadBytesPerConnection, 64 * 1024, limits.GlobalPayloadBytes,
                "payload bytes per connection", notes),
        };

        // Last, and against the clamped ring rather than the requested one: a prologue is a slice of the
        // ring, so bounding it against a value that was itself out of range would let it back out again.
        limits = limits with
        {
            PrologueEntries = Bound(PrologueEntries, 0, limits.EnvelopeEntries / 2, "prologue entries", notes),
        };

        adjustments = notes;
        return limits;
    }

    private static int Bound(int value, int min, int max, string what, List<string> notes) =>
        (int)Bound((long)value, min, max, what, notes);

    private static long Bound(long value, long min, long max, string what, List<string> notes)
    {
        if (value < min)
        {
            notes.Add($"{what}: {value} is below the minimum {min}, so {min} is used.");
            return min;
        }

        if (value > max)
        {
            notes.Add($"{what}: {value} is above the maximum {max}, so {max} is used.");
            return max;
        }

        return value;
    }
}
