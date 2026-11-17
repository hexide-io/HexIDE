using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Projects;

/// <summary>
/// VB6's projects, registered at start-up through the same route as an add-in's project type (#3411 slice 10b). Its type
/// carries VB6's whole format, which reads a <c>.vbp</c> and a <c>.vbg</c> and everything they list. It's first-party, so it
/// needs no package, signature or consent, and its files are reserved against every other type.
/// </summary>
public static class Vb6ProjectType
{
    /// <summary>The id its projects carry as <see cref="HexIDE.Runtime.ProjectElements.ProjectDefinition.ContributedBy"/>.</summary>
    public const string Id = "hexide.vb6";

    /// <summary>What a refusal or a project's <c>ContributorTitle</c> calls it.</summary>
    public const string Title = "Visual Basic 6.0";

    /// <summary>The files no other type may take.</summary>
    public static ProjectFileReservation Reservation { get; } = new(Id, Title, ["*.vbp", "*.vbg"]);

    /// <summary>
    /// Member files a refusal may call VB6's when VB6's type isn't registered: the unambiguous VB6 source extensions only.
    /// <c>.cls</c> is left out because it is equally a LaTeX class file (#279), and HexIDE's own text says only what it knows.
    /// </summary>
    public static ProjectFileReservation Members { get; } = new(Id, Title,
        HexIDE.Lsp.DocumentLanguage.UnambiguousVb6Extensions.Select(extension => "*" + extension).ToArray());

    /// <summary>The type, reading and rendering through <paramref name="format"/>.</summary>
    public static AddinProjectType For(IProjectFileFormat format) => new("Visual Basic 6.0 projects")
    {
        FileNames = ["*.vbp"],
        GroupFileNames = ["*.vbg"],
        Format = format,
    };

    /// <summary>Who registers it: HexIDE itself.</summary>
    internal static AddinOrigin Origin { get; } = new(Id, Title, IsFirstParty: true);

    public static readonly IProjectTemplate StandardExe = new ProjectTemplate("Standard EXE", "standard_exe", true, name =>
    {
        var project = new ProjectDefinition(VBProjectType.EXE, name);

        var form = FormDefinition.NewForm(project, FormComponentClass.Instance, "Form1");
        project.AddForm(form);

        // OLE Automation — always present in a new VB6 Standard EXE
        project.AddReference(new VbReference(
            "{00020430-0000-0000-C000-000000000046}", "2.0", 0, null, "OLE Automation"));

        return project;
    }, "Str.ProjectType.StandardEXE");

    public static readonly IProjectTemplate ActiveXExe = new ProjectTemplate("ActiveX EXE", "activex_exe", false, _ => new ProjectDefinition(0, ""), "Str.ProjectType.ActiveXEXE");
    public static readonly IProjectTemplate ActiveXDll = new ProjectTemplate("ActiveX DLL", "activex_dll", false, _ => new ProjectDefinition(0, ""), "Str.ProjectType.ActiveXDLL");
    public static readonly IProjectTemplate ActiveXControl = new ProjectTemplate("ActiveX Control", "activex_control", false, _ => new ProjectDefinition(0, ""), "Str.ProjectType.ActiveXControl");

    /// <summary>The templates it offers, in the New Project dialog's order.</summary>
    public static readonly IProjectTemplate[] Templates =
    [
        StandardExe,
        ActiveXExe,
        ActiveXDll,
        ActiveXControl,
        new ProjectTemplate("VB Application Wizard", "wizard", false, _ => new ProjectDefinition(0, ""), "Str.ProjectType.VBApplicationWizard"),
        new ProjectTemplate("VB Wizard Manager", "wizard", false, _ => new ProjectDefinition(0, ""), "Str.ProjectType.VBWizardManager"),
        new ProjectTemplate("Data Project", "generic", false, _ => new ProjectDefinition(0, ""), "Str.ProjectType.DataProject"),
        new ProjectTemplate("VB Enterprise Edition Controls", "generic", false, _ => new ProjectDefinition(0, ""), "Str.ProjectType.VBEnterpriseControls"),
    ];

    /// <summary>The id a profile names each of <see cref="Templates"/> by, in the same order.</summary>
    public static readonly string[] TemplateIds = ["standard-exe", "activex-exe", "activex-dll", "activex-control",
        "application-wizard", "wizard-manager", "data-project", "enterprise-controls"];

    /// <summary>Registers its templates with <paramref name="templates"/>, as first-party ones (#3411 slice 10b).</summary>
    public static void RegisterTemplates(AddinProjectTemplateService templates)
    {
        for (var i = 0; i < Templates.Length; i++)
            templates.RegisterFirstParty(TemplateIds[i], Templates[i]);
    }

    /// <summary>Registers it with <paramref name="types"/>, reading through <paramref name="format"/>.</summary>
    public static IDisposable Register(ContributedProjects types, IProjectFileFormat format) => types.Register(For(format), Origin);
}
