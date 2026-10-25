using System.Collections.Generic;
using HexIDE.Runtime.ProjectElements;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools.ObjectBrowser;

public partial class OBLibraryViewModel
{
    // Sentinel that represents the "<All Libraries>" dropdown entry.
    public static readonly OBLibraryViewModel AllLibraries =
        new("<All Libraries>") { IsLoaded = true };

    [Notify] private string name;
    public List<OBClassViewModel> Classes { get; } = new();

    // Non-null for type-library references that are loaded on demand.
    public VbReference? Reference { get; }

    // False until the type library has been loaded (or attempted).
    public bool IsLoaded { get; set; }

    public OBLibraryViewModel(string name, VbReference? reference = null)
    {
        this.name = name;
        Reference = reference;
        IsLoaded = reference == null; // project/VB libraries are pre-loaded
    }

    public override string ToString() => Name;
}
