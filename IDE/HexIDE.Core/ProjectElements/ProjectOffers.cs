namespace HexIDE.Runtime.ProjectElements;

/// <summary>
/// What a project's type lets the IDE do with a project of that type. The IDE asks this, not who opened the project, so a
/// type can come to offer one of these without anything else about it changing.
/// </summary>
public sealed record ProjectOffers
{
    /// <summary>The IDE saves the project's own file: it can count as edited, and a language server can be given its text.</summary>
    public bool WritesProjectFile { get; init; }

    /// <summary>Save Project As can write the project's file somewhere new.</summary>
    public bool MovesProjectFile { get; init; }

    /// <summary>
    /// The IDE can render the project's forms: to save one, to compare it with its file, and to keep a copy for crash
    /// recovery. Where it can't, a form is never edited and nothing renders it.
    /// </summary>
    public bool WritesForms { get; init; }

    /// <summary>The IDE can make a program of the project, and hand its project file to VB6's toolchain.</summary>
    public bool Makes { get; init; }

    /// <summary>The IDE's own run engine can start the project. An add-in's debug launch is another route, and doesn't ask this.</summary>
    public bool Runs { get; init; }

    /// <summary>Members can be added and removed in the IDE, by the user, by automation and by crash recovery.</summary>
    public bool ChangesMembership { get; init; }

    /// <summary>The IDE's Project Properties can change the project.</summary>
    public bool SetsProperties { get; init; }

    /// <summary>The IDE's References can change the project.</summary>
    public bool SetsReferences { get; init; }

    /// <summary>The IDE's Components can change the project.</summary>
    public bool SetsComponents { get; init; }

    /// <summary>All of them, which is what a project the IDE read itself offers.</summary>
    public static ProjectOffers Everything { get; } = new()
    {
        WritesProjectFile = true,
        MovesProjectFile = true,
        WritesForms = true,
        Makes = true,
        Runs = true,
        ChangesMembership = true,
        SetsProperties = true,
        SetsReferences = true,
        SetsComponents = true,
    };

    /// <summary>None of them.</summary>
    public static ProjectOffers Nothing { get; } = new();
}
