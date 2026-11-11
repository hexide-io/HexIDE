namespace HexIDE.Language;

/// <summary>A format's rules for writes to read-only text, separate from reading its regions.</summary>
public interface IReadOnlyRegionRules
{
    /// <summary>The replacement to apply, or the format's refusal. With no policy, keep the incoming text.</summary>
    /// <remarks>
    /// <c>ReplaceContent</c> assigning <c>Document.Text = text</c> and the closed-document writer's
    /// <c>text.StartsWith(prefix)</c>
    /// fallback are equivalent for VB6 only because the replacement's new text always begins with the current header.
    /// </remarks>
    ContentReplacement Replace(string current, int prefixLength, string incoming) => new(incoming, null, false);

    /// <summary>Findings about the result of range edits, in the format's words; null when allowed.</summary>
    string? RangeEditRefusal(string before, string after, int prefixLength) => null;

    /// <summary>The member name used only to choose press_key's deletion hint, or null for the header.</summary>
    string? MemberNamedBy(string text, int prefixLength, TextRegion region) => null;

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
