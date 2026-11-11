using System;
using System.Text;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

/// <summary>
/// Renders a project's own file from the project as the IDE holds it, for the IDE to write (#3698). An add-in's project type
/// supplies one through <see cref="HexIDE.Addins.AddinProjectType.ProjectFile"/>, so that adding and removing members,
/// References and Project Properties reach its file.
/// </summary>
/// <remarks>
/// <para>
/// <b>A render, not a write.</b> The IDE renders to tell an edited project from the one it opened, to keep a copy for crash
/// recovery and to check a save before it writes anything, as well as to save. It writes the text itself, in
/// <see cref="Encoding"/>, at the path the project was opened from and nowhere else.
/// </para>
/// <para>
/// <b>The same render every time.</b> A project counts as edited when a fresh render differs from the one taken when it
/// opened or was last saved, so a render that changes on its own, as one with a timestamp in it does, makes the project look
/// edited every time.
/// </para>
/// <para>
/// <b>Refusing.</b> A render that throws has rendered nothing: a save stops before any file is written and the user is
/// shown the message. Throw where the text can't be written faithfully, as where <see cref="Encoding"/> can't hold it.
/// </para>
/// </remarks>
public interface IProjectFileWriter
{
    /// <summary>
    /// Which parts of the project the render writes. Only those can be changed in the IDE: a change the render would leave
    /// out is refused, rather than lost at the next save without a word.
    /// </summary>
    ProjectFileParts Carries { get; }

    /// <summary>What the file's text is written in. A BOM is written where its preamble has one.</summary>
    Encoding Encoding { get; }

    /// <summary>The project's file, as it should read for the project as it is now.</summary>
    /// <param name="project">The project. Its members, references and properties are what the render writes.</param>
    /// <param name="path">The project file's full path, which members' relative paths are taken from.</param>
    /// <param name="pathOf">
    /// Where a form or module that has no file yet will be written by the save under way, or null for one that has its file.
    /// Null itself where nothing is being saved, when a member with no file can be left out.
    /// </param>
    string RenderProject(ProjectDefinition project, string path, Func<object, string?>? pathOf);
}

/// <summary>The parts of a project a project file's render can carry.</summary>
[Flags]
public enum ProjectFileParts
{
    /// <summary>None: the file is written as it was read, and nothing in it changes.</summary>
    None = 0,

    /// <summary>The project's members: Add and Remove.</summary>
    Members = 1,

    /// <summary>Its references: the References dialog.</summary>
    References = 2,

    /// <summary>Its general properties, its name and description: Project Properties.</summary>
    Properties = 4,
}
