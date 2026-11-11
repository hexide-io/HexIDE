namespace HexIDE.Addins;

/// <summary>
/// A kind of project file an add-in reads, such as an RDCore <c>.rdproj</c>, which HexIDE then opens as a project: its
/// modules and their kinds, its references and folders, with code editing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Membership through the type's writer.</b> Where the type has a <see cref="ProjectFile"/> writer, HexIDE writes the
/// project file from what it renders, and offers whatever the render carries: adding and removing members, References and
/// Project Properties' name and description. Without one, the project file is the add-in's program's to write, never
/// HexIDE's: saving writes the modules that were edited and not the project file, and none of those is offered. Components
/// is never offered, and HexIDE neither makes nor runs the project. Renaming a module is offered either way, as its name is
/// the <c>Attribute VB_Name</c> in its own file. A change made outside HexIDE reaches it the other way, through
/// <see cref="IAddinProjectChanges"/>.
/// </para>
/// <para>
/// <b>Opened only when the user opens one.</b> HexIDE asks the add-in to read a file it names, from Open Project, the
/// recent projects list or a project group, and at no other time.
/// </para>
/// </remarks>
/// <param name="Title">What the Open Project dialog calls the files, such as <c>"RDCore projects"</c>.</param>
public sealed record AddinProjectType(string Title)
{
    /// <summary>
    /// The files it reads: whole file names, such as <c>".rdproj"</c>, or patterns of the form <c>"*.ext"</c>. Compared
    /// case-insensitively, and copied when the type is registered.
    /// </summary>
    public required IReadOnlyList<string> FileNames { get; init; }

    /// <summary>
    /// Reads the project file at a full path. The changes handle is how the add-in tells HexIDE, for as long as the project
    /// stays open, that its membership or references changed. One that throws leaves nothing opened, and its message is
    /// shown. The token is cancelled if the type is taken out before the read ends, as when the add-in is unloaded.
    /// </summary>
    public required Func<string, IAddinProjectChanges, CancellationToken, Task<AddinProjectContent>> OpenAsync { get; init; }

    /// <summary>
    /// What reads the forms, UserControls and PropertyPages its projects list, each from its file's text and companion. Needed
    /// only by a type that lists those: one listed with no reader is left out and named. It is called on the UI thread, for a
    /// project's open and whenever such a file changes, and one that throws is taken not to have read it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Written only where it can also render.</b> Where this is an <see cref="HexIDE.IDE.IFormFileFormat"/>, HexIDE saves
    /// the forms it read through its renders, as it saves a .vbp's forms. Where it only reads, each of its forms opens
    /// read-only and is never written.
    /// </para>
    /// <para>
    /// <b>A format runs often.</b> Besides each save, HexIDE renders a form to tell whether it was edited, which crash recovery
    /// asks every few seconds of every form of every open project, and to refresh the header over its code. All of it is on
    /// the UI thread. One that throws hasn't rendered, and the form is saved by nothing until it renders again.
    /// </para>
    /// <para>
    /// <b>Called only while the type is registered.</b> Once the add-in is turned off, or its consent is withdrawn, HexIDE
    /// neither reads nor renders through it again.
    /// </para>
    /// <para>
    /// The controls on a form are the add-in's own <see cref="HexIDE.Runtime.Components.IComponentClass"/>es, which
    /// <see cref="IHexIdeHost.RegisterComponent"/> offers in the toolbox.
    /// </para>
    /// </remarks>
    public HexIDE.IDE.IFormFileReader? Forms { get; init; }

    /// <summary>
    /// What renders its projects' own files, so that HexIDE can write the changes it offers to them. Without one, HexIDE never
    /// writes a project file of this type, and its membership and settings can only change outside HexIDE.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Where it's written.</b> Only at the path the project was opened from: Save Project As writes it there too, and
    /// asks for no new place. No member is ever written at that path, whichever route names it.
    /// </para>
    /// <para>
    /// <b>A writer runs often.</b> Besides each save, HexIDE renders the project file to tell whether the project was edited,
    /// which crash recovery asks every few seconds and closing asks too, and once when the project opens. All of it is on the
    /// UI thread. One that throws hasn't rendered, and the project file is saved by nothing until it renders again.
    /// </para>
    /// <para>
    /// <b>A file changed outside HexIDE.</b> HexIDE keeps the bytes it last read or wrote. A save that finds the file changed
    /// asks whether to reload it, through <see cref="OpenAsync"/>, overwrite it, or cancel, where HexIDE has changes of its
    /// own to write. Where it has none, the file is left as it is.
    /// </para>
    /// <para>
    /// <b>Called only while the type is registered.</b> Once the add-in is turned off, or its consent is withdrawn, HexIDE
    /// never renders through it again. It asks first about a project file with unsaved changes, as it does about forms.
    /// </para>
    /// </remarks>
    public HexIDE.IDE.IProjectFileWriter? ProjectFile { get; init; }
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

    /// <summary>A form, read through <see cref="AddinProjectType.Forms"/>. Its name is the one the reader gives it.</summary>
    Form,

    /// <summary>A UserControl, read and named as a form is. UserControls are read first, because a form can hold them.</summary>
    UserControl,

    /// <summary>A PropertyPage, read and named as a form is.</summary>
    PropertyPage,
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
    /// that is ignored, so an add-in can stop watching the project's files. Its callbacks run on the thread pool, and one
    /// that throws is logged.
    /// </summary>
    /// <remarks>
    /// An exit that closes no project, as an exit forced without the save prompt is, ends the process with the project
    /// still open, and the add-in with it, so this is not cancelled then.
    /// </remarks>
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
