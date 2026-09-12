using System.Collections.Generic;
using AvaloniaEdit.Folding;

namespace HexIDE.Controls;

/// <summary>
/// Foldable regions in a JSON document, found by matching brackets.
/// </summary>
/// <remarks>
/// <b>Written here because AvaloniaEdit ships folding but no JSON strategy</b> — only an XML one. It is a
/// scan rather than a parse on purpose: the content this folds is a language server's own output, and the
/// single most interesting case is a body that does NOT parse. A strategy that gave up there would fold
/// everything except the one message somebody opened the window for.
///
/// <para>
/// So brackets inside strings are skipped, escapes are honoured, and anything left unclosed at the end is
/// simply not foldable. A truncated body — head, a marker, tail — falls out of that correctly without
/// being special-cased: the halves fold as far as they close.
/// </para>
/// </remarks>
public static class JsonFolding
{
    /// <summary>Every region worth a fold marker, in the order AvaloniaEdit requires: by start offset.</summary>
    /// <remarks>
    /// Single-line regions are left out. A fold arrow that saves no vertical space is noise on every line
    /// of a pretty-printed frame, and the export's JSON-lines shape is entirely single-line regions.
    /// </remarks>
    public static IReadOnlyList<NewFolding> Foldings(string text)
    {
        var found = new List<NewFolding>();
        var open = new Stack<(int Offset, char Bracket)>();

        var inString = false;
        var escaped = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;

                case '{':
                case '[':
                    open.Push((i, c));
                    break;

                case '}':
                case ']':
                    if (open.Count == 0) break;

                    var (offset, bracket) = open.Pop();

                    // A mismatched pair means this is not the JSON it looks like. Skipping the fold rather
                    // than emitting a wrong one keeps the viewer honest about content it cannot make sense
                    // of, which is exactly the content worth looking at.
                    if ((bracket == '{') != (c == '}')) break;

                    if (text.AsSpan(offset, i - offset).IndexOfAny('\n', '\r') < 0) break;

                    found.Add(new NewFolding(offset, i + 1)
                    {
                        Name = bracket == '{' ? "{…}" : "[…]",
                    });
                    break;
            }
        }

        found.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return found;
    }
}
