namespace HexIDE.IDE;

public static class MessageBoxClosePolicy
{
    /// <summary>
    /// What closing a box with these buttons answers when no button was pressed, or null where it has no close.
    /// </summary>
    /// <remarks>
    /// Measured on vb6.exe, posting WM_CLOSE and SC_CLOSE to each kind: OK alone answers vbOK; OK/Cancel, Yes/No/Cancel
    /// and Retry/Cancel answer vbCancel; Yes/No and Abort/Retry/Ignore stay open, because they have no close. That is
    /// Windows' own message box, and the same holds for every box the IDE shows.
    /// </remarks>
    public static MessageBoxResult? AnswerOnClose(MessageBoxButtons buttons) => buttons switch
    {
        MessageBoxButtons.Ok => MessageBoxResult.Ok,
        MessageBoxButtons.OkCancel or MessageBoxButtons.YesNoCancel or MessageBoxButtons.RetryCancel
            => MessageBoxResult.Cancel,
        _ => null,
    };
}
