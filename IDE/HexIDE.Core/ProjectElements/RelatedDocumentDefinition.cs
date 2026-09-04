using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HexIDE.Runtime.ProjectElements;

/// <summary>
/// A file carried by a project but not compiled by it — a README, a spec, notes beside the source.
/// VB6 writes these as <c>RelatedDoc=</c> and, unlike every other project member, never opens them itself.
///
/// <para>
/// <b>Deliberately not a fifth <see cref="ModuleKind"/>.</b> A related document is not a module that happens
/// to hold prose; it is a different kind of thing, and modelling it as a module would put a <c>.md</c> into
/// <c>ProjectDefinition.Modules</c> — the collection the interpreter enumerates, the save loop renames by
/// extension, and the header writer prepends <c>Attribute VB_Name</c> to. Every one of those would then need
/// a guard, and a missed guard is silent damage to a file the developer never edited (see the corruption
/// this prevents, hexide-io/HexIDE#245). A separate collection makes the whole class of mistake
/// unreachable: a related document cannot be run, renamed or rewritten because it was never in the
/// collection those things iterate.
/// </para>
/// </summary>
public partial class RelatedDocumentDefinition : INotifyPropertyChanged
{
    private string? absolutePath;
    private string name;

    public ProjectDefinition Owner { get; }

    /// <summary>Display name. Derived from the filename — a <c>RelatedDoc=</c> line carries no name field.</summary>
    public string Name
    {
        get => name;
        set => SetField(ref name, value);
    }

    public string? AbsolutePath
    {
        get => absolutePath;
        set => SetField(ref absolutePath, value);
    }

    /// <summary>
    /// The item line to re-emit verbatim, when this document was read from a line that was <em>not</em>
    /// <c>RelatedDoc=</c>.
    ///
    /// <para>
    /// VB6's "Add As Related Document" tickbox is not sticky and defaults off, so VB6 itself writes
    /// non-code files as ordinary <c>Module=Name; Thing.md</c> entries. HexIDE reclassifies those on read —
    /// otherwise the file is treated as VB6 source and corrupted on the next save — but it must not
    /// <em>rewrite</em> the line, because reclassifying is a guess about intent and silently editing a
    /// developer's project file on the strength of a guess is not a trade worth making. The line changes
    /// only when the developer actually changes the project's membership.
    /// </para>
    /// </summary>
    public string? OriginalItemLine { get; }

    public RelatedDocumentDefinition(
        ProjectDefinition owner, string name, string? absolutePath = null, string? originalItemLine = null)
    {
        Owner = owner;
        this.name = name;
        this.absolutePath = absolutePath;
        OriginalItemLine = originalItemLine;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
