using HexIDE.Language;

namespace HexIDE.Projects;

/// <summary>The changes one save reports to the caller in its successful reply.</summary>
public sealed class SaveRewriteReport
{
    private readonly List<string> notes = [];

    public string Note => string.Concat(notes);

    internal void Record(string whole, IReadOnlyList<TextChange> changes, IReadOnlyRegionWriter writer)
    {
        foreach (var change in changes)
        {
            if (writer.DescribeSaveRewrite(whole, change) is not { } sentence)
                continue;
            var note = " " + sentence;
            if (!notes.Contains(note))
                notes.Add(note);
        }
    }
}
