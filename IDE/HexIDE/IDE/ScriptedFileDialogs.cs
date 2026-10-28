#if DEBUG
using System.Collections.Generic;

namespace HexIDE.IDE;

/// <summary>
/// Answers the next file dialog on behalf of an automation client, which cannot reach one.
/// </summary>
/// <remarks>
/// <b>A file picker is a native operating-system dialog, so it is outside the control tree entirely.</b>
/// <c>dump_visual_tree</c> cannot see it, <c>interact</c> cannot address it, and a modal one stops the
/// automation server answering at all — so every feature that ends in Save As, Open, Make EXE, Add File or
/// Export was unverifiable, and each of them was verified by asking a person to click. That is the one
/// thing the dev loop is not allowed to require.
///
/// <para>
/// <b>This does not fake the dialog; it replaces the question with an answer already given.</b> The
/// picker's whole job is to return a path, and everything downstream of it — the writing, the naming, the
/// refusals — is exactly the code a real click reaches. What is skipped is the part a person performs, and
/// no other part.
/// </para>
///
/// <para>
/// <b>DEBUG only, like the automation server it exists for.</b> A shipped build has no bypass, because a
/// shipped build has nothing that could set one: the queue and its call sites both compile out. A single
/// answer is consumed by a single dialog and there is no standing override, so a script that arms one and
/// then takes a different path leaves nothing armed behind it — a sticky answer would silently redirect a
/// later save, which is exactly the accident this must not cause.
/// </para>
/// </remarks>
public static class ScriptedFileDialogs
{
    private static readonly object Lock = new();
    // Numbered as they are armed, so a save can tell the answers armed before it from any armed while it ran (#810).
    // Paths is null for a cancel, and holds several only for a multi-select Open (#2408).
    private static readonly Queue<(long Serial, IReadOnlyList<string>? Paths)> Answers = new();
    private static long _armed;
    private static long _cancelsTaken;
    private static long _taken;
    // Answers of several paths spent by a picker that takes one, which answered as cancelled (#2408), and how many of
    // those a reply has told the caller about.
    private static long _refusalsTaken;
    private static long _refusalsReported;
    private static string _lastRefusal = "";

    /// <summary>How many answers are queued, for a tool that has to report what it armed.</summary>
    public static int Pending { get { lock (Lock) return Answers.Count; } }

    /// <summary>
    /// Arms one answer. A null or empty path answers as a cancelled dialog.
    /// </summary>
    /// <remarks>
    /// Cancel is offered deliberately: "the reader changed their mind" is a distinct path through every
    /// one of these flows and is the one most likely to be written wrong, because it is the one nobody
    /// exercises by hand.
    /// </remarks>
    public static void AnswerNextWith(string? path)
    {
        lock (Lock) Answers.Enqueue((++_armed, string.IsNullOrWhiteSpace(path) ? null : [path]));
    }

    /// <summary>
    /// Arms one answer of several files, as a person selects them in a multi-select Open picker (#2408). Every path
    /// must be non-blank; one path is the same answer as <see cref="AnswerNextWith"/>.
    /// </summary>
    /// <remarks>
    /// It is still one answer, spent by the next dialog to open. A picker that takes one file, a save or an Open
    /// without multi-select, cannot take it: a person could not have made that choice there. That picker answers as
    /// cancelled rather than taking the first path, which would be a wrong answer that runs, and the refusal is told
    /// to the caller by <see cref="CancelWatch.After"/> or by <see cref="TakeUnreportedRefusal"/>.
    /// </remarks>
    public static void AnswerNextWithMany(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0 || paths.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Every path must be non-blank, and there must be at least one.", nameof(paths));
        lock (Lock) Answers.Enqueue((++_armed, [.. paths]));
    }

    /// <summary>Discards every armed answer, so a failed script cannot affect the next one.</summary>
    public static int Clear()
    {
        lock (Lock)
        {
            var count = Answers.Count;
            Answers.Clear();
            return count;
        }
    }

    /// <summary>
    /// True when saving a document at this path would put a real picker on screen: it has no file yet and
    /// nothing is armed to answer for it.
    /// </summary>
    /// <remarks>
    /// <b>A tool asks this before a save, and refuses rather than blocks when it is true</b>
    /// (hexide-io/HexIDE#514). A modal native picker stops the automation server answering at all, so the
    /// call that opened it never returns and neither does anything behind it. Asked before anything is
    /// changed, so a refusal leaves nothing half-done. An armed answer, including an armed cancel, makes
    /// the save safe to attempt, which is what a caller testing the pathless state needs.
    /// </remarks>
    public static bool WouldShowPicker(string? absolutePath) => absolutePath is null && Pending == 0;

