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
    private static readonly Queue<string?> Answers = new();

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
        lock (Lock) Answers.Enqueue(string.IsNullOrWhiteSpace(path) ? null : path);
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

    /// <summary>Takes the next armed answer, if there is one. False means show the real dialog.</summary>
    public static bool TryTake(out string? path)
    {
        lock (Lock)
        {
            if (Answers.Count == 0)
            {
                path = null;
                return false;
            }

            path = Answers.Dequeue();
            return true;
        }
    }
}
#endif
