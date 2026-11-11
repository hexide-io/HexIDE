using System.Text;

namespace HexIDE.Language;

/// <summary>Applies replacements to text.</summary>
public static class TextChanges
{
    /// <summary><paramref name="text"/> with <paramref name="changes"/> made, which must be in order of offset.</summary>
    public static string Apply(string text, IReadOnlyList<TextChange> changes)
    {
        if (changes.Count == 0)
            return text;
        var builder = new StringBuilder(text);
        for (var i = changes.Count - 1; i >= 0; i--)
            builder.Remove(changes[i].Offset, changes[i].Length).Insert(changes[i].Offset, changes[i].Text);
        return builder.ToString();
    }
}
