using System;
using System.Runtime.CompilerServices;

namespace HexIDE.Runtime.ProjectElements;

/// <summary>
/// What a form, module or class <em>is</em> to the IDE, as opposed to what it is called.
///
/// <para>
/// A document has several names — its own VB6 name, the file it is saved in, and the name language servers
/// know it by — and every one of them changes while the developer works: a rename, a first save, a Save As,
/// a rename of the project it belongs to. Everything a developer builds up on a document (breakpoints,
/// bookmarks, where the debugger is stopped, which diagnostics belong to it) has to survive all of those, and
/// it can only do that if it is attached to the document itself. So this is the key, and a name never is.
/// </para>
///
/// <para>
/// It is the document's <b>definition</b>, compared by reference, qualified by the project that owns it. None
/// of rename, first save, Save As or a project rename replaces the definition, so none of them moves a mark.
/// It does not survive unloading the project, and does not need to: the per-user sidecar carries state across
/// that.
/// </para>
///
/// <para>
/// <b>Deliberately not a string.</b> The stores this replaces disagreed about case — two compared ordinally,
/// the debugger ignored case and the diagnostic ledger normalised — so the same document answered to three
/// different keys depending on which one asked. A value with its own equality ends that argument rather than
/// settling it three times. It is also not an allocated id: there is nothing to persist, because it lives
/// only for as long as the project is loaded.
/// </para>
///
/// <para>
/// <b>Carried files have no identity here, and that is not an omission.</b> A <c>.txt</c> or <c>.md</c> a
/// project carries opens in the plain-text editor, which attaches neither gutter — the bookmark and
/// breakpoint margins are installed by the VB6 code editor alone. Nothing keys per-document state on a
/// carried file, so nothing here needs to name one.
/// </para>
/// </summary>
/// <remarks>
/// A class rather than a struct so that it cannot be <c>default</c>. A <c>default</c> struct would carry a
/// null definition into a dictionary key and throw at the first lookup, which is precisely the kind of
/// silent-until-it-is-not failure this type exists to remove.
/// </remarks>
public sealed class DocumentIdentity : IEquatable<DocumentIdentity>
{
    private readonly FormDefinition? form;
    private readonly ModuleDefinition? module;

    private DocumentIdentity(FormDefinition? form, ModuleDefinition? module)
    {
        this.form = form;
        this.module = module;
    }

    /// <summary>The identity of a standard module, class module, UserControl or PropertyPage.</summary>
    public static DocumentIdentity For(ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        return new DocumentIdentity(null, module);
    }

    /// <summary>
    /// The identity of a form — or, when the form is the designer half of a UserControl or PropertyPage, the
    /// identity of the module that owns it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>.ctl</c> or <c>.pag</c> is one file with two halves, and it gets one identity. This mirrors what
    /// the editor already does: <c>EditorService.EditCode(FormDefinition)</c> redirects a designer's View
    /// Code to the module door for the same reason, so that one file has one tab and one buffer (#152). Were
    /// this to answer with the form part instead, a UserControl would have two identities and its
    /// breakpoints would land on whichever half happened to ask.
    /// </para>
    /// <para>
    /// Read off the form rather than found by scanning the project's modules, which would be wrong in the
    /// window between a UserControl's two halves being joined and its module being added to the project —
    /// several statements, in both the creation path and the load path. A scan would answer "a plain form"
    /// there, and that identity compares unequal to the one every later call produces.
    /// </para>
    /// </remarks>
    public static DocumentIdentity For(FormDefinition form)
    {
        ArgumentNullException.ThrowIfNull(form);
        return form.OwningModule is { } module
            ? new DocumentIdentity(null, module)
            : new DocumentIdentity(form, null);
    }

    /// <summary>The project this document belongs to. Two projects in a group may each hold a
    /// <c>Module1</c>, and VB6 requires a name to be unique only within a project, so an identity without
    /// this could not tell them apart.</summary>
    public ProjectDefinition Project => module?.Owner ?? form!.Owner;

    /// <summary>
    /// The document's current VB6 name, read live.
    /// </summary>
    /// <remarks>
    /// Read live, never captured. A name captured when a gutter attaches and recomputed when a command runs
    /// is the disagreement this change removes (#269): after a rename the two no longer name the same thing,
    /// and the mark is shown in one place and honoured in another.
    /// </remarks>
    public string Name => module?.Name ?? form!.Name;

    /// <summary>The module, or null for a form.</summary>
    public ModuleDefinition? Module => module;

    /// <summary>
    /// The form — the document itself for a form, and null for a module, <b>including</b> a UserControl or
    /// PropertyPage whose designer half is reachable through <see cref="Module"/>'s <c>FormPart</c>.
    /// </summary>
    public FormDefinition? Form => form;

    /// <summary>True for a form; false for a standard module, class, UserControl or PropertyPage.</summary>
    public bool IsForm => module is null;

    /// <summary>
    /// The file this document is saved in, or null when it has none yet.
    /// </summary>
    /// <remarks>
    /// For a UserControl or PropertyPage this is the <em>module's</em> path. The two halves diverge today —
    /// creation sets only the module's, and the code window's Save repoints the form part's alone (#474) —
    /// and the module's is the one the project file names and the one the save event carries.
    /// </remarks>
    public string? AbsolutePath => module?.AbsolutePath ?? form!.AbsolutePath;

    /// <summary>
    /// What this document is written as for a human or an automation client: <c>&lt;Project&gt;/&lt;Name&gt;</c>.
    /// </summary>
    /// <remarks>
    /// For display and lookup only. <b>Nothing keys on it</b> — it is built from two names, both of which
    /// change, which is the whole reason this type exists. Lookup through it is case-insensitive, because VB6
    /// names are.
    /// </remarks>
    public string Display => $"{Project.Name}/{Name}";

    /// <summary>Whether this document answers to a name, compared as VB6 compares names.</summary>
    public bool IsNamed(string documentName) =>
        string.Equals(Name, documentName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this document belongs to a project, compared by reference as identity is.</summary>
    public bool IsIn(ProjectDefinition project) => ReferenceEquals(Project, project);

    /// <summary>
    /// The one object this identity is: the module, or the form for a document that has no module.
    /// </summary>
    /// <remarks>
    /// Equality is reference equality on this, which is what makes the identity survive every rename. It is
    /// exposed because the two stores hold it as a dictionary key and the equality has to be inspectable, not
    /// because a caller should switch on its type — <see cref="Module"/> and <see cref="Form"/> are for that.
    /// </remarks>
    public object Definition => (object?)module ?? form!;

    public bool Equals(DocumentIdentity? other) =>
        other is not null && ReferenceEquals(Definition, other.Definition);

    public override bool Equals(object? obj) => Equals(obj as DocumentIdentity);

    // The definition's own reference hash, not its name's: a name changes, and a key whose hash changed
    // underneath a dictionary is lost rather than moved.
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(Definition);

    public static bool operator ==(DocumentIdentity? left, DocumentIdentity? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(DocumentIdentity? left, DocumentIdentity? right) => !(left == right);

    public override string ToString() => Display;
}
