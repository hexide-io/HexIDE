using System.Collections.Generic;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tools.ObjectBrowser;

public class OBClassViewModel
{
    public string Name { get; }
    public OBClassKind Kind { get; }
    public string LibraryName { get; }

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
        Name = name;
        Kind = kind;
        LibraryName = libraryName;
        ModuleDefinition = moduleDefinition;
        FormDefinition = formDefinition;
    }
}
