namespace HexIDE.Tools.ObjectBrowser;

public class OBMemberViewModel
{
    public string Name { get; }
    public OBMemberKind Kind { get; }
    public string Signature { get; }
    public string? Description { get; }

    public OBMemberViewModel(string name, OBMemberKind kind, string signature, string? description = null)
    {
        Name = name;
        Kind = kind;
        Signature = signature;
        Description = description;
    }

    public string KindGlyph => GlyphFor(Kind);

    /// <summary>
    /// The glyph for a kind, without a member to hang it on.
    /// </summary>
    /// <remarks>
    /// Static because the workspace-search list shows the same glyphs for things that are not members of
    /// any loaded class. Two lists in one window disagreeing about what a property looks like would read as
    /// a rendering fault rather than as two code paths.
    /// </remarks>
    public static string GlyphFor(OBMemberKind kind) => kind switch
    {
        OBMemberKind.Property   => "⊞",
        OBMemberKind.Event      => "⟡",
        OBMemberKind.Constant   => "⊡",
        OBMemberKind.EnumMember => "⊡",
        _                       => "◈"
    };
}
