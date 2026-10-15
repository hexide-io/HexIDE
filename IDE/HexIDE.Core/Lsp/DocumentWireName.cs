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
/// thing that turns one into a name for the wire.
/// </para>
///
/// <para>
/// <b>The scheme is no longer HexIDE's own.</b> <c>vb6://</c> was a private invention, and a server that had
/// never heard of it could not open the document, resolve anything relative to it, or say anything useful
/// about it. A document with a file is now named by that file, like any other client would name it; a
/// document with no file is named <c>untitled:</c>, which is the spelling the protocol's own reference
/// implementations use for exactly this and which four of the five foreign servers in the suite accept
/// unchanged.
/// </para>
/// </summary>
public static class DocumentWireName
{
    /// <summary>
    /// The name a language server knows this document by: its <c>file:</c> URI once it has a file, and
    /// <c>untitled:&lt;Project&gt;/&lt;Name&gt;&lt;ext&gt;</c> until then.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This answers what a session should be OPENED under. It is not what a live session is called.</b>
    /// A name is fixed when the session opens and changes only by an announced close-and-reopen, because
    /// Make EXE repoints every document's path into a temporary folder and puts it back afterwards — a
    /// name recomputed from the path while that is in flight would hand a server a document it never
    /// opened, twice.
    /// </para>
    /// <para>
    /// A UserControl or PropertyPage is named by its <em>module's</em> file, because that is what it is to
    /// the IDE: one file, one identity. The two halves' paths diverge today (#474), and the module's is the
    /// one the project file names and the one the save event carries.
    /// </para>
    /// <para>
    /// <b>A saved document whose file is then deleted keeps its <c>file:</c> name</b>, following the
    /// protocol maintainers' own guidance: <c>untitled:</c> means "no file yet", not "file missing". That
    /// falls out of reading <see cref="DocumentIdentity.AbsolutePath"/> rather than probing the filesystem,
    /// and is the reason not to probe it.
    /// </para>
    /// </remarks>
    public static string For(DocumentIdentity document) =>
        document.AbsolutePath is { } path
            ? LspDocumentUri.ForFile(path)
            : LspDocumentUri.ForUntitled(document.Project.Name, document.Name, document.Extension);

    /// <inheritdoc cref="For(DocumentIdentity)"/>
    public static string For(FormDefinition form) => For(DocumentIdentity.For(form));

    /// <inheritdoc cref="For(DocumentIdentity)"/>
    public static string For(ModuleDefinition module) => For(DocumentIdentity.For(module));
}
