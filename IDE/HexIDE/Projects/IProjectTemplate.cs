using Avalonia.Media;
using HexIDE.Addins;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Projects;

public interface IProjectTemplate : IAddinProjectTemplate
{
    IImage Icon { get; }

    public static readonly IProjectTemplate StandardEXE = new ProjectTemplate("Standard EXE", "standard_exe", true, name =>
    {
        var project = new ProjectDefinition(VBProjectType.EXE, name);

        project.AddForm(new FormDefinition(project, FormComponentClass.Instance, "Form1"));

        // OLE Automation — always present in a new VB6 Standard EXE
        project.AddReference(new VbReference(
            "{00020430-0000-0000-C000-000000000046}", "2.0", 0, null, "OLE Automation"));

        return project;
    });

    public static readonly IProjectTemplate ActiveXEXE = new ProjectTemplate("ActiveX EXE", "activex_exe", false, _ => new ProjectDefinition(0, ""));
    public static readonly IProjectTemplate ActiveXDLL = new ProjectTemplate("ActiveX DLL", "activex_dll", false, _ => new ProjectDefinition(0, ""));
    public static readonly IProjectTemplate ActiveXControl = new ProjectTemplate("ActiveX Control", "activex_control", false, _ => new ProjectDefinition(0, ""));

    static readonly IProjectTemplate[] Templates =
    [
        StandardEXE,
        ActiveXEXE,
        ActiveXDLL,
        ActiveXControl,
        new ProjectTemplate("VB Application Wizard", "wizard", false, _ => new ProjectDefinition(0, "")),
        new ProjectTemplate("VB Wizard Manager", "wizard", false, _ => new ProjectDefinition(0, "")),
        new ProjectTemplate("Data Project", "generic", false, _ => new ProjectDefinition(0, "")),
        new ProjectTemplate("VB Enterprise Edition Controls", "generic", false, _ => new ProjectDefinition(0, "")),
    ];
}