namespace HexIDE.Addins;

/// <summary>
/// A kind of project file an add-in reads, such as an RDCore <c>.rdproj</c>, which HexIDE then opens as a project: its
/// modules and their kinds, its references and folders, with code editing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read-only membership.</b> The project file is the add-in's program's to write, never HexIDE's: saving writes the
/// modules that were edited and not the project file, and Add File, Remove and Rename are not offered for the project. A
/// change to the project's membership reaches HexIDE the other way, through <see cref="IAddinProjectChanges"/>.
/// </para>
/// <para>
/// <b>Opened only when the user opens one.</b> HexIDE asks the add-in to read a file it names, from Open Project or the
/// recent projects list, and at no other time.
/// </para>
/// </remarks>
/// <param name="Title">What the Open Project dialog calls the files, such as <c>"RDCore projects"</c>.</param>
public sealed record AddinProjectType(string Title)
{
    /// <summary>
    /// The files it reads: whole file names, such as <c>".rdproj"</c>, or patterns of the form <c>"*.ext"</c>. Compared
    /// case-insensitively.
    /// </summary>
    public required IReadOnlyList<string> FileNames { get; init; }

    /// <summary>
    /// Reads the project file at a full path. The changes handle is how the add-in tells HexIDE, for as long as the project
    /// stays open, that its membership or references changed. One that throws leaves nothing opened, and its message is
    /// shown.
    /// </summary>
    public required Func<string, IAddinProjectChanges, CancellationToken, Task<AddinProjectContent>> OpenAsync { get; init; }
}

/// <summary>What a project file holds, as the add-in read it.</summary>
/// <param name="Name">The project's name.</param>
public sealed record AddinProjectContent(string Name)
{
    /// <summary>Its modules and other files, in the order they are listed.</summary>
    public IReadOnlyList<AddinProjectMember> Members { get; init; } = [];

    /// <summary>Its references, in priority order.</summary>
    public IReadOnlyList<AddinProjectReference> References { get; init; } = [];

    /// <summary>Its folders, relative to the project file's folder, including any that hold nothing.</summary>
    public IReadOnlyList<string> Folders { get; init; } = [];
}

/// <summary>A module or other file of a contributed project.</summary>
/// <param name="Path">Its full path. A module's name is its <c>Attribute VB_Name</c>, read from the file.</param>
/// <param name="Kind">What it is.</param>
public sealed record AddinProjectMember(string Path, AddinMemberKind Kind);

/// <summary>What a member of a contributed project is.</summary>
public enum AddinMemberKind
{
    /// <summary>A standard module.</summary>
    StandardModule,

    /// <summary>A class module.</summary>
    ClassModule,

    /// <summary>
    /// The code module of a host's document, such as an Excel worksheet's. It is there to be read and edited: no host is
    /// presumed, so nothing in HexIDE runs or instantiates it.
    /// </summary>
    DocumentModule,

    /// <summary>A file the project carries that is not a module, such as a README.</summary>
    Other,
}

/// <summary>A type library a contributed project references.</summary>
public sealed record AddinProjectReference(string Name, Guid? Guid = null, int? Major = null, int? Minor = null, string? Path = null);

/// <summary>
/// How an add-in tells HexIDE that a project it opened changed below the level of the whole project, such as a module its
/// program added. Each change is applied as the same change to a .vbp project would be, so every view of the project
/// follows it.
/// </summary>
public interface IAddinProjectChanges
{
    /// <summary>
    /// Cancelled once HexIDE no longer has the project open: it was closed, or its open failed. A change reported after
    /// that is ignored, so an add-in can stop watching the project's files.
    /// </summary>
    CancellationToken Closed { get; }

    /// <summary>A member joined the project.</summary>
    void MemberAdded(AddinProjectMember member);

    /// <summary>The member at this path left the project.</summary>
    void MemberRemoved(string path);

    /// <summary>The member at <paramref name="oldPath"/> is now at <paramref name="newPath"/>, its code as it was.</summary>
    void MemberMoved(string oldPath, string newPath);

    /// <summary>The project's references are now these, in priority order.</summary>
    void ReferencesChanged(IReadOnlyList<AddinProjectReference> references);
}
