using System;

namespace HexIDE.Projects;

/// <summary>
/// A new form or module was not added, because its file would have been written where a file already is, or
/// where the project file names one for a member that did not load. Nothing was written.
/// </summary>
/// <remarks>
/// Only a caller that names the member itself can reach this: automation's <c>add_file</c>. The Add commands
/// pick a name that avoids it (<see cref="IProjectService.NextFreeFormName"/>,
/// <see cref="IProjectService.NextFreeModuleName"/>). <see cref="Exception.Message"/> is the sentence that
/// caller receives.
/// </remarks>
public sealed class FileInTheWayRefusal : InvalidOperationException
{
    public FileInTheWayRefusal(string member, string path, bool namedByProject)
        : base(Describe(member, path, namedByProject))
    {
        Member = member;
        Path = path;
    }

    /// <summary>The name the new member would have had.</summary>
    public string Member { get; }

    /// <summary>The file in the way, as a path on this machine.</summary>
    public string Path { get; }

    private static string Describe(string member, string path, bool namedByProject) =>
        namedByProject
            ? $"'{member}' was not added: the project file already names {path}, for a member that did not load, "
              + "and the new file would be loaded in its place. Nothing was written. Choose another name."
            : $"'{member}' was not added: {path} is already there, where its file or the binary file beside it "
              + "would be written. Nothing was written. Choose another name, or use Add File to bring that file "
              + "into the project.";
}
