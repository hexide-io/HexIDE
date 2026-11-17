using Avalonia.Media;
using HexIDE.Addins;

namespace HexIDE.Projects;

public interface IProjectTemplate : IAddinProjectTemplate
{
    IImage Icon { get; }

    /// <summary>The <c>Str.*</c> key for the name shown to the developer. <see cref="IAddinProjectTemplate.Name"/> stays the English identity.</summary>
    string? NameKey { get; }
}
