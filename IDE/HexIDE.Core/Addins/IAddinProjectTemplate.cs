using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Addins;

public interface IAddinProjectTemplate
{
    string Name { get; }
    bool Supported { get; }
    ProjectDefinition Create(string name);
}