    /// <summary>What a tool says when <see cref="WouldShowPicker"/> refuses it, after naming the document.</summary>
    public const string PickerRefusal =
        "has no file yet, so saving it would open a native save picker, which stops this server answering " +
        "until a person closes it. Nothing was changed. Arm answer_next_file_dialog first: with a path to " +
        "save it there, or with none to answer the picker as cancelled and leave the document without a file.";

    /// <summary>
    /// Taken just before a save of a document. After the save, <see cref="CancelWatch.Cancelled"/> says whether
    /// the save's picker was answered with an armed cancel.
    /// </summary>
    /// <remarks>
    /// An armed cancel is a route the tools offer (<see cref="PickerRefusal"/>): it keeps a document without a file.
    /// So a save it cancels is that outcome, not a failure, and a tool reports it as one (#755). The decision comes
    /// from the dialog's state rather than from the exception the cancelled save throws, because that exception is
    /// not specific to it: a save refused for an encoding its code page cannot hold throws the same kind and must
    /// stay a failure (#751), and a save refused before its picker opens takes no answer at all.
    /// </remarks>
    public static CancelWatch WatchForCancel(string? absolutePath)
    {
        // Only a document without a file opens a picker. Which answer the picker takes needs no peek: only a
        // cancel moves the count.
        lock (Lock)
            return new CancelWatch(absolutePath is null, _cancelsTaken, _taken, _armed, _refusalsTaken);
    }

    /// <summary>
    /// A project save reached through the UI asks for its destinations before its first incomplete await (#1335).
    /// It can save several documents, so no single document's existing path explains an unused answer.
    /// </summary>
    public static CancelWatch WatchForUiProjectSave()
    {
        lock (Lock)
            return new CancelWatch(true, _cancelsTaken, _taken, _armed, _refusalsTaken, "this save opened no picker");
    }

    /// <summary>
    /// The sentence for a refusal no reply has reported yet, or "" (#2408). Reporting it marks it reported, so the
    /// caller is told once.
    /// </summary>
    /// <remarks>
    /// A refusal reached through a save tool is reported in that tool's reply. One reached any other way, through Add
    /// File or a running program's Open, has no reply of its own, so answer_next_file_dialog and
    /// clear_file_dialog_answers report it: the caller arms or clears next, having seen the dialog do nothing.
    /// </remarks>
    public static string TakeUnreportedRefusal()
    {
        lock (Lock)
        {
            if (_refusalsReported >= _refusalsTaken) return "";
            var count = _refusalsTaken - _refusalsReported;
            _refusalsReported = _refusalsTaken;
            return count == 1
                ? $" Earlier, {_lastRefusal}."
                : $" Earlier, {count} answers of several paths were refused that way; the last time, {_lastRefusal}.";
        }
    }

