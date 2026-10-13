using HexIDE.Lsp;

namespace HexIDE.IDE;

/// <summary>
/// What an automation caller named: a form, module or class, or a carried file; or why the name found nothing
/// it could use.
/// </summary>
/// <param name="Document">The form, module or class, or null.</param>
/// <param name="Carried">The carried file, or null. Only a lookup that accepts carried files fills it.</param>
/// <param name="Error">The refusal, written for the caller, or null when something was found.</param>
/// <param name="Missing">
/// True when the name answered to no form, module or class at all, as opposed to answering to more than one, or
/// naming a project that is not loaded. Only a miss of that kind may go on to be looked for among carried files.
/// </param>
public readonly record struct NamedDocument(
    DocumentIdentity? Document, RelatedDocumentDefinition? Carried, string? Error, bool Missing = false)
{
    /// <summary>The project holding what was found, by its own name, or null when nothing was.</summary>
    public string? Project => Document?.Project.Name ?? Carried?.Owner.Name;

    /// <summary>
    /// What was found, by its own name: as the IDE spells it, not as the caller did. Null when nothing was.
    /// </summary>
    public string? Name => Document?.Name ?? Carried?.Name;
}

/// <summary>
/// A form, UserControl or PropertyPage to show or change in the designer, or why the name cannot be one.
/// </summary>
/// <param name="Form">The designer half: the form itself, or a UserControl's or PropertyPage's form part.</param>
/// <param name="Document">
/// What the reply names: the form, or the UserControl's or PropertyPage's MODULE, which is what the document is
/// to the IDE (one file, one identity).
/// </param>
/// <param name="Error">The refusal, written for the caller, or null when a designer was found.</param>
public readonly record struct NamedDesigner(FormDefinition? Form, DocumentIdentity? Document, string? Error);

/// <summary>
/// How an automation tool finds the document a caller named, and the refusal it gives when it cannot
/// (hexide-io/HexIDE#273, the hexide-mcp-server delta).
/// </summary>
/// <remarks>
/// <para>
/// <b>One rule for every tool that takes a document's name.</b> The name is matched as VB6 matches names,
/// without regard to case, in every loaded project, and resolves to the document before anything is keyed or
/// changed, so what a reply names is the document's own spelling rather than the caller's. A name that
/// answers to two documents, which a group of projects makes ordinary, is refused with both listed rather than
/// answered with whichever was found first, and <c>project</c> says which.
/// </para>
/// <para>
/// The tools used to apply this unevenly. The mark tools minted a store key out of the caller's spelling
/// (#467); five tools saw only the startup project (#575); and <c>set_control_property</c>,
/// <c>move_control</c> and <c>add_control</c> were still doing one or the other until this class took the rule
/// over. The search itself is <see cref="DocumentLookup.Find"/>; what lives here is what a tool replies.
/// </para>
/// <para>
/// The automation tools are compiled into an executable nothing else references, so the rule lives here, where
/// a test can reach it, as <see cref="MarkLineRules"/> and <see cref="DocumentContent"/> do. Its refusals are
/// English by design: they are automation replies, not IDE chrome.
/// </para>
/// </remarks>
public static class NamedDocuments
{
    /// <summary>
    /// The form, module or class a caller named, in any loaded project, or the reason it could not be named.
    /// </summary>
    /// <param name="loaded">Every loaded project.</param>
    /// <param name="name">The document's VB6 name. Matched without regard to case, as VB6 does.</param>
    /// <param name="project">The project to look in, by name and without regard to case; null searches them all.</param>
    /// <param name="sought">What the refusal says was looked for, such as "form or module".</param>
    public static NamedDocument Resolve(
        IReadOnlyCollection<ProjectDefinition> loaded, string name, string? project, string sought = "form or module")
    {
        ArgumentNullException.ThrowIfNull(loaded);
        if (loaded.Count == 0)
            return new NamedDocument(null, null, "No project loaded");

        if (project is not null
            && !loaded.Any(p => string.Equals(p.Name, project, StringComparison.OrdinalIgnoreCase)))
            return new NamedDocument(null, null,
                $"No loaded project named '{project}'. Loaded: {string.Join(", ", loaded.Select(p => p.Name))}");

        var found = DocumentLookup.Find(loaded, name, project);

        return found.Count switch
        {
            1 => new NamedDocument(found[0], null, null),
            0 => new NamedDocument(null, null, (project is null
                ? $"No {sought} named '{name}' in any loaded project"
                : $"No {sought} named '{name}' in project '{project}'")
                + (CarriedNamed(loaded, name, project).Count > 0 ? $": '{name}' is a carried file. " : ". ")
                + Inventory(loaded, project), Missing: true),
            _ => new NamedDocument(null, null,
                $"'{name}' names more than one document: {string.Join(", ", found.Select(d => d.Display))}. "
                + "Pass `project` to say which."),
        };
    }

