using System;
using System.Collections.Generic;
using System.Linq;

namespace HexIDE.Runtime.ProjectElements;

/// <summary>
/// Finding a document by the names a human or a client knows it by.
///
/// <para>
/// One search, shared by every surface that takes a name from outside the IDE — the automation tools, the
/// add-in host — because they used to disagree. Some searched the startup project only, some searched every
/// open editor, and one built a key out of the caller's own spelling rather than the document's
/// (hexide-io/HexIDE#467).
/// </para>
///
/// <para>
/// What it deliberately does <b>not</b> do is decide what "none" or "more than one" means. A tool answering
/// JSON and an add-in method returning void owe their callers different things, and a helper that picked for
/// them would have to pick badly for one of them.
/// </para>
/// </summary>
public static class DocumentLookup
{
    /// <summary>Every form, module and class of a project, as identities.</summary>
    /// <remarks>
    /// A UserControl or PropertyPage appears once, as its module: its designer half is not in
    /// <see cref="ProjectDefinition.Forms"/>, and <see cref="DocumentIdentity.For(FormDefinition)"/> would
    /// resolve it to the same module even if it were.
    /// </remarks>
    public static IEnumerable<DocumentIdentity> DocumentsOf(ProjectDefinition project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.Forms.Select(DocumentIdentity.For)
            .Concat(project.Modules.Select(DocumentIdentity.For))
            .Distinct();
    }

    /// <summary>
    /// Every document answering to a name, across the projects given.
    /// </summary>
    /// <param name="projects">The projects to search — normally every loaded one.</param>
    /// <param name="name">The document's VB6 name. Compared without regard to case, as VB6 does.</param>
    /// <param name="project">
    /// The name of the project to look in, or null to search them all. Also compared without regard to case.
    /// </param>
    /// <returns>
    /// Every match, in the order the projects were given. More than one is possible and is not an error
    /// here: VB6 requires a name to be unique only within a project, so with a group open two documents may
    /// genuinely answer to <c>Module1</c>. The caller says what to do about it.
    /// </returns>
    public static IReadOnlyList<DocumentIdentity> Find(
        IEnumerable<ProjectDefinition> projects, string name, string? project = null)
    {
        ArgumentNullException.ThrowIfNull(projects);
        return [.. projects
            .Where(p => project is null || string.Equals(p.Name, project, StringComparison.OrdinalIgnoreCase))
            .SelectMany(DocumentsOf)
            .Where(document => document.IsNamed(name))];
    }
}
