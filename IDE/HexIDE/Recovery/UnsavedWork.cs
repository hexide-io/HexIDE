namespace HexIDE.Recovery;

/// <summary>
/// One loaded project's unsaved work, rendered as a save would write it, for crash recovery (hexide-io/HexIDE#499).
/// </summary>
/// <param name="Name">The project's name.</param>
/// <param name="Path">The project file's path, or null for a project that has never been saved.</param>
/// <param name="ProjectFileHash">
/// The hash of the project file as this run last read or wrote it, which is what a moved project is matched by when
/// nothing is left at <paramref name="Path"/>. Null when the project has no file.
/// </param>
/// <param name="Encoding">The project file's code page, which a restore writes it in, as a save would.</param>
/// <param name="ProjectFile">The project file's text, when a save would change it; otherwise null.</param>
/// <param name="Documents">Every form and module the project holds, unsaved or not, so a never-saved project can be rebuilt whole.</param>
/// <param name="RenderedAt">
/// Where <paramref name="ProjectFile"/>'s text was rendered: <paramref name="Path"/>, or for a project with no file, a
/// private folder of its own. The text names each member relative to it, so a restore puts the files back there.
/// </param>
/// <param name="Scratch">
/// The project is in a scratch folder the IDE made, as <c>--newproject</c>'s is. Nobody opens a project from there, so
/// its work is offered at start, as a never-saved project's is.
/// </param>
public sealed record UnsavedProject(
    string Name,
    string? Path,
    string? ProjectFileHash,
    Vb6TextEncoding Encoding,
    UnsavedFile? ProjectFile,
    IReadOnlyList<UnsavedDocument> Documents,
    string RenderedAt = "",
    bool Scratch = false);

/// <summary>A form or module in an <see cref="UnsavedProject"/>.</summary>
/// <param name="Name">Its name within the project, which is how a restore finds it again.</param>
/// <param name="Kind"><c>Form</c>, or the <see cref="ModuleKind"/> of a module.</param>
/// <param name="Path">Its file, or null when it has none yet.</param>
/// <param name="Encoding">The code page a save writes it in.</param>
/// <param name="Content">What a save would write, or null when it has nothing unsaved.</param>
/// <param name="DiskHash">
/// The hash of its file as this run last read or wrote it, so a restore can say when the file has changed since.
/// Null when it has no file.
/// </param>
public sealed record UnsavedDocument(
    string Name, string Kind, string? Path, Vb6TextEncoding Encoding, UnsavedFile? Content, string? DiskHash = null);

/// <summary>
/// A file as a save would write it: its text under the file name it would have, and its binary companion when it has
/// one. A form's text cites its companion by that name.
/// </summary>
public sealed record UnsavedFile(string FileName, string Text, string? CompanionFileName = null, byte[]? Companion = null);
