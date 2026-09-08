using System;

namespace HexIDE.IDE;

/// <summary>
/// The last runtime error a program raised, kept after its dialog has gone.
/// </summary>
/// <remarks>
/// <para>The error's only representation used to be a modal dialog, and <c>stop_project</c> and
/// <c>shutdown_ide</c> both close open dialogs — so the ordinary run → stop → look rhythm destroyed the
/// evidence before anything could read it. A run whose form silently did nothing was indistinguishable
/// from a run whose error dialog had been dismissed a moment earlier.</para>
///
/// <para>That is not hypothetical: a runtime error raised inside a <c>.bas</c> module was swallowed
/// entirely (the handler threw before it could show the dialog), and the automated symptom — a form that
/// runs and does nothing — looked exactly like success. It surfaced only because a human happened to see
/// something flash on screen.</para>
///
/// <para><b>Sequence, not just a message.</b> A caller needs to distinguish "no error this run" from "the
/// same error as last time", and comparing message text cannot do that — a loop can raise the identical
/// error twice. The counter only ever increases, so a caller that notes it before a run can tell exactly
/// whether that run added anything.</para>
/// </remarks>
public sealed class RuntimeErrorLog
{
    private readonly object _gate = new();
    private string? _message;
    private DateTimeOffset _at;
    private int _sequence;

    /// <summary>Records an error. Called from the runtime's error event.</summary>
    public void Record(string message)
    {
        lock (_gate)
        {
            _message = message;
            _at = DateTimeOffset.Now;
            _sequence++;
        }
    }

    /// <summary>
    /// The most recent error, or null when none has been raised since the last <see cref="Clear"/>.
    /// </summary>
    public (string Message, DateTimeOffset At, int Sequence)? Last
    {
        get
        {
            lock (_gate)
                return _message is null ? null : (_message, _at, _sequence);
        }
    }

    /// <summary>
    /// Forgets the message but <b>keeps the sequence</b>, so a starting run reads as "nothing yet" while a
    /// caller holding an older sequence can still see that something has happened since.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
            _message = null;
    }
}