    /// <summary>
    /// A name as the document tools resolve it: a form, module or class in any loaded project, else, where the
    /// tool takes one, a carried file. A refusal says what the name is when it is something else, and what the
    /// projects do hold, so a near-miss does not cost a second call to diagnose (#573).
    /// </summary>
    /// <remarks>
    /// These tools used to look only in the startup project, while the mark tools searched every loaded one:
    /// with a group open, a document in the second project could be given a breakpoint but not opened or read,
    /// and the refusal said it did not exist (#575).
    /// </remarks>
    /// <param name="carried">Whether a carried file is an answer this tool can use.</param>
    public static NamedDocument Find(
        IReadOnlyCollection<ProjectDefinition> loaded, string name, string? project, bool carried)
    {
        var lookup = Resolve(loaded, name, project, carried ? "form, module or carried file" : "form or module");
        if (lookup.Document is not null || !lookup.Missing || !carried)
            return lookup;

        var files = CarriedNamed(loaded, name, project);
        return files.Count switch
        {
            1 => new NamedDocument(null, files[0], null),
            0 => lookup,
            _ => new NamedDocument(null, null, $"'{name}' names more than one carried file: "
                + $"{string.Join(", ", files.Select(d => $"{d.Owner.Name}/{d.Name}"))}. Pass `project` to say which."),
        };
    }

    /// <summary>
    /// A form, UserControl or property page to show or change in the designer. A module or a carried file is
    /// refused by what it is and where it opens instead: "No form named 'Module1'" read as a wrong name, when
    /// the name was right and the tool was not.
    /// </summary>
    public static NamedDesigner FindDesigner(IReadOnlyCollection<ProjectDefinition> loaded, string name, string? project)
    {
        var found = Find(loaded, name, project, carried: true);
        if (found.Document is { } document)
            return (document.Form ?? document.Module?.FormPart) is { } designer
                ? new NamedDesigner(designer, document, null)
                : new NamedDesigner(null, null, $"'{document.Name}' is a module, which has no designer. open_file opens its code.");
        if (found.Carried is { } file)
            return new NamedDesigner(null, null, $"'{file.Name}' is a carried file, which has no designer. open_file opens it.");
        return new NamedDesigner(null, null, found.Error);
    }

    /// <summary>
    /// What a language server's name for a document is to the IDE: the loaded form, module, class or carried
    /// file it names, or nothing when it names none of them.
    /// </summary>
    /// <param name="loaded">Every loaded project.</param>
    /// <param name="uri">The name the server used, as a URI.</param>
    /// <remarks>
    /// <para>
    /// For a reply that carries a server's name for a document beside the IDE's own, so a caller can tell which
    /// document a diagnostic is about without knowing how the IDE spells a document to a server. A form,
    /// module or class is named by <see cref="DocumentWireName.For(DocumentIdentity)"/>; a carried file has no
    /// identity, and is named by its file.
    /// </para>
    /// <para>
    /// Compared with <see cref="LspDocumentUri.AreSame"/>, as <c>EditorService</c> compares a server's reply,
    /// because a server need not echo a name byte for byte. A name that answers to nothing loaded, such as a
    /// file a server analysed on its own account or a document since removed, finds nothing here, and that is
    /// an answer rather than a failure.
    /// </para>
    /// </remarks>
    public static NamedDocument ForUri(IEnumerable<ProjectDefinition> loaded, string uri)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        if (string.IsNullOrWhiteSpace(uri))
            return new NamedDocument(null, null, null, Missing: true);

        foreach (var project in loaded)
        {
            foreach (var document in DocumentLookup.DocumentsOf(project))
                if (Same(uri, () => DocumentWireName.For(document)))
                    return new NamedDocument(document, null, null);

            foreach (var file in project.RelatedDocuments)
                if (file.AbsolutePath is { } path && Same(uri, () => LspDocumentUri.ForFile(path)))
                    return new NamedDocument(null, file, null);
        }

        return new NamedDocument(null, null, null, Missing: true);

        // A path that cannot be turned into a URI names nothing, and is not an error either: it is a project
        // entry naming something this filesystem cannot express.
        static bool Same(string uri, Func<string> name)
        {
            try { return LspDocumentUri.AreSame(uri, name()); }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
        }
    }

    /// <summary>
    /// Carried files by name, and only when none has that name, by filename. The two differ for a file VB6
    /// carried on a code line: <c>Module=Notes; Notes.md</c> is named Notes (#548). AbsolutePath is a host
    /// path, resolved against the filesystem, so System.IO.Path is the right tool for it.
    /// </summary>
    private static List<RelatedDocumentDefinition> CarriedNamed(
        IReadOnlyCollection<ProjectDefinition> loaded, string name, string? project)
    {
        var all = loaded
            .Where(p => project is null || string.Equals(p.Name, project, StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => p.RelatedDocuments)
            .ToList();
        var byName = all.Where(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        return byName.Count > 0
            ? byName
            : all.Where(d => d.AbsolutePath is { } path
                             && string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>What the loaded projects hold, for a refusal: documents with a designer, code modules, carried files.</summary>
    private static string Inventory(IReadOnlyCollection<ProjectDefinition> loaded, string? project)
    {
        var lines = loaded
            .Where(p => project is null || string.Equals(p.Name, project, StringComparison.OrdinalIgnoreCase))
            .Select(p =>
            {
                var documents = DocumentLookup.DocumentsOf(p).ToList();
                return $"{p.Name} holds "
                       + Listed("forms and UserControls", documents.Where(d => d.Form is not null || d.Module?.FormPart is not null).Select(d => d.Name))
                       + "; " + Listed("modules", documents.Where(d => d.Form is null && d.Module?.FormPart is null).Select(d => d.Name))
                       + "; " + Listed("carried files", p.RelatedDocuments.Select(d => d.Name))
                       + ".";
            });
        return string.Join(" ", lines);

        static string Listed(string what, IEnumerable<string> names)
        {
            var list = names.ToList();
            return list.Count == 0 ? $"no {what}" : $"{what} {string.Join(", ", list)}";
        }
    }
}
