using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.IDE;

/// <summary>
/// What a project's files say: how they become the IDE's model of a project, a form or a module, and how that model comes
/// out as files again. The IDE does the writing itself, so every save is atomic and writes a companion before its text; a
/// format only renders.
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
public interface IProjectFileFormat : IFormFileFormat
{
    /// <summary>A module's file, as it would be written under this name.</summary>
    RenderedFile RenderModule(ModuleDefinition module, string fileName);

    /// <inheritdoc/>
    RenderedFile IFormFileFormat.RenderDesignerFile(ModuleDefinition module, string fileName) => RenderModule(module, fileName);

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

    /// <summary>Reads a project's file and every member it names.</summary>
    /// <param name="path">The project's file.</param>
    /// <param name="hostAnsi">The code page a file without a byte-order mark is read in.</param>
    /// <param name="fileRead">
    /// Told each member's file and companion as it was on disk, so the IDE can tell later whether it changed. Not told the
    /// project's own file, which comes back in the result.
    /// </param>
    /// <param name="problems">Told what stopped a member loading. A project file that can't be read at all throws.</param>
    Task<ProjectFileRead> ReadProjectAsync(string path, Vb6TextEncoding hostAnsi, Action<string, byte[]>? fileRead,
        IDeserializeErrorSink problems);

    /// <summary>What a project group's file names.</summary>
    SerializedProjectGroup ReadGroup(string text);

    /// <summary>
    /// A module's file, as the part kept for its next save and the part the code window edits. The header is empty when
    /// there is nothing to keep.
    /// </summary>
    (string Header, string Body) SplitModule(string text, ModuleKind kind);

    /// <summary>
    /// The text the code window puts in front of a form's code. Its whole text is <c>CodePrefix(x) + x.Code</c>;
    /// the reader reads its regions with this prefix's length.
    /// </summary>
    string CodePrefix(FormDefinition form);

    /// <summary>
    /// The text the code window puts in front of a module's code. Its whole text is <c>CodePrefix(x) + x.Code</c>;
    /// the reader reads its regions with this prefix's length.
    /// </summary>
    string CodePrefix(ModuleDefinition module);

    /// <summary>
    /// Whether the module's kind carries its name in the prefix: true for .bas and .cls.
    /// After read, measured .bas/.cls shapes carry their module-level VB_Name in the prefix.
    /// A rename also rewrites a name left in the code, as after an editor write to a headerless module.
    /// </summary>
    bool PrefixCarriesName(ModuleDefinition module);

    /// <summary>The extension of the companion that goes with this designer file.</summary>
    string CompanionExtension(string path);

    /// <inheritdoc/>
    string? IFormFileReader.CompanionPath(string path) => System.IO.Path.ChangeExtension(path, CompanionExtension(path));

    /// <summary>The line a project's file holds for this member, kept when it fails to load so a save doesn't drop it.</summary>
    string MemberLine(ModuleKind kind, string name, string relativePath);

    /// <summary>Whether a property's stored value is a citation of a record in the companion, and not a value.</summary>
    bool IsCompanionCitation(string value);
}

/// <summary>A file as a format renders it.</summary>
/// <param name="Text">Its text.</param>
/// <param name="Companion">The binary file that goes with it, or null when it has none.</param>
public readonly record struct RenderedFile(string Text, byte[]? Companion);

/// <summary>What reading a project's file gave.</summary>
/// <param name="Project">The project, with every member that loaded.</param>
/// <param name="SkippedDocuments">Members the file names that the IDE does not open, as the file names them, to tell the user.</param>
/// <param name="ReadByteForByte">
/// The files the host's code page couldn't decode, which were read byte for byte instead: the project's file by its name, a
/// member by its path as the project's file gives it.
/// </param>
/// <param name="ProjectFileBytes">The project's file as it was on disk when it was read.</param>
public sealed record ProjectFileRead(ProjectDefinition Project, IReadOnlyList<string> SkippedDocuments,
    IReadOnlyList<string> ReadByteForByte, byte[] ProjectFileBytes);
