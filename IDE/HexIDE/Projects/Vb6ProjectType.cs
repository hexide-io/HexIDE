using HexIDE.Addins;
using HexIDE.IDE;

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

    /// <summary>The type, reading and rendering through <paramref name="format"/>.</summary>
    public static AddinProjectType For(IProjectFileFormat format) => new("Visual Basic 6.0 projects")
    {
        FileNames = ["*.vbp"],
        GroupFileNames = ["*.vbg"],
        Format = format,
    };

    /// <summary>Who registers it: HexIDE itself.</summary>
    internal static AddinOrigin Origin { get; } = new(Id, Title, IsFirstParty: true);

    /// <summary>Registers it with <paramref name="types"/>, reading through <paramref name="format"/>.</summary>
    public static IDisposable Register(ContributedProjects types, IProjectFileFormat format) => types.Register(For(format), Origin);
}
