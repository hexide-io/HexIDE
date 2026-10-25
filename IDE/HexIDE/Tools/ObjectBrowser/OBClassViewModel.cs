using System.Collections.Generic;
using HexIDE.Runtime.ProjectElements;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools.ObjectBrowser;

public partial class OBClassViewModel
{
    [Notify] private string name;

    /// <summary>The name: what a list of these is read by in automation, which names a row by its item's ToString (#1299).</summary>
    public override string ToString() => Name;
    public OBClassKind Kind { get; }
    [Notify] private string libraryName;

    public ModuleDefinition? ModuleDefinition { get; }
    public FormDefinition? FormDefinition { get; }

    public bool CanNavigate => ModuleDefinition != null || FormDefinition != null;

    public List<OBMemberViewModel> Members { get; } = new();

    public string KindGlyph => Kind switch
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
