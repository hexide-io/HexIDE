using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Addins;

public interface IAddinProjectTemplate
{
    string Name { get; }
    bool Supported { get; }
    ProjectDefinition Create(string name);

    /// <summary>
    /// A new project, made for <paramref name="format"/>: the format the IDE gives it once it's made, which a template asks
    /// for the class of a form it starts with (#3411 slice 2.4f-2). The IDE calls this one. It's asked before the project
    /// has a format of its own, because the IDE adopts the project only after the template returns it. By default, the
    /// project <see cref="Create(string)"/> makes.
    /// </summary>
    ProjectDefinition Create(string name, IProjectFileFormat format) => Create(name);
}
