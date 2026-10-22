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
    /// Whether a name with a name's shape (<see cref="IsValidName"/>) is still one VB6 was measured to refuse:
    /// <c>Rem</c>, <c>Empty</c>, <c>New</c> or <c>Nothing</c>, in any case.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>These six and no others, because only these were measured refused as a name</b>
    /// (<c>docs/vb6-fidelity-oracle.md</c>). <c>Rem</c> was refused as a variable, a <c>Sub</c>, a parameter, a
    /// <c>Type</c>'s field and an enum member, and is never an identifier anywhere; <c>Empty</c> as a variable, a
    /// reserved word and not a name; <c>New</c> and <c>Nothing</c> as a <c>Sub</c>'s name and as a label; <c>Me</c>
    /// as a module-level array (<c>Private mE(N - 1)</c>, "Expected: identifier", found as a by-product of the
    /// constant-bound probe); and <c>Attribute</c> as a parameter (p07, "Expected: identifier"). The first four were
    /// all task 4.4 knew of; the review of its documentation found the last two. None was accepted in any position
    /// tried, so each is refused in every position, including ones nobody tried. Every other keyword is left alone: which words
    /// VB6 reserves is not derivable (the label probes found <c>Beep</c> and <c>Reset</c> usable and <c>Stop</c>
    /// and <c>Randomize</c> not), and refusing one unmeasured would refuse a name the compiler may accept.
    /// </para>
    /// <para>
    /// Asked of a rename's new name (hexide-io/HexIDE#273 task 4.4), which becomes a member's name in code. A
    /// document's and a project's names are not held to it: nothing measured a form called <c>Rem</c>.
    /// </para>
    /// </remarks>
    public static bool IsReservedWord(string? name) => name is not null && ReservedWords.Contains(name);

    private static readonly HashSet<string> ReservedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Rem", "Empty", "New", "Nothing", "Me", "Attribute",
    };

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
    /// <param name="project">The project the name is for.</param>
    /// <param name="prefix">What the name is numbered after, such as <c>Form</c>.</param>
    /// <param name="alsoTaken">
    /// A further reason a name is not free, which the project's members cannot show. The Add commands pass one that
    /// asks whether the new member's file would be written where a file already is (hexide-io/HexIDE#738).
    /// </param>
    public static string NextFreeName(ProjectDefinition project, string prefix, Func<string, bool>? alsoTaken = null)
    {
        for (var i = 1; ; i++)
        {
            var candidate = prefix + i;
            if (!IsNameTaken(project, candidate) && alsoTaken?.Invoke(candidate) != true) return candidate;
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
