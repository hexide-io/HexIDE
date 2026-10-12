namespace HexIDE.IDE;

/// <summary>
/// Joining a message HexIDE did not word with a sentence of its own after it.
/// </summary>
public static class MessageText
{
    /// <summary>
    /// <paramref name="message"/>, then <paramref name="sentence"/> as a paragraph of its own.
    /// </summary>
    /// <remarks>
    /// A message from a run carries its own punctuation, or none: "fieldLength as type not implemented" ends with
    /// no full stop, and appending " Nothing is running." to it ran the two into one sentence in run_project's
    /// reply. A blank line between them reads the same whatever the message ends with, and leaves its text as it
    /// was, which matters to a caller comparing it with get_last_runtime_error. Messages already use blank lines
    /// between their own parts.
    /// </remarks>
    public static string ThenSay(string message, string sentence) =>
        message.TrimEnd() is { Length: > 0 } text ? $"{text}\n\n{sentence}" : sentence;

    /// <summary>What a tool that starts a run answers when the start never became a run.</summary>
    public static string StartFailedReply(string failure) => ThenSay(failure,
        "Nothing is running. The IDE showed this in a runtime-error dialog, which is still open, and"
        + " get_last_runtime_error returns it.");
}
