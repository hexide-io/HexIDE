namespace HexIDE.Language;

/// <summary>The changes the IDE itself makes to a format's read-only text.</summary>
/// <remarks>
/// Each change lies inside a region the reader marks for the same text and prefix. The code window's caret
/// arithmetic and undo loop depend on this: an IDE write is skipped by Undo in the code window.
/// </remarks>
public interface IReadOnlyRegionWriter
{
    /// <summary>
    /// What a save rewrites, in order of offset; empty when nothing is out of step. It reads the whole text, header
    /// included, and never the body alone, because VB6 decides a run's kind against every name the file declares,
    /// the designer's included.
    /// </summary>
    IReadOnlyList<TextChange> SaveRewrites(string text, int prefixLength) => [];

    /// <summary>
    /// A complete sentence reporting one change a save made, or null when nothing should be said. The change's
    /// offsets index into <paramref name="text"/>, the whole text that <see cref="SaveRewrites"/> read.
    /// </summary>
    string? DescribeSaveRewrite(string text, TextChange change) => null;

    /// <summary>
    /// The change that makes the code after <paramref name="prefixLength"/> name its document <paramref name="name"/>;
    /// null when it already does, or names nothing. Offsets are into <paramref name="text"/>. VB6 compares the name
    /// by value, so a line spaced differently from how VB6 writes it isn't rewritten.
    /// </summary>
    TextChange? NameRewrite(string text, int prefixLength, string name) => null;
}
