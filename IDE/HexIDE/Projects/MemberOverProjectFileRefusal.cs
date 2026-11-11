namespace HexIDE.Projects;

/// <summary>
/// A save would write a form or module over its own project's file, which a Save As can choose (#3698). It ends the save
/// before anything is written, as a member with no file in the project file does.
/// </summary>
/// <remarks>
/// <see cref="Exception.Message"/> is the sentence an automation caller receives; a person sees the localized warning
/// instead.
/// </remarks>
public sealed class MemberOverProjectFileRefusal(string member, string projectFile)
    : OperationCanceledException($"{member} would be written over {projectFile}, its project's own file, so nothing was written.")
{
    /// <summary>The form or module, by name.</summary>
    public string Member { get; } = member;

    /// <summary>The project file, by file name.</summary>
    public string ProjectFile { get; } = projectFile;
}
