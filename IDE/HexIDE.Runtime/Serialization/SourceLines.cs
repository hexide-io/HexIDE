namespace HexIDE.Runtime.Serialization;

/// <summary>
/// Where a line of VB6 source ends: at <c>\r\n</c>, at a lone <c>\r</c> and at a lone <c>\n</c>, each one line end, as
/// <see cref="System.IO.TextReader.ReadLine"/> reads them and as the code window draws them.
/// </summary>
/// <remarks>
/// <para>
/// <b>One rule for every reader that walks a module's or a form's lines</b> (hexide-io/HexIDE#1020). The header split,
/// the <c>VB_Name</c> retarget, the read-only regions and the guard each split on <c>\n</c> alone, while the
/// <c>.frm</c> designer reader and the editor took a lone <c>\r</c> for a line end too. A <c>.bas</c> whose lines end
/// with a lone CR was therefore one line to the header split, which took it all for the header, and Save Project wrote
/// the file back as its <c>VB_Name</c> line alone.
/// </para>
/// <para>
/// <b>VB6 reads code the same way</b> (measured with <c>vb6.exe /make</c>, recorded in
/// <c>docs/vb6-fidelity-oracle/a-lone-cr-or-lf-in-vb6-source.md</c>). A <c>.bas</c> whose lines all end with a lone CR, or all with a lone LF, builds
/// and runs as its CRLF twin. A lone CR or LF between two statements ends the first, and a lone CR between a procedure
/// and its <c>Attribute</c> line leaves the line attached to it. The exception is a designer block, a class's
/// <c>VERSION</c> … <c>END</c> or a form's <c>Begin</c> … <c>End</c>, which VB6 refuses to load unless it is CRLF.
/// </para>
/// </remarks>
internal static class SourceLines
{
    /// <summary>Whether <paramref name="c"/> ends a line, alone or as half of <c>\r\n</c>.</summary>
    public static bool IsLineEnd(char c) => c is '\r' or '\n';

    /// <summary>
    /// The line that starts at <paramref name="start"/>: where its text ends, and where the next line starts, past its
    /// terminator. Both are <c>text.Length</c> for a last line with no terminator.
    /// </summary>
    public static (int TextEnd, int Next) LineAt(string text, int start)
    {
        var end = start;
        while (end < text.Length && !IsLineEnd(text[end]))
            end++;
        if (end == text.Length)
            return (end, end);
        return (end, text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? end + 2 : end + 1);
    }

    /// <summary>
    /// The lines of <paramref name="text"/>, without their terminators. As with <c>Split('\n')</c>, text that ends with a
    /// terminator has an empty last line.
    /// </summary>
    public static string[] Split(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>How many line ends <paramref name="text"/> holds, a <c>\r\n</c> counting once.</summary>
    public static int Count(string text)
    {
        var count = 0;
        for (var pos = 0; pos < text.Length;)
        {
            var (end, next) = LineAt(text, pos);
            if (end < text.Length)
                count++;
            pos = next;
        }
        return count;
    }
}
