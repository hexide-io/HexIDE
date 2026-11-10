using System.Collections.Generic;

namespace HexIDE.Runtime.Serialization;

/// <summary>What a project group's file names.</summary>
/// <param name="StartupProjectRelativePath">The project F5 starts, as the file gives it, if any.</param>
/// <param name="ProjectRelativePaths">Its projects, in order, as the file gives them.</param>
/// <param name="UnknownLines">Lines nothing models, kept so a save writes them back.</param>
public record SerializedProjectGroup(
    string? StartupProjectRelativePath,
    IReadOnlyList<string> ProjectRelativePaths,
    IReadOnlyList<string> UnknownLines);
