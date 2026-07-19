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

    public string KindGlyph => Kind switch
    {
        OBMemberKind.Property   => "⊞",
        OBMemberKind.Event      => "⟡",
        OBMemberKind.Constant   => "⊡",
        OBMemberKind.EnumMember => "⊡",
        _                       => "◈"
    };
}
