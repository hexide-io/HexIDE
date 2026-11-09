using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Projects;

/// <summary>
/// The name a project type is shown by. The enum spells these the way the <c>.vbp</c>'s <c>Type=</c> line does, and
/// that spelling is what is stored; the developer reads the VB6 dialog wording, from these keys, as New Project does.
/// </summary>
internal static class ProjectTypeNames
{
    /// <summary>The <c>Str.*</c> key of a project type's name, or null for a type with no shown name.</summary>
    public static string? KeyOf(VBProjectType type) => type switch
    {
        VBProjectType.EXE => "Str.ProjectType.StandardEXE",
        VBProjectType.OleExe => "Str.ProjectType.ActiveXEXE",
        VBProjectType.OleDll => "Str.ProjectType.ActiveXDLL",
        VBProjectType.Control => "Str.ProjectType.ActiveXControl",
        _ => null,
    };
}
