using System.Collections.Generic;
using Avalonia.Media;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Utils;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools.ObjectBrowser;

public partial class OBClassViewModel
{
    [Notify] private string name;

    /// <summary>The name: what a list of these is read by in automation, which names a row by its item's ToString (#1299).</summary>
    public override string ToString() => Name;
    public OBClassKind Kind { get; }
    [Notify] private string libraryName;
    public bool IsStatus { get; init; }

    public ModuleDefinition? ModuleDefinition { get; }
    public FormDefinition? FormDefinition { get; }

    public bool CanNavigate => ModuleDefinition != null || FormDefinition != null;

    public List<OBMemberViewModel> Members { get; } = new();

    private static IImage? formIcon, moduleIcon, classIcon, enumIcon, genericIcon;

    public IImage KindIcon => IconFor(Kind);

    public static IImage IconFor(OBClassKind kind) => kind switch
    {
        OBClassKind.Form        => formIcon ??= IconFactory.Themed("Geo.Form"),
        OBClassKind.Module      => moduleIcon ??= IconFactory.Themed("Geo.Addmodule"),
        OBClassKind.ClassModule => classIcon ??= IconFactory.Themed("Geo.Addclassmodule"),
        OBClassKind.Enum        => enumIcon ??= IconFactory.Themed("Geo.Listconstants"),
        _                       => genericIcon ??= IconFactory.Themed("Geo.Generic")
    };

    // Text form retained for completion lists.
    public static string GlyphFor(OBClassKind kind) => kind switch
    {
        OBClassKind.Form        => "▣",
        OBClassKind.ClassModule => "◉",
        OBClassKind.Enum        => "∈",
        _                       => "≡"
    };

    public OBClassViewModel(string name, OBClassKind kind, string libraryName,
        ModuleDefinition? moduleDefinition = null, FormDefinition? formDefinition = null)
    {
        this.name = name;
        Kind = kind;
        this.libraryName = libraryName;
        ModuleDefinition = moduleDefinition;
        FormDefinition = formDefinition;
    }
}
