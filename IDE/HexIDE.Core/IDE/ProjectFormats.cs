using HexIDE.Language;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

/// <summary>
/// The format a project's documents are read, written and guarded through: the project's own, which whatever opened or
/// made it sets (#4053), or the caller's fallback for a project nothing opened, such as a test's.
/// </summary>
/// <remarks>
/// The read-only regions are the format's, and the fallback's only where the format gives none. Every format a project is
/// opened with gives them, so that is a test's stand-in format. The fallbacks go when #3411's slice 10b gives every project a
/// format from a registry, and a format with no regions then guards nothing.
/// </remarks>
public static class ProjectFormats
{
    public static IProjectFileFormat Of(ProjectDefinition project, IProjectFileFormat fallback) => project.Format ?? fallback;

    /// <summary>The reader of the project's format's read-only regions.</summary>
    public static IReadOnlyRegionReader ReaderOf(ProjectDefinition project, IReadOnlyRegionReader fallback) =>
        project.Format?.Regions?.Reader ?? fallback;

    /// <summary>The rules of the project's format's read-only regions.</summary>
    public static IReadOnlyRegionRules? RulesOf(ProjectDefinition project, IReadOnlyRegionRules? fallback) =>
        project.Format?.Regions?.Rules ?? fallback;

    /// <summary>The writer of the project's format's read-only regions.</summary>
    public static IReadOnlyRegionWriter? WriterOf(ProjectDefinition project, IReadOnlyRegionWriter? fallback) =>
        project.Format?.Regions?.Writer ?? fallback;
}