    /// <summary>Whether a save was answered by the armed cancel that was next when it began.</summary>
    public readonly struct CancelWatch(bool armed, long takenBefore, long anyTakenBefore, long armedBefore,
        long refusalsBefore, string? pendingReason = null)
    {
        /// <summary>
        /// True when the document had no file and its picker, which takes one file, was answered with several paths
        /// since <see cref="WatchForCancel"/>, so it answered as cancelled (#2408).
        /// </summary>
        public bool Refused
        {
            get
            {
                lock (Lock)
                    return armed && _refusalsTaken > refusalsBefore;
            }
        }

        /// <summary>
        /// How the save's picker was answered when it wrote nothing: an armed cancel, or several paths it cannot
        /// take. For a reply's "…, so nothing was written".
        /// </summary>
        public string HowAnswered
        {
            get
            {
                if (!Refused) return "the armed answer cancelled the save picker";
                lock (Lock)
                {
                    _refusalsReported = Math.Max(_refusalsReported, _refusalsTaken);
                    return "the save picker answered as cancelled, because the armed answer named several files and "
                           + "it takes one (answer_next_file_dialog's paths is for an Open that allows several)";
                }
            }
        }

        /// <summary>
        /// True when the document had no file and a picker has taken an armed cancel since
        /// <see cref="WatchForCancel"/>.
        /// </summary>
        public bool Cancelled
        {
            get
            {
                lock (Lock)
                    return armed && _cancelsTaken > takenBefore;
            }
        }

        /// <summary>
        /// A sentence for the save's reply when answers armed before it are still pending and it took none, or ""
        /// (#810).
        /// </summary>
        /// <remarks>
        /// <para>A caller arms an answer for a save it expects to open a picker. One that needed none, because the
        /// document already had a file, or stopped before its picker, leaves the answer for the next dialog to open,
        /// however much later and whatever it is for, and only clear_file_dialog_answers used to show it.</para>
        /// <para>Only answers armed before the watch count, by their numbers, so one armed while the save ran is not
        /// put down to it. The sentence states what happened rather than what the answer was for, since a caller may
        /// have armed it for a later step. A document without a file opens a picker unless its save stopped first, so
        /// for one, an answer taken while the save ran is taken to be its own: a dialog elsewhere taking one in that
        /// window, as a running program's can, reads as the save's (#810 review).</para>
        /// </remarks>
        public string StillArmed
        {
            get
            {
                var left = 0;
                lock (Lock)
                {
                    if (armed && _taken > anyTakenBefore) return "";
                    foreach (var answer in Answers)
                        if (answer.Serial <= armedBefore) left++;
                }

                if (left == 0) return "";
                var why = pendingReason ?? (armed
                    ? "this save stopped before its picker opened"
                    : "this save opened no picker, because the document already has a file");
                return left == 1
                    ? $" 1 file-dialog answer armed before this call is still pending: {why}. The next file dialog to "
                      + "open will take it, whatever it is for; if it was armed for this save, clear_file_dialog_answers "
                      + "discards it, with any armed since."
                    : $" {left} file-dialog answers armed before this call are still pending: {why}. The next file "
                      + "dialogs to open will take them in turn, whatever they are for; if they were armed for this "
                      + "save, clear_file_dialog_answers discards them, with any armed since.";
            }
        }

        /// <summary>
        /// <paramref name="text"/> with <see cref="StillArmed"/> after it, as one more sentence; the text alone when
        /// there is nothing to say, and the sentence alone when there is no text.
        /// </summary>
        public string? After(string? text)
        {
            var still = StillArmed;
            // Unless HowAnswered has already said it, in the same reply.
            if (Refused)
                lock (Lock)
                    if (_refusalsReported < _refusalsTaken)
                    {
                        _refusalsReported = _refusalsTaken;
                        still = $" {char.ToUpperInvariant(_lastRefusal[0])}{_lastRefusal[1..]}." + still;
                    }
            if (still.Length == 0) return text;
            if (string.IsNullOrWhiteSpace(text)) return still.TrimStart();
            // A message ending without a stop, as an exception's can, would otherwise run into the sentence.
            var said = text.TrimEnd();
            return (said.EndsWith('.') ? said : said + ".") + still;
        }
    }

    /// <summary>
    /// Takes the next armed answer for a picker that returns one file, a save or a single-select Open, if there is
    /// one. False means show the real dialog. An answer of several paths answers as cancelled; see
    /// <see cref="AnswerNextWithMany"/>.
    /// </summary>
    public static bool TryTake(out string? path)
    {
        var taken = TryTake(takesSeveral: false, out var paths, picker: "a save picker or an Open that takes one file");
        path = paths is { Count: > 0 } ? paths[0] : null;
        return taken;
    }

    /// <summary>
    /// Takes the next armed answer, if there is one. False means show the real dialog. Null paths is a cancel.
    /// </summary>
    /// <param name="takesSeveral">Whether the picker lets a person select several files: an Open that allows them.</param>
    /// <param name="picker">The picker as the refusal names it, should it be answered with several paths it cannot take.</param>
    public static bool TryTake(bool takesSeveral, out IReadOnlyList<string>? paths, string picker)
    {
        lock (Lock)
        {
            if (Answers.Count == 0)
            {
                paths = null;
                return false;
            }

            paths = Answers.Dequeue().Paths;
            _taken++;
            if (paths is { Count: > 1 } && !takesSeveral)
            {
                // Spent, as every answer is by the first dialog to open, so it cannot redirect a later one; and not
                // its first path, which no person could have chosen here.
                _refusalsTaken++;
                _lastRefusal = $"{picker} took an answer of {paths.Count} paths, which it cannot take, so it answered "
                               + "as cancelled";
                paths = null;
            }
            else if (paths is null)
                _cancelsTaken++;
            return true;
        }
    }
}
#endif
