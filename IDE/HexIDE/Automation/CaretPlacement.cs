using HexIDE.Forms.ViewModels;

namespace HexIDE.Automation;

/// <summary>
/// Puts an editor's caret at a line and column, as the editor numbers them, for open_file: a code window's (#700), or a
/// carried file's (#2403).
/// </summary>
public static class CaretPlacement
{
    /// <summary>
    /// Why (<paramref name="line"/>, <paramref name="column"/>), both 1-based, is no place in <paramref name="text"/>, the
    /// code window's text, or null when it is one. Checked before anything is opened or moved.
    /// </summary>
    /// <remarks>
    /// Refused rather than clamped, unlike a language server's position: a caret quietly put somewhere else would take
    /// the caller's next type_text there. A column one past the line's last character is its end, where typing appends.
    /// Columns count UTF-16 characters, as the status bar and get_diagnostics do, so a tab is one; a column between the
    /// halves of a surrogate pair is no place the editor can show a caret, and it would move it. Lines are counted by
    /// <c>'\n'</c>, as <see cref="HexIDE.Runtime.Serialization.CodeWindowText.LineCount"/> counts them.
    /// </remarks>
    public static string? Refusal(string text, int line, int column)
    {
        var lines = text.Split('\n');
        if (line < 1 || line > lines.Length)
            return $"line {line} is not one of its lines, numbered 1 to {lines.Length}";
        var content = lines[line - 1].TrimEnd('\r');
        if (column < 1 || column > content.Length + 1)
            return $"column {column} is not on line {line}, whose columns are numbered 1 to {content.Length + 1}, its end";
        if (column > 1 && column <= content.Length && char.IsLowSurrogate(content[column - 1]))
            return $"column {column} of line {line} falls between the two halves of one character; it is column "
                   + $"{column - 1} or {column + 1}";
        return null;
    }

    /// <summary>
    /// Puts <paramref name="editor"/>'s caret at (<paramref name="line"/>, <paramref name="column"/>), 1-based, if its
    /// text has that place; false, with the caret left alone, if it has not.
    /// </summary>
    public static bool Place(ISearchableDocument editor, int line, int column)
    {
        if (Refusal(editor.Document.Text, line, column) is not null) return false;
        editor.CaretOffset = editor.Document.GetLineByNumber(line).Offset + column - 1;
        return true;
    }

    /// <summary>
    /// Where <paramref name="editor"/>'s caret is, read back rather than echoed, and whether that is where
    /// (<paramref name="line"/>, <paramref name="column"/>) asked for it.
    /// </summary>
    public static string Where(ISearchableDocument editor, int line, int column)
    {
        var at = editor.Document.GetLocation(editor.CaretOffset);
        var where = $"The caret is at line {at.Line}, column {at.Column}: offset {editor.CaretOffset} in the text "
                    + "get_file_content returns.";
        return at.Line == line && at.Column == column
            ? where
            : $"{where} That is not line {line}, column {column}, as asked: the editor moved it, so type_text would write "
              + "there.";
    }
}
