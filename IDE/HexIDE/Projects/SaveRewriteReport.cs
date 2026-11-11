using HexIDE.Language;

namespace HexIDE.Projects;

/// <summary>The member qualifiers changed by one save, for the caller's successful reply.</summary>
public sealed class SaveRewriteReport
{
    private readonly List<string> notes = [];

    public string Note => string.Concat(notes);

    internal void Record(string whole, IReadOnlyList<TextChange> changes)
    {
        foreach (var change in changes)
        {
            var oldName = whole.Substring(change.Offset, change.Length);
            var note = $" The save wrote {change.Text}'s Attribute lines with its name (they said {oldName}), as VB6's own Save does.";
            if (!notes.Contains(note))
                notes.Add(note);
        }
    }
}
