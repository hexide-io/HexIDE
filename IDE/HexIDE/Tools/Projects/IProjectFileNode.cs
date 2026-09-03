namespace HexIDE.Tools;

/// <summary>
/// A Project Explorer leaf backed by a project member file on disk (form or module).
/// Gives the tree builder uniform access to the display name and disk location.
/// </summary>
public interface IProjectFileNode : IProjectTreeElement
{
    string Name { get; }
    string? AbsolutePath { get; }

    /// <summary>
    /// Where this member actually lives, when that is not what its position in the tree implies —
    /// null otherwise. Set by <see cref="ProjectTreeBuilder"/> as the tree is built, because the
    /// anchor a member is measured against belongs to the project, not to the member.
    /// </summary>
    string? LocationCaption { get; set; }
}
