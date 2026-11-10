using System;
using System.Collections.Generic;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

/// <summary>
/// What a project's files say: how the IDE's model of a project, a form or a module comes out as a file. The IDE does the
/// writing itself, so every save is atomic and writes a companion before its text; a format only renders.
/// </summary>
/// <remarks>
/// <para>
/// <b>A render writes nothing and changes nothing.</b> A save renders, and so does everything that only needs to know what
/// a save would write: the dirty check and the baselines it compares against, the check that a file's code page can hold
/// its text, crash recovery's capture, and the project text a language server imports.
/// </para>
/// <para>
/// <b>The same model renders the same.</b> A document counts as edited when a fresh render differs from the last one, so a
/// render that varies shows every project as edited.
/// </para>
/// </remarks>
public interface IProjectFileFormat
{
    /// <summary>A form's file, as it would be written under this name, by which its text cites its companion.</summary>
    RenderedFile RenderForm(FormDefinition form, string fileName);

    /// <summary>A module's file, as it would be written under this name.</summary>
    RenderedFile RenderModule(ModuleDefinition module, string fileName);

    /// <summary>The part of a form's file above its code, which the code window shows in front of the code.</summary>
    string RenderDesignerHeader(FormDefinition form, string fileName);

    /// <summary>A project's file, as it would be written at this path.</summary>
    /// <param name="project">The project.</param>
    /// <param name="projectPath">Where the file would be, which its members are named relative to.</param>
    /// <param name="pathOf">
    /// Where a form or module will be when this text is written, for a caller checking a save before it moves anything;
    /// null for one staying where it is.
    /// </param>
    /// <param name="includeRelatedDocuments">Whether the file names the documents the project carries.</param>
    string RenderProject(ProjectDefinition project, string projectPath, Func<object, string?>? pathOf = null,
        bool includeRelatedDocuments = true);

    /// <summary>A project group's file, as it would be written at this path.</summary>
    /// <param name="groupPath">Where the file would be, which its projects are named relative to.</param>
    /// <param name="projects">The projects in it, in order.</param>
    /// <param name="startupProject">The one F5 starts, if any.</param>
    /// <param name="unknownLines">Lines the group's file held that nothing models, kept as they were.</param>
    /// <param name="pathOf">
    /// Where a project will be when this text is written; null for one staying where it is.
    /// </param>
    string RenderGroup(string groupPath, IReadOnlyList<ProjectDefinition> projects, ProjectDefinition? startupProject,
        IReadOnlyList<string>? unknownLines, Func<object, string?>? pathOf);

    /// <summary>
    /// The components a form's own block holds, in the order its file lists them. The designer places carried blocks among
    /// these, so it asks here rather than working the order out again.
    /// </summary>
    IReadOnlyList<ComponentInstance> RootChildren(IReadOnlyList<ComponentInstance> components, ComponentInstance root);
}

/// <summary>A file as a format renders it.</summary>
/// <param name="Text">Its text.</param>
/// <param name="Companion">The binary file that goes with it, or null when it has none.</param>
public readonly record struct RenderedFile(string Text, byte[]? Companion);
