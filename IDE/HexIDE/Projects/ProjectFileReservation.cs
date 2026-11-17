namespace HexIDE.Projects;

/// <summary>
/// Project files that HexIDE's own reader reads, which no add-in's project type may take, whenever either registers
/// (#3411 slice 10b). They're known when <see cref="ContributedProjects"/> is made, so the order types arrive in can't
/// change who reads a file.
/// </summary>
/// <param name="Owner">The id of what reads them: a first-party project type's add-in id, or another reader's.</param>
/// <param name="Title">What a refusal calls their reader.</param>
/// <param name="Patterns">The file names and patterns, in the form <see cref="HexIDE.Addins.AddinProjectType.FileNames"/> takes.</param>
public sealed record ProjectFileReservation(string Owner, string Title, IReadOnlyList<string> Patterns)
{
    /// <summary>Whether one of these patterns names the file at <paramref name="path"/>.</summary>
    public bool Covers(string path) => Patterns.Any(name => ContributedProjects.Matches(name, Path.GetFileName(path)));
}
