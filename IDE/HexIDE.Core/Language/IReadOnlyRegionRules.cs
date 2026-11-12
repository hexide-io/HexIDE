namespace HexIDE.Language;

/// <summary>A format's rules for writes to read-only text, separate from reading its regions.</summary>
public interface IReadOnlyRegionRules
{
    /// <summary>The replacement to apply, the format's refusal, or null to leave the decision to the IDE.</summary>
    /// <remarks>
    /// With null, the IDE accepts incoming text only when the reader's regions retain their text and order,
    /// and hanging regions retain their keys and order. Added regions are refused too. The IDE reads incoming
    /// text with the current prefix's length when it starts with that prefix, and zero otherwise; it never puts
    /// the prefix back. An override returning a replacement owns the whole decision.
    /// </remarks>
    ContentReplacement? Replace(string current, int prefixLength, string incoming) => null;

    /// <summary>Findings about the result of range edits, in the format's words; null when allowed.</summary>
    string? RangeEditRefusal(string before, string after, int prefixLength) => null;

    /// <summary>
    /// A noun phrase naming the non-empty set of regions an edit or the caret reaches, or null for the IDE's
    /// generic wording. The phrase must read after both "would change" and "is in".
    /// </summary>
    string? DescribeRegions(string text, int prefixLength, IReadOnlyList<TextRegion> regions) => null;

    /// <summary>Whether a rename may write this edit into a region the reader marks read-only.</summary>
    bool RenameMayWrite(string text, int prefixLength, IReadOnlyList<TextChange> changes, TextChange edit) => false;

    /// <summary>Whether the header declares a name that cannot be renamed from the code.</summary>
    bool HeaderDeclares(string text, int prefixLength, string name) => false;
}
