using HexIDE.Language;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

/// <summary>
/// The format a project's documents are read, written and guarded through: the project's own (#4053, #3411 slice 10b).
/// </summary>
/// <remarks>
/// <para>
/// Whatever opens or makes a project sets its format: the project type that read its file, or for a type that opens its
/// projects itself and gives no format of its own, the format of the type registered for its members' kind. An add-in's
/// <c>.rdproj</c> holds VB6 <c>.bas</c>, <c>.cls</c> and <c>.frm</c> files, so VB6's format serves it. Nothing injects a
/// format any more, so a project nothing set one on has none, and asking for it throws, naming the project.
/// </para>
/// <para>
/// The read-only regions are the format's. A format that gives none guards nothing.
/// </para>
/// </remarks>
public static class ProjectFormats
{
    /// <summary>
    /// The format of a project nothing set one on, <b>for test fixtures only</b>, which build projects by hand. Set by a module
    /// initializer in each test assembly that needs it. The product never sets it: there, a project with no format is a defect
    /// that throws.
    /// </summary>
    internal static IProjectFileFormat? TestFixturesOnlyFallback { get; set; }

    public static IProjectFileFormat Of(ProjectDefinition project) =>
        project.Format ?? TestFixturesOnlyFallback ?? throw new InvalidOperationException(
            $"Project '{project.Name}' has no file format: nothing that opened or made it set one, and no registered project type "
            + "reads its members.");

    // The format of the project, or for no project, the test fixtures' fallback: in the product every code window shows a
    // document a project owns, and only a test's bare window has none.
    private static FormatRegions? RegionsOf(ProjectDefinition? project) =>
        (project is null ? TestFixturesOnlyFallback : Of(project))?.Regions;

    /// <summary>The reader of the project's format's read-only regions, or one that finds none.</summary>
    public static IReadOnlyRegionReader ReaderOf(ProjectDefinition? project) => RegionsOf(project)?.Reader ?? NoReadOnlyRegions.Instance;

    /// <summary>The rules of the project's format's read-only regions, or null where it has none.</summary>
    public static IReadOnlyRegionRules? RulesOf(ProjectDefinition? project) => RegionsOf(project)?.Rules;

    /// <summary>The writer of the project's format's read-only regions, or null where it has none.</summary>
    public static IReadOnlyRegionWriter? WriterOf(ProjectDefinition? project) => RegionsOf(project)?.Writer;
}

/// <summary>A reader for a format that gives no read-only regions: it finds none.</summary>
public sealed class NoReadOnlyRegions : IReadOnlyRegionReader
{
    public static readonly NoReadOnlyRegions Instance = new();

    private NoReadOnlyRegions() { }

    public IReadOnlyList<TextRegion> Regions(string text, int prefixLength) => [];
}
