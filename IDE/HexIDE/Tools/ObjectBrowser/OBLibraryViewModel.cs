using System.Collections.Generic;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tools.ObjectBrowser;

public class OBLibraryViewModel
{
    // Sentinel that represents the "<All Libraries>" dropdown entry.
    public static readonly OBLibraryViewModel AllLibraries =
        new("<All Libraries>") { IsLoaded = true };

    public string Name { get; }
    public List<OBClassViewModel> Classes { get; } = new();

    // Non-null for type-library references that are loaded on demand.
    public VbReference? Reference { get; }

    // False until the type library has been loaded (or attempted).
    public bool IsLoaded { get; set; }

    public OBLibraryViewModel(string name, VbReference? reference = null)
    {
        Name = name;
        Reference = reference;
        IsLoaded = reference == null; // project/VB libraries are pre-loaded
    }

    public override string ToString() => Name;
}
