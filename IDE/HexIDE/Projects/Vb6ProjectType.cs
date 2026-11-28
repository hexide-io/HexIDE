using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Projects;

/// <summary>
/// VB6's projects. The Basic provider registers their type through the host, as any add-in registers one (#5625). Its type
/// carries VB6's whole format, which reads a <c>.vbp</c> and a <c>.vbg</c> and everything they list. The provider's grant
/// reserves it (<see cref="Reserved"/>): it takes VB6's identity, and its files are refused to every other type.
/// </summary>
public static class Vb6ProjectType
{
    /// <summary>The id its projects carry as <see cref="HexIDE.Runtime.ProjectElements.ProjectDefinition.ContributedBy"/>.</summary>
    public const string Id = "hexide.vb6";

    /// <summary>What a refusal or a project's <c>ContributorTitle</c> calls it.</summary>
    public const string Title = "Visual Basic 6.0";

    /// <summary>
    /// VB6's type as the Basic provider's grant reserves it (#5625): its identity, its files, and the formats of the members
    /// an add-in's project may borrow, every kind of module and forms, named "vb6".
    /// </summary>
    internal static HexIDE.Addins.ReservedProjectType Reserved { get; } =
        // Its own set: this is initialised before MemberKinds, which is declared below.
        new(Id, Title, ["*.vbp"], ["*.vbg"], new HashSet<ModuleKind>(Enum.GetValues<ModuleKind>()), Forms: true, FormFormat: "vb6");

    /// <summary>
    /// Member files a refusal may call VB6's when VB6's type isn't registered: the unambiguous VB6 source extensions only.
    /// <c>.cls</c> is left out because it is equally a LaTeX class file (#279), and HexIDE's own text says only what it knows.
    /// </summary>
    public static ProjectFileReservation Members { get; } = new(Id, Title,
        HexIDE.Lsp.DocumentLanguage.UnambiguousVb6Extensions.Select(extension => "*" + extension).ToArray());


    /// <summary>
    /// A project holding Form1. Its form's class comes from the format the project is made for (#3411 slice 2.4f-2), so
    /// it's made through <see cref="HexIDE.Addins.IAddinProjectTemplate.Create(string, IProjectFileFormat)"/>, and
    /// <c>Create(name)</c> alone, which names no format, throws.
    /// </summary>
    public static readonly IProjectTemplate StandardExe = new ProjectTemplate("Standard EXE", "standard_exe", true, (name, format) =>
    {
        var project = new ProjectDefinition(VBProjectType.EXE, name);

        if (format?.NewRootClass("VB.Form") is not { Role: ComponentRole.Form } formClass)
            throw new InvalidOperationException(format is null
                ? "The Standard EXE template makes its form in the format it's given, and was given none."
                : $"The format {format.GetType().FullName} gives no form class for a Standard EXE project's Form1.");
        var form = FormDefinition.NewForm(project, formClass, "Form1");
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

    /// <summary>
    /// The kinds of module its format reads, which an add-in type that opens its projects itself may hold too: every kind,
    /// and forms. A VBA document module is a class to it.
    /// </summary>
    public static IReadOnlySet<ModuleKind> MemberKinds { get; } = new HashSet<ModuleKind>(Enum.GetValues<ModuleKind>());
}
