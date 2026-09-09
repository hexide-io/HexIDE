using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Serialization;

/// <summary>
/// The text of a form's <b>code section</b> — everything a <c>.frm</c> holds after its designer block.
/// </summary>
/// <remarks>
/// <para>A form's code section opens with an <c>Attribute VB_*</c> block that VB6 hides and HexIDE's editor
/// does not show. <c>VB_Name</c> is load-bearing: it is the form's identity. Anyone writing "the code" of a
/// form naturally writes the part they can see, and a straight replacement then deletes that block with no
/// warning — which happened, and reached a commit before <c>git diff</c> caught it.</para>
///
/// <para>The mirror mistake is passing a whole <c>.frm</c> instead, which puts <c>VERSION</c> /
/// <c>Begin VB.Form</c> into the code where it is compiled as VB.</para>
/// </remarks>
public static class FormCodeText
{
    /// <summary>
    /// True when this is a whole <c>.frm</c> file rather than the code section of one.
    /// </summary>
    /// <remarks>
    /// Both signals are required. A lone <c>VERSION</c> line is not proof — a form's code could plausibly
    /// begin with an identifier of that name — while <c>VERSION</c> followed by a <c>Begin</c> block is the
    /// shape of the file and of nothing else.
    /// </remarks>
    public static bool LooksLikeFormFile(string content)
    {
        var sawVersion = false;
        foreach (var raw in Lines(content))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (!sawVersion)
            {
                if (!line.StartsWith("VERSION", StringComparison.OrdinalIgnoreCase)) return false;
                sawVersion = true;
                continue;
            }
            return line.StartsWith("Begin ", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    /// <summary>
    /// The leading <c>Attribute</c> block of a code section, with its line endings, or <c>""</c> if absent.
    /// </summary>
    /// <remarks>
    /// Leading blank lines are carried into the block so restoring it cannot introduce or lose one. Only a
    /// LEADING run counts: an <c>Attribute</c> line further down belongs to a procedure and is not part of
    /// the file's identity.
    /// </remarks>
    public static string AttributeBlock(string code)
    {
        var end = 0;
        var pos = 0;
        foreach (var raw in Lines(code))
        {
            var lineLength = raw.Length + 1;   // the split ate one '\n'
            var line = raw.Trim();
            if (line.Length == 0) { pos += lineLength; continue; }
            if (!line.StartsWith("Attribute ", StringComparison.OrdinalIgnoreCase)) break;
            pos += lineLength;
            end = pos;
        }
        return end == 0 ? "" : code[..Math.Min(end, code.Length)];
    }

    /// <summary>
    /// <paramref name="incoming"/> with <paramref name="existing"/>'s attribute block restored when the
    /// incoming text has none of its own. Returns <paramref name="incoming"/> unchanged otherwise.
    /// </summary>
    /// <remarks>
    /// Preserving rather than refusing, because omitting the block is what a caller does when they mean
    /// exactly what they said — "replace the code" — and the block is not code. The alternative, silently
    /// accepting a body that destroys the form's identity, is the one behaviour that should not exist.
    /// </remarks>
    public static string PreserveAttributes(string incoming, string existing)
    {
        if (AttributeBlock(incoming).Length > 0) return incoming;

        var block = AttributeBlock(existing);
        return block.Length == 0 ? incoming : block + incoming;
    }

    private static IEnumerable<string> Lines(string text)
    {
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            yield return line;
    }
}
