namespace HexIDE.Lsp;

/// <summary>
/// What a workspace-artefact provider is told about the project it is describing.
///
/// <para>
/// <b>A projection, not the live model.</b> A provider runs to produce the content of a file a language
/// server will read, and nothing more; handing it the IDE's own project objects would give it a reach it
/// has no reason to have, and would make the set of things a provider can depend on unbounded the day a
/// third party writes one. Everything here is a value.
/// </para>
/// </summary>
/// <param name="Name">The project's own name, as it names itself rather than as its folder is spelled.</param>
/// <param name="DefinitionPath">
/// The host path of the file that defines the project. Null for a project that has never been saved, which
/// a provider must handle: a descriptor cannot be relative to a file that does not exist yet.
/// </param>
/// <param name="Files">Every file the project holds, in the order the project lists them.</param>
/// <param name="References">The project's references, in declaration order, which is significant.</param>
public sealed record WorkspaceProjectSnapshot(
    string Name,
    string? DefinitionPath,
    IReadOnlyList<WorkspaceProjectFile> Files,
    IReadOnlyList<WorkspaceProjectReference> References);

/// <summary>
/// One file in a project, carrying both spellings of where it is.
/// </summary>
/// <remarks>
/// <b>Both paths, deliberately, and this is not redundancy.</b> A native VB6 project file is a
/// Windows-native format: the paths inside it are backslash-separated on every host, whatever the host's
/// own separator is. A provider writing a descriptor that quotes project-relative paths needs
/// <see cref="ProjectPath"/> verbatim; a provider that must read the file to describe it needs
/// <see cref="HostPath"/>, which is resolved against the filesystem actually running.
///
/// <para>
/// Deriving either from the other with <c>System.IO.Path</c> is the trap that only fails off Windows,
/// because a backslash is an ordinary filename character on Linux and the conversion silently yields
/// something that still looks like a path. Both are supplied so that no provider has to try.
/// </para>
/// </remarks>
/// <param name="ProjectPath">The path as the project file spells it: relative, backslash-separated.</param>
/// <param name="HostPath">The resolved absolute path, in this host's own separators.</param>
/// <param name="Kind">What the project considers this file to be.</param>
public sealed record WorkspaceProjectFile(string ProjectPath, string HostPath, WorkspaceProjectFileKind Kind);

/// <summary>
/// What a project considers one of its files to be.
/// </summary>
/// <remarks>
/// The project's classification, not the editor's. A descriptor generally distinguishes what a backend
/// should analyse from what merely travels with the project, and that is a property of how the project
/// lists a file rather than of how the IDE happens to be displaying it.
/// </remarks>
public enum WorkspaceProjectFileKind
{
    /// <summary>Listed by the project in a way this build does not recognise.</summary>
    Unknown = 0,

    /// <summary>A standard module.</summary>
    Module,

    /// <summary>A class module.</summary>
    Class,

    /// <summary>A form.</summary>
    Form,

    /// <summary>A user control.</summary>
    UserControl,

    /// <summary>
    /// A file the project carries but does not compile.
    /// </summary>
    /// <remarks>
    /// The interesting kind for a polyglot workspace: this is where a source file in some other language
    /// lives, and therefore what a descriptor for a backend serving that language would be built from.
    /// </remarks>
    Carried,
}

/// <summary>
/// One of a project's references, as the project file recorded it.
/// </summary>
/// <remarks>
/// <b>Every field but the name is optional, and that is the shape of the data rather than laziness.</b> A
/// native project file records what it was given when the reference was added — some entries carry a
/// registry GUID and a version, some only a path, and a path that was valid on the authoring machine may
/// resolve to nothing here. A provider must be able to describe a reference from whichever parts survived,
/// so the type refuses to promise more than the format does.
///
/// <para>
/// Order is meaningful and is preserved: where two references export the same name, which one wins is
/// decided by their order, so re-sorting this list would change what a program means.
/// </para>
/// </remarks>
/// <param name="Name">The identifier source code uses to qualify this reference's members.</param>
/// <param name="Guid">The registry identifier, where the reference was recorded with one.</param>
/// <param name="Major">The major version, where one was recorded.</param>
/// <param name="Minor">The minor version, where one was recorded.</param>
/// <param name="Lcid">The locale identifier, where one was recorded.</param>
/// <param name="HostPath">The recorded location, in this host's separators, whether or not it exists.</param>
public sealed record WorkspaceProjectReference(
    string Name,
    Guid? Guid = null,
    int? Major = null,
    int? Minor = null,
    int? Lcid = null,
    string? HostPath = null);
