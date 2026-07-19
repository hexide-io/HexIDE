namespace HexIDE.Tools;

/// <summary>
/// A Project Explorer leaf backed by a project member file on disk (form or module).
/// Gives the tree builder uniform access to the display name and disk location.
/// </summary>
public interface IProjectFileNode : IProjectTreeElement
{
    string Name { get; }
    string? AbsolutePath { get; }
}
