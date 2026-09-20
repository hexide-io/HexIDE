using System;
using System.Collections.Generic;
using System.Linq;

namespace HexIDE.Runtime.ProjectElements;

/// <summary>
/// What a form, module, class or project may be called.
///
/// <para>
/// These are VB6's own rules, measured against the real compiler rather than assumed, and recorded in
/// <c>docs/vb6-fidelity-oracle.md</c>. Enforcing them where a name is chosen turns a failure at build time
/// into a refusal at the moment it is caused — and keeps the name usable as an identifier, because a
/// document with no file is named to a language server by its project and its own name, and two documents
/// sharing one are a single document as far as a server can tell.
/// </para>
/// </summary>
public static class ProjectNaming
{
    /// <summary>
    /// Whether a name is a VB6 name: a letter, then letters, digits and underscores.
    /// </summary>
    /// <remarks>
    /// Deliberately no leading underscore, unlike the keyword normaliser's private predicate: VB6 does not
    /// accept one at the start of an identifier. No length limit is imposed, because none has been measured
    /// — a rule invented here would refuse names the compiler accepts.
    /// </remarks>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!char.IsLetter(name[0])) return false;
        for (var i = 1; i < name.Length; i++)
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_')
                return false;
        return true;
    }

    /// <summary>
    /// Whether a project already has a form, module or class of this name.
    /// </summary>
    /// <param name="project">The project to check.</param>
    /// <param name="name">The proposed name, compared without regard to case as VB6 compares them.</param>
    /// <param name="except">
    /// A document allowed to hold the name already — the one being renamed, so that renaming it to the case
    /// it already has is not a collision with itself.
    /// </param>
    /// <remarks>
    /// Forms and modules share <b>one</b> per-project namespace. A project whose <c>Form=Thing.frm</c> sits
    /// beside <c>Module=Thing</c> does not build, whatever their order and whatever the case: VB6 answers
    /// <c>Name conflicts with existing module, project, or object library</c>, and does not say which of the
    /// two it meant. That is the same message two <c>.bas</c> files sharing a <c>VB_Name</c> produce.
    /// </remarks>
    public static bool IsNameTaken(ProjectDefinition project, string name, DocumentIdentity? except = null) =>
        DocumentLookup.DocumentsOf(project)
            .Any(document => document != except && document.IsNamed(name));

    /// <summary>
    /// The first free <c>{prefix}{n}</c> in a project, counting from 1.
    /// </summary>
    /// <remarks>
    /// The lowest unused index rather than one past the count, so deleting <c>Form1</c> and adding a form
    /// gives <c>Form1</c> back rather than a second <c>Form2</c>. Pooled across every kind, because the
    /// namespace is: a project holding a module called <c>Form1</c> gets <c>Form2</c> for its first form.
    /// </remarks>
    public static string NextFreeName(ProjectDefinition project, string prefix)
    {
        for (var i = 1; ; i++)
        {
            var candidate = prefix + i;
            if (!IsNameTaken(project, candidate)) return candidate;
        }
    }

    /// <summary>
    /// Whether a project of this name is already loaded.
    /// </summary>
    /// <param name="loaded">Every loaded project.</param>
    /// <param name="name">The proposed name, compared without regard to case.</param>
    /// <param name="except">The project being renamed, which may keep its own name.</param>
    /// <remarks>
    /// Two projects in one group may not share a <c>Name=</c>. VB6 refuses the whole group at load —
    /// <c>A project with the name 'Project1' is already loaded.</c> — and nothing in it builds, not even a
    /// third member with a distinct name.
    /// </remarks>
    public static bool IsProjectNameTaken(
        IEnumerable<ProjectDefinition> loaded, string name, ProjectDefinition? except = null) =>
        loaded.Any(p => !ReferenceEquals(p, except)
                        && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The first free <c>{prefix}{n}</c> among the loaded projects, counting from 1.</summary>
    public static string NextFreeProjectName(IEnumerable<ProjectDefinition> loaded, string prefix = "Project")
    {
        var projects = loaded as IReadOnlyCollection<ProjectDefinition> ?? [.. loaded];
        for (var i = 1; ; i++)
        {
            var candidate = prefix + i;
            if (!IsProjectNameTaken(projects, candidate)) return candidate;
        }
    }
}
