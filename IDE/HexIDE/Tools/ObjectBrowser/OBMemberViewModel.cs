using Avalonia.Media;
using HexIDE.Lsp.Messages;
using HexIDE.Utils;

namespace HexIDE.Tools.ObjectBrowser;

public class OBMemberViewModel
{
    public string Name { get; }

    /// <summary>The name: what a list of these is read by in automation, which names a row by its item's ToString (#1299).</summary>
    public override string ToString() => Name;
    public OBMemberKind Kind { get; }
    public string Signature { get; }
    public string? Description { get; }
    public Position? DeclarationPosition { get; }

    public OBMemberViewModel(string name, OBMemberKind kind, string signature, string? description = null,
        Position? declarationPosition = null)
    {
        Name = name;
        Kind = kind;
        Signature = signature;
        Description = description;
        DeclarationPosition = declarationPosition;
    }

    private static IImage? methodIcon, propertyIcon, eventIcon, constantIcon, genericIcon;

    public IImage KindIcon => IconFor(Kind);

    // Shared with workspace-search rows, so both lists use the same image for each kind.
    public static IImage IconFor(OBMemberKind kind) => kind switch
    {
        OBMemberKind.Method     => methodIcon ??= IconFactory.Themed("Geo.Addprocedure"),
        OBMemberKind.Property   => propertyIcon ??= IconFactory.Themed("Geo.Properties"),
        OBMemberKind.Event      => eventIcon ??= IconFactory.Themed("Geo.Event"),
        OBMemberKind.Constant
            or OBMemberKind.EnumMember => constantIcon ??= IconFactory.Themed("Geo.Lockcontrols"),
        _                       => genericIcon ??= IconFactory.Themed("Geo.Generic")
    };

    // Text form retained for completion lists.
    public static string GlyphFor(OBMemberKind kind) => kind switch
    {
        OBMemberKind.Property   => "⊞",
        OBMemberKind.Event      => "⟡",
        OBMemberKind.Constant   => "⊡",
        OBMemberKind.EnumMember => "⊡",
        _                       => "◈"
    };
}
