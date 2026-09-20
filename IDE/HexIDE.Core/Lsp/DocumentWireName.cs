using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Lsp;

/// <summary>
/// The one place a document of a project is turned into the name a language server knows it by.
///
/// <para>
/// This used to be eight string interpolations across seven files — the code editor, the Object Browser,
/// <c>EditorService</c>, <c>ProjectRunnerService</c>, the sidecar twice, the toolchain service and the
/// automation tools — each spelling <c>vb6://…</c> by hand, with no shared helper. Six other places read a
/// module's name back out of that string. One string was doing five jobs at once: the wire name, the
/// breakpoint key, the bookmark key, the sidecar key and what automation reported.
/// </para>
///
/// <para>
/// Those jobs are now separate. <see cref="DocumentIdentity"/> is what the IDE keys on, and this is the only
/// thing that turns one into a name for the wire. The scheme it emits is still HexIDE's own
/// <c>vb6://</c> — changing what a document is <em>called</em> on the wire is the next step, and it is this
/// one function that changes.
/// </para>
/// </summary>
public static class DocumentWireName
{
    /// <summary>
    /// The name a language server knows this document by.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A UserControl or PropertyPage is named as a module, because that is what it is to the IDE: one file,
    /// one identity, its module's. That matches what the code editor has always sent for one.
    /// </para>
    /// <para>
    /// Built from the document's current name, which is the behaviour being replaced rather than endorsed: a
    /// session's name is fixed when it opens and a rename has to be announced, not recomputed underneath a
    /// server. Callers hold a session; this answers what that session should be opened under.
    /// </para>
    /// </remarks>
    public static string For(DocumentIdentity document) =>
        document.IsForm
            ? $"vb6://form/{document.Name}"
            : $"vb6://module/{document.Name}";

    /// <inheritdoc cref="For(DocumentIdentity)"/>
    public static string For(FormDefinition form) => For(DocumentIdentity.For(form));

    /// <inheritdoc cref="For(DocumentIdentity)"/>
    public static string For(ModuleDefinition module) => For(DocumentIdentity.For(module));
}
