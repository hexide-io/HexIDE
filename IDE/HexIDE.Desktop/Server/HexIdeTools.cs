using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;
using HexIDE.Conversations;
using HexIDE.Lsp;
using HexIDE.Redaction;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.IDE;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using ModelContextProtocol.Server;
using Microsoft.AspNetCore.Http;

namespace HexIDE.Desktop.Server;

[McpServerToolType]
internal sealed class HexIdeTools(IdeContext ctx, IHttpContextAccessor? httpContextAccessor = null)
{
    [McpServerTool(Name = "get_project_info")]
    [Description("Returns the startup project's name, path, and lists of forms, modules and carried files (RelatedDoc entries), and in 'projects' the same for every loaded project, each with 'isStartup'. With a project group open the top-level fields describe only the startup project, so read 'projects' for the rest; 'note' says when the top-level fields are not the whole picture, including when nothing is loaded or no startup project is set.")]
    public async Task<ProjectInfoResult> GetProjectInfoAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Every loaded project, because this is the tool every other description sends a caller to for
            // names, and with a group open it used to list only the startup project's. With no startup
            // project set it answered three empty lists and nothing else, which cannot be told apart from a
            // project with nothing in it. (#581)
            var startup = ctx.ProjectManager.StartupProject;
            var loaded = ctx.ProjectManager.LoadedProjects;
            var projects = loaded.Select(p => new ProjectSummary(
                    p.Name,
                    p.AbsolutePath,
                    ReferenceEquals(p, startup),
                    p.Forms.Select(f => f.Name).ToArray(),
                    p.Modules.Select(m => m.Name).ToArray(),
                    p.RelatedDocuments.Select(d => d.Name).ToArray()))
                .ToArray();

            var note = loaded.Count switch
            {
                0 => "No project is loaded.",
                _ when startup is null =>
                    $"{loaded.Count} project{(loaded.Count == 1 ? " is" : "s are")} loaded but none is the startup project, "
                    + "so the top-level fields are empty. 'projects' lists what is loaded.",
                1 => null,
                _ => $"{loaded.Count} projects are loaded. The top-level fields describe the startup project, "
                     + $"{startup.Name}; 'projects' lists all of them.",
            };

            return startup is null
                ? new ProjectInfoResult(null, null, [], [], [], projects, note)
                : new ProjectInfoResult(
                    startup.Name,
                    startup.AbsolutePath,
                    startup.Forms.Select(f => f.Name).ToArray(),
                    startup.Modules.Select(m => m.Name).ToArray(),
                    startup.RelatedDocuments.Select(d => d.Name).ToArray(),
                    projects,
                    note);
        });
    }

    [McpServerTool(Name = "get_open_editors")]
    [Description("Returns the open EDITOR windows — code windows and designers — and which of them is active. It "
               + "does not list tool documents (the Object Browser, the connection list, the protocol inspector), "
               + "although they are tabs in the same strip, and 'activeWindow' is null while one of those is in "
               + "front, because no editor is then active. get_document_tabs answers the other question: every tab, "
               + "with its type and which one is active. The 'note' says which case an empty or null answer is. 'editors' and 'activeEditor' give title, type, project, document and isModified (whether it has unsaved changes); openWindows and activeWindow retain display titles.")]
    public async Task<OpenEditorsResult> GetOpenEditorsAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var docs = ctx.DocumentDockService.OpenDocuments;
            var windows = docs.Select(d => d.Title).ToArray();
            var active = ctx.DocumentDockService.ActiveDocument?.Title;

            // A null activeWindow means "no EDITOR is active", which is not the same as "no tab is active", and
            // nothing said so: a caller comparing this with get_document_tabs saw a tab there and nothing here. (#684)
            var front = ctx.DocumentDockService.ActiveTab;
            var note = windows.Length == 0
                ? "No editor is open" + (front is not null
                    ? $"; the tab in front is '{front.Title}', which is not an editor."
                    : " and no tab is in front.")
                : active is not null
                    ? null
                    : front is not null
                        ? $"No editor is active: the tab in front is '{front.Title}', which is not an editor. "
                          + "get_document_tabs lists it."
                        : "No editor is active, and no tab is in front.";
            return new OpenEditorsResult(windows, active, note,
                docs.Select(TabInfo).ToArray(),
                ctx.DocumentDockService.ActiveDocument is { } editor ? TabInfo(editor) : null);
        });
    }

    [McpServerTool(Name = "get_diagnostics")]
    [DescribesEnum(typeof(HexIDE.Lsp.Messages.DiagnosticSeverity))]
    [Description("Returns current diagnostics, from every attached language server and from the VB6 "
               + "compiler, merged. Each carries 'source' (which of them reported it) and, where the "
               + "server sent one, 'code' (the rule that fired) and 'href' (where that rule is documented). "
               + "'uri' is the name the language server knows the document by: its file: URI, or "
               + "untitled:<project>/<name>.<ext> while it has no file. 'project' and 'document' name it as the IDE "
               + "does, and are absent when the uri names no loaded form, module, class or carried file, or a file two loaded "
               + "projects share, since nothing then says which project's copy it is about. 'severity' is Error, Warning, "
               + "Information or Hint, as the server reported it, or Unknown when the server sent none. "
               + "'line' and 'column' are 1-based, and the line is counted from the top of the file with its header "
               + "included, as the code window numbers it; a VB6 compiler error is converted to that numbering. "
               + "Only OPEN documents are analysed, by a server that starts on the first one of its language. "
               + "'analysed' lists, by uri, the documents whose diagnostics are in, clean ones included, and 'note' says "
               + "why the list is empty or what has not been analysed yet: no server started, or a document "
               + "opened or edited and still being analysed.")]
    public async Task<DiagnosticsResult> GetDiagnosticsAsync(CancellationToken ct = default)
    {
        var published = ctx.Diagnostics.GetAll();

        // The IDE's name for each document beside the server's, read on the UI thread, where the model lives.
        // A caller asking about Form1 should not have to know how the IDE spells Form1 to a server (#273).
        var named = await Dispatcher.UIThread.InvokeAsync(() => published
            .Select(p => p.Uri)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(uri => uri, uri => NamedDocuments.ForUri(ctx.ProjectManager.LoadedProjects, uri),
                StringComparer.Ordinal));

        var items = published
            .SelectMany(p => p.Diagnostics.Select(d => new DiagnosticItem(
                p.Uri,
                d.Message,
                d.Severity?.ToString() ?? "Unknown",
                d.Range.Start.Line + 1,
                d.Range.Start.Character + 1,
                d.CodeText,
                d.Source,
                d.CodeDescription?.Href,
                named[p.Uri].Project,
                named[p.Uri].Name)))
            .ToArray();
        var analysed = published.Select(p => p.Uri).Order(StringComparer.Ordinal).ToArray();

        // An empty list meant clean code, no server started, or an analysis not yet back, and read the same
        // for all three; an edit checked with set_file_content then get_diagnostics came back clean. (#664)
        // An open document with nothing yet, or edited since its latest diagnostics: what is held for it
        // describes older text, so a clean reply for it is not an answer.
        var pending = await Dispatcher.UIThread.InvokeAsync(() => ctx.DocumentDockService.OpenDocuments
            .OfType<CodeEditorViewModel>()
            .Where(e => e.AwaitingDiagnostics)
            .Select(e => DocumentWireName.For(e.Identity))
            .Distinct(LspDocumentUri.Comparer)
            .Order(StringComparer.Ordinal)
            .ToArray());
        var serverStarted = ctx.Capture.ConnectionIds.Any();

        string? note;
        if (!serverStarted)
            note = "No language server has started. One starts on the first open document of a language it "
                   + "claims, and only open documents are analysed: open_file a form or module, then ask again."
                   + (items.Length > 0 ? " The diagnostics listed came from the VB6 compiler." : "");
        else if (pending.Length > 0)
            note = $"Not analysed yet in its current form: {string.Join(", ", pending)}. No diagnostics have "
                   + $"arrived for {(pending.Length == 1 ? "it" : "them")} since {(pending.Length == 1 ? "it was" : "they were")} "
                   + "opened or last edited, so any listed describe older text, most likely because the analysis is "
                   + "still running; ask again in a moment. list_lsp_messages shows whether anything came back.";
        else if (items.Length > 0)
            note = null;
        else if (analysed.Length > 0)
            note = $"No diagnostics: {(analysed.Length == 1 ? "the analysed document is" : $"all {analysed.Length} analysed documents are")} "
                   + "clean. Only open documents are analysed.";
        else
            note = "Nothing has been analysed: no document is open, and only open documents are. open_file a form "
                   + "or module, then ask again.";

        return new DiagnosticsResult(items, analysed, note);
    }

    [McpServerTool(Name = "get_file_content")]
    [Description("Returns the current text of a named form, module or carried file: the open editor's text if it is open, otherwise the IDE's copy (for a carried file, the file on disk). For a form, module or class that is its WHOLE FILE, open or not: a form's VERSION line and designer block, a class's header and the leading Attribute lines come first, then the code. So line 1 is the file's first line, as set_breakpoints, set_bookmarks, get_diagnostics and the debugger tools count it, and an editor offset (interact set_property CaretOffset, or the offset a type_text or press_key refusal gives) indexes this text. set_file_content takes it back as it is, or the code alone. A form created in this session and not yet saved or changed in the designer has no header yet, so its text is its code alone. Searches every loaded project, without regard to case; pass `project` when a group holds two documents of one name, and without it an ambiguous name is refused and the reply lists them. The reply's 'project' and 'document' name what was read, as the IDE spells them. hasUnsavedChanges is true when saving would change the file on disk — edits typed in the editor, controls added, removed or reordered in the designer, or any other change to the IDE's copy since it was last loaded or saved. A document with no file yet always has unsaved changes.")]
    public async Task<FileContentResult> GetFileContentAsync(string name, string? project = null, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // COMPUTED, where it used to report only which source the text came from: true for any open
            // editor, false for everything else, so a caller deciding what to save saved every open document
            // and skipped every edited closed one. (#481) Asked without flushing anything, because a read
            // must not change the model it reports on -- so the three places an edit can live are each
            // compared directly: the editor buffer against the model, the designer's control list against
            // the model's, and the model against what was last loaded or saved.
            var found = Find(name, project, carried: true);
            if (found.Document is { } document)
            {
                // The WHOLE file, header included, open or not (#273 task 3.18): the text every line number
                // and every editor offset counts in, and the text set_file_content takes back. The dirtiness
                // below is a different question with a different answer, and the two are deliberately read
                // from different things: the editor's code section against the model's Code. The whole buffer
                // never equals Code, so comparing what this returns would call every open document edited.
                var editor = CodeEditorOf(document);
                var text = HexIDE.IDE.DocumentContent.Read(document, editor);
                var codeAhead = HexIDE.IDE.DocumentContent.CodeAheadOfModel(document, editor);
                if (document.Form is { } form)
                {
                    var unsaved = codeAhead
                                  || DesignerOf(form) is { HasComponentsNotInModel: true }
                                  || ctx.ProjectService.HasUnsavedChanges(form);
                    return new FileContentResult(text, unsaved, null, document.Project.Name, document.Name);
                }

                var module = document.Module!;
                var moduleUnsaved = codeAhead
                                    || (module.FormPart is { } part && DesignerOf(part) is { HasComponentsNotInModel: true })
                                    || ctx.ProjectService.HasUnsavedChanges(module);
                return new FileContentResult(text, moduleUnsaved, null, document.Project.Name, document.Name);
            }

            // A carried file is read as its editor holds it, or else as it is on disk. It used to be refused
            // as a name that did not exist, straight after open_file had opened it. (#572)
            if (found.Carried is { } carried)
            {
                var (text, error) = CarriedTextOf(carried);
                return new FileContentResult(text, CarriedEditorOf(carried)?.IsDirty ?? false, error, carried.Owner.Name, carried.Name);
            }

            return new FileContentResult(null, false, found.Error);
        });
    }

    private HexIDE.VisualDesigner.FormEditViewModel? DesignerOf(FormDefinition form) =>
        ctx.DocumentDockService.OpenDocuments
            .OfType<HexIDE.VisualDesigner.FormEditViewModel>()
            .FirstOrDefault(d => d.FormDefinition == form);

    [McpServerTool(Name = "set_file_content")]
    [Description("Replaces the VB6 source of the form, module or class 'name' and saves to disk; `project` picks one where a group holds two of that name. The reply's 'project' and 'document' name what was written, as the IDE spells them. Two parts of the file are the IDE's, and content that changes either is refused with nothing changed. The header (a form's or UserControl's VERSION line and designer block, a class's header, the leading Attribute lines) changes from the designer and the Properties window, so a changed control or Instancing is not applied by sending it here. A member's Attribute lines, the run under a procedure's or variable's declaration that gives its description or dispatch id, may move or be renamed with it and go with its declaration; the reply names each member whose lines are otherwise changed, added or dropped. A form's leading 'Attribute VB_*' lines are its identity: if content leaves them out the existing ones are kept and the reply says so. A document with no file yet saves through a native picker, which would stop this server answering, so it is refused before anything changes unless answer_next_file_dialog has been armed first. The reply's 'note' names the file written and the range the document's lines are now numbered in, from the top of the file with the header included, as get_file_content returns it and set_breakpoints counts, and says whether the document is open in an editor, since only open documents are analysed for get_diagnostics. The save writes the whole file, so text set earlier and not yet saved, such as a form's captions, counts too: when the document's code page cannot hold a character anywhere in it, nothing is written and no dialog is shown, the code stays in the IDE, and the reply names the document, the code page, the character and the line that holds it.")]
    public async Task<MutateResult> SetFileContentAsync(
        [Description("A form, module or class, as get_project_info lists them, found in every loaded project without "
                     + "regard to case. A name two documents share is refused, and the reply lists them.")]
        string name,
        [Description("What get_file_content returns, edited: the whole file with its header and every member's Attribute lines as the document has them. The code alone is accepted too, and the document's own header is kept in front of it.")]
        string content,
        [Description("The project the document is in, as get_project_info names it, for a group that holds two "
                     + "documents of one name; omit it otherwise.")]
        string? project = null,
        CancellationToken ct = default)
    {
        var restoredHeader = false;
        DocumentIdentity? target = null;
        NamedDocument found = default;
        var (form, module, error) = await Dispatcher.UIThread.InvokeAsync<(FormDefinition?, ModuleDefinition?, string?)>(() =>
        {
            // Kept for the reply, which names whatever the name reached, a carried file included: with a carried
            // Notes in two projects, the refusal below has to say which one it found.
            found = Find(name, project, carried: true);
            if (found.Carried is { } carried)
                return (null, null,
                    $"'{carried.Name}' is a carried file, and set_file_content writes only forms and modules. "
                    + "open_file opens it for editing in the IDE.");
            if (found.Document is not { } document)
                return (null, null, found.Error);
            target = document;

            // Refused BEFORE the edit. This used to apply the code and only then find there was no file to
            // write, so the reply said the change failed while the editor held it. (#538)
            if (document.Form is { } pickerForm && HexIDE.IDE.ScriptedFileDialogs.WouldShowPicker(pickerForm.AbsolutePath))
                return (null, null, $"Form '{pickerForm.Name}' {HexIDE.IDE.ScriptedFileDialogs.PickerRefusal}");
            if (document.Module is { } pickerModule && HexIDE.IDE.ScriptedFileDialogs.WouldShowPicker(pickerModule.AbsolutePath))
                return (null, null, $"Module '{pickerModule.Name}' {HexIDE.IDE.ScriptedFileDialogs.PickerRefusal}");

            // Through the guarded path (#273 tasks 3.9, 3.18 and 4.5b), which is the one place that decides what
            // incoming content may touch, for every kind alike. It accepts what get_file_content returns, the
            // whole file with its header unchanged, and the code alone with the header kept. A header that
            // DIFFERS is refused, because the header changes only from the IDE's model, and so is a change to a
            // member's attribute lines other than moving or renaming them with it or deleting them with its
            // declaration, with each member named.
            //
            // A whole .frm used to be refused here before the guard saw it (#338), on the ground that its
            // designer block describes controls this tool does not apply. The header comparison answers that
            // better: a designer block exactly as the form has it applies nothing, and one that differs is
            // refused with the reason. The separate refusal had to go in the same change as the read, or this
            // tool would have refused exactly what get_file_content now returns.
            var replaced = HexIDE.IDE.DocumentContent.Write(document, CodeEditorOf(document), content);
            if (replaced.Refusal is { } refusal)
                return (null, null, refusal);

            // Whether the CALLER left a form's Attribute block out, not whether anything was put back: the
            // designer block is always put back in front of code sent alone. The block is the form's identity,
            // and leaving it out used to delete it with no warning, straight to disk (gap 14).
            restoredHeader = HexIDE.IDE.DocumentContent.LeftOutAttributeLines(document, content);
            return (document.Form, document.Module, null);
        });

        if (error is not null)
            return Naming(new MutateResult(false, error), found);

        // Before the save, which may take an armed cancel: that is the route the refusal above offers, and its
        // outcome is the document kept without a file, not a failure (#755).
        var cancel = HexIDE.IDE.ScriptedFileDialogs.WatchForCancel(form is not null ? form.AbsolutePath : module?.AbsolutePath);
        try
        {
            // The result is honoured rather than assumed. A refusal used to come back as
            // MutateResult(true, null) — success for a file that was not written — and an agent, unlike a
            // developer, has no dialog to read and will build on that answer. (#147)
            // ON THE UI THREAD, and that is the whole of #334 rather than a tidiness point. A save first
            // publishes ApplyAllUnsavedChangesEvent to flush open editor buffers into the model; the
            // handler reads AvaloniaEdit's Document.Text, which throws "Call from invalid thread" off it.
            // EventBus logs that and carries on, so the flush silently does nothing and the PREVIOUS text
            // is serialized -- a file with a fresh timestamp, stale contents, and a success returned to a
            // caller who cannot see the log. Awaiting the save here left this method on a pool thread.
            var rewrites = new HexIDE.Projects.SaveRewriteReport();
            var (written, refused) = await SaveQuietlyAsync(() => form is not null
                ? ctx.ProjectService.SaveForm(form, false, interactive: false, rewrites: rewrites)
                : module is not null ? ctx.ProjectService.SaveModule(module, false, interactive: false, rewrites: rewrites) : Task.FromResult(false));
            if (refused is not null)
                // Not "the new code was refused": the character can be anywhere in the file. The message quotes its line.
                return Naming(new MutateResult(false, cancel.After($"The new code is in the IDE, but the file was not written. {refused.Message}")), target);
            return Naming(written
                ? new MutateResult(true, null, cancel.After(await Dispatcher.UIThread.InvokeAsync(() => WrittenNote(target!)) + rewrites.Note
                    + (restoredHeader
                        ? " Kept the form's Attribute header, which the content omitted. VB_Name is the form's "
                          + "identity; without this the write would have destroyed it. Call get_file_content "
                          + "first and edit what it returns, which includes the header."
                        : "")))
                : new MutateResult(false, cancel.After("HexIDE cannot reproduce this file faithfully, so it was not "
                                        + "written and the copy on disk is unchanged.")), target);
        }
        catch (OperationCanceledException) when (cancel.Cancelled || cancel.Refused)
        {
            var kind = form is not null ? "Form" : "Module";
            var kept = $"The new code is in the IDE. {kind} '{form?.Name ?? module!.Name}' still has no file: "
                       + $"{cancel.HowAnswered}, so nothing was written. To write it, arm answer_next_file_dialog with a "
                       + "path and call set_file_content again.";
            // An armed cancel was asked for; several paths for a save were not, so that one did not do what was asked.
            return Naming(cancel.Refused ? new MutateResult(false, cancel.After(kept)) : new MutateResult(true, null, kept), target);
        }
        catch (Exception ex)
        {
            // The edit is already in the IDE by now; a bare exception message would read as though it were not.
            return Naming(new MutateResult(false, cancel.After($"The new code is in the IDE but was not written: {ToolFailures.WithoutProfile(ex.Message)}")), target);
        }
    }

    /// <summary>What set_file_content wrote, and whether get_diagnostics will see it. (#670)</summary>
    /// <remarks>
    /// <para>
    /// The range is the one set_breakpoints accepts and its refusals state, counted from the top of the file
    /// with the header included, so a caller can take it straight there. MarkLineRules counts it the same way.
    /// </para>
    /// <para>
    /// Read AFTER the save, and the order matters. A form's save re-renders its designer block and adopts the
    /// result as the header: a form that had no header yet gains one there, and one VB6 wrote can change
    /// height. A range taken before the save would disagree with the next get_file_content in exactly the case
    /// a caller cannot predict. The path is read afterwards for the same reason: a first save through an armed
    /// file dialog is where a document gets one.
    /// </para>
    /// </remarks>
    private string WrittenNote(DocumentIdentity document)
    {
        var editor = CodeEditorOf(document);
        var lines = HexIDE.IDE.DocumentContent.TextOf(document, editor).LineCount;
        return $"Wrote {document.Name} to {document.AbsolutePath}. Its lines are numbered 1..{lines}, from the top of "
               + "the file with the header included, as get_file_content returns it. "
               + (editor is not null
                   ? "Its editor shows the new text; get_diagnostics has diagnostics for it once the server has "
                     + "analysed the change."
                   : "It is not open in an editor, and only open documents are analysed: open_file it, then "
                     + "get_diagnostics.");
    }

    /// <summary>
    /// Runs a save that shows no dialog, and hands back an encoding refusal as a value. A dialog would hold
    /// the reply until a person dismissed it, and the refusal's message, which names the document, the code
    /// page and the character, is the reply.
    /// </summary>
    /// <remarks>
    /// Caught on the UI thread rather than after the hop back: the refusal derives from
    /// <see cref="OperationCanceledException"/>, and an exception of that kind need not leave
    /// <c>Dispatcher.InvokeAsync</c> as itself.
    /// </remarks>
    private static Task<(bool Written, SourceEncodingRefusal? Refused)> SaveQuietlyAsync(Func<Task<bool>> save) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                return (await save(), (SourceEncodingRefusal?)null);
            }
            catch (SourceEncodingRefusal refusal)
            {
                return (false, refusal);
            }
        });

    private static readonly HashSet<string> KnownFileTypes = ["form", "module", "classmodule", "usercontrol", "propertypage"];

    [McpServerTool(Name = "add_file")]
    [Description("Adds a new form or module to the startup project, or to the loaded project `project` names (without regard to case), with 'project' and 'document' naming what was added. Nothing is written to disk, as in VB6: the new document has no file, and 'path' is null, until the project is saved. Save Project then asks where to put each new form and module, and the project file if it has none, in that order (forms, then modules, then the project file); a caller answers each picker with answer_next_file_dialog, armed beforehand. type must be 'Form', 'Module', 'ClassModule', 'UserControl', or 'PropertyPage'. name must be a VB6 name (a letter, then letters, digits and underscores) not already used by a form, module or class in the project; anything else is refused, as is a name the new file's code page, or the project file's, cannot hold.")]
    public async Task<AddFileResult> AddFileAsync(string name, string type, string? project = null, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // The startup project unless the caller names another, which with a group open is the only way to add
            // to the rest of it. The reply names the project either way, so where the file went is never a guess.
            var loaded = ctx.ProjectManager.LoadedProjects;
            var into = project is null
                ? ctx.ProjectManager.StartupProject
                : loaded.FirstOrDefault(p => string.Equals(p.Name, project, StringComparison.OrdinalIgnoreCase));
            if (into is null)
                return new AddFileResult(false, null, loaded.Count == 0
                    ? "No project loaded"
                    : project is not null
                        ? $"No loaded project named '{project}'. Loaded: {string.Join(", ", loaded.Select(p => p.Name))}"
                        : $"No startup project is set. Pass `project` to say which to add to: {string.Join(", ", loaded.Select(p => p.Name))}");

            if (!KnownFileTypes.Contains(type.ToLowerInvariant()))
                return new AddFileResult(false, null, $"Unknown type '{type}' — must be 'Form', 'Module', 'ClassModule', 'UserControl', or 'PropertyPage'");

            // The rules every other way of naming a document already applies. This tool was the one route to a
            // document VB6 cannot load, such as "My Module" (#596).
            if (!ProjectNaming.IsValidName(name))
                return new AddFileResult(false, null,
                    $"'{name}' is not a valid name. A name starts with a letter and continues with letters, digits and underscores. Nothing was added.");
            if (ProjectNaming.IsNameTaken(into, name))
                return new AddFileResult(false, null,
                    $"'{name}' is already the name of a form, module or class in {into.Name}. VB6 gives them one namespace, so two cannot share a name. Nothing was added.");

            // A null path reads as "something went wrong" unless the reply says it is the expected state: a new
            // document has no file until the project is saved, as in VB6 (#500), and the save asks where.
            string Note(DocumentIdentity added) =>
                $"{added.Name} has no file yet, as in VB6: Save Project writes it, asking where with a file dialog "
                + "(answer it with answer_next_file_dialog, armed first), and the project file lists it from then.";

            try
            {
                var added = type.ToLowerInvariant() switch
                {
                    "form" => DocumentIdentity.For(await ctx.ProjectService.AddNewForm(into, name, interactive: false)),
                    "module" => DocumentIdentity.For(await ctx.ProjectService.AddNewModule(into, name, ModuleKind.StandardModule, interactive: false)),
                    "classmodule" => DocumentIdentity.For(await ctx.ProjectService.AddNewModule(into, name, ModuleKind.ClassModule, interactive: false)),
                    "usercontrol" => DocumentIdentity.For(await ctx.ProjectService.AddNewUserControl(into, name, interactive: false)),
                    "propertypage" => DocumentIdentity.For(await ctx.ProjectService.AddNewPropertyPage(into, name, interactive: false)),
                    _ => throw new System.Diagnostics.UnreachableException(),
                };
                return new AddFileResult(true, added.AbsolutePath, null, Note(added), added.Project.Name, added.Name);
            }
            // Caught here, on the UI thread, for the reason SaveQuietlyAsync gives.
            catch (SourceEncodingRefusal refusal)
            {
                return new AddFileResult(false, null, $"'{name}' was not added: {refusal.Message}");
            }
            catch (Exception ex)
            {
                return new AddFileResult(false, null, ToolFailures.WithoutProfile(ex.Message));
            }
        });
    }

    [McpServerTool(Name = "get_form_controls")]
    [Description("Returns all controls on a form or UserControl with their key design-time properties (name, type, position, size, caption, text, visible, enabled, and a control array element's index, which name(index) takes); with 'controlName', that one control with every property it has, in the text set_control_property takes where it takes one. Position and size in that summary are in PIXELS, left/top relative to the control's container; a .frm stores twips, 15 to a pixel. The 'properties' rows give Left, Top, Width and Height as the Properties window does instead, in twips or the container's ScaleMode, each naming its unit. Searches every loaded project, without regard to case; pass `project` when a group holds two documents of one name, and without it an ambiguous name is refused and the reply lists them. The reply's 'project' and 'document' name the form, as the IDE spells them; visible, enabled, caption and text are left out where the control's class has no such property.")]
    public async Task<FormControlsResult> GetFormControlsAsync(
        string formName,
        string? project = null,
        [Description("One control's name, or the form's own, without regard to case; a control array's element as "
                     + "name(index). The reply then lists that control alone, with 'properties' holding every property "
                     + "the Properties window shows for it, by the name there, with its value as text: an enum as "
                     + "\"1 - Opaque\", a colour as its literal, a number with '.', a font as \"Tahoma, 9.75 pt, bold\"; a "
                     + "picture as a summary. Left, Top, Width and Height are as VB6's Properties window shows them, "
                     + "not the summary's pixels: in the container's ScaleMode, twips on a form unless it says otherwise, "
                     + "twips inside a Frame, and a form's own Width and Height with its frame; the row's 'note' names the "
                     + "unit. 'settable' says whether set_control_property takes that text back; a "
                     + "picture and the like are set in the Properties window. Properties the file holds that HexIDE does not model, or "
                     + "could not read, are rows too, with the file's text, not settable, and a 'note' saying which. "
                     + "A control array named without an index is its first element, and the reply's 'note' says so.")]
        string? controlName = null,
        CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var named = FindDesigner(formName, project);
            if (named is not { Form: { } form, Document: { } document })
                return new FormControlsResult(named.Error, []);

            // When the form is open in the designer, read live VM state (FormDefinition.Components
            // is only synced on save via ApplyAllUnsavedChangesEvent and won't reflect unsaved additions).
            var designerVm = ctx.DocumentDockService.OpenDocuments
                .OfType<HexIDE.VisualDesigner.FormEditViewModel>()
                .FirstOrDefault(d => d.FormDefinition == form);
            var components = designerVm is not null
                ? (IEnumerable<ComponentInstance>)designerVm.AllComponents.Select(v => v.Instance).ToList()
                : form.Components;

            ControlInfo Info(ComponentInstance c)
            {
                var name    = c.GetPropertyOrDefault(VBProperties.NameProperty) ?? "";
                var left    = c.GetPropertyOrDefault(VBProperties.LeftProperty);
                var top     = c.GetPropertyOrDefault(VBProperties.TopProperty);
                var width   = c.GetPropertyOrDefault(VBProperties.WidthProperty);
                var height  = c.GetPropertyOrDefault(VBProperties.HeightProperty);
                bool? visible = c.BaseClass.PropertiesByName.ContainsKey("Visible")
                    ? c.GetPropertyOrDefault(VBProperties.VisibleProperty) : null;
                bool? enabled = c.BaseClass.PropertiesByName.ContainsKey("Enabled")
                    ? c.GetPropertyOrDefault(VBProperties.EnabledProperty) : null;

                string? caption = c.BaseClass.PropertiesByName.ContainsKey("Caption")
                    ? c.GetPropertyOrDefault(VBProperties.CaptionProperty) : null;
                string? text = c.BaseClass.PropertiesByName.ContainsKey("Text")
                    ? c.GetPropertyOrDefault(VBProperties.TextProperty) : null;

                var typeName = c.BaseClass is FormComponentClass
                    ? form.RootVBTypeName
                    : c.BaseClass.VBTypeName;

                var container = c.Container is { } parent && parent.BaseClass is not FormComponentClass
                    ? parent.GetPropertyOrDefault(VBProperties.NameProperty)
                    : null;

                return new ControlInfo(
                    name,
                    typeName,
                    left, top, width, height,
                    visible, enabled,
                    caption, text,
                    container,
                    PropertyText.ArrayIndexOf(c));
            }

            // One control, with every row the Properties window shows for it (#923): set_control_property sent
            // callers here for a control's properties, and only eight were listed.
            if (controlName is not null)
            {
                var all = components.ToList();
                var matches = PropertyText.Named(all, controlName);
                if (matches.Count == 0)
                    return new FormControlsResult(
                        $"No control named '{controlName}' on {document.Display}; its controls are "
                        + string.Join(", ", all.Select(c => c.GetPropertyOrDefault(VBProperties.NameProperty)).Distinct(StringComparer.OrdinalIgnoreCase)),
                        [], document.Project.Name, document.Name);
                var control = matches[0];
                // A control array's elements share the name; say so rather than answer for one of them silently.
                var note = matches.Count == 1 ? null
                    : $"'{controlName}' names a control array of {matches.Count}; this is its first, Index "
                      + $"{PropertyText.ArrayIndexOf(control)?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}. "
                      + $"Name one as {controlName}(n) for another.";
                return new FormControlsResult(null, [Info(control)], document.Project.Name, document.Name,
                    PropertyText.Rows(control, (property, pixels) => DesignPositions.IsPosition(property)
                        ? (DesignPositions.Shown(form, control, property, pixels), DesignPositions.Unit(form, control, property))
                        : null), note);
            }
            return new FormControlsResult(null, components.Select(Info).ToArray(), document.Project.Name, document.Name);
        });
    }

    [McpServerTool(Name = "set_control_property")]
    [Description("Sets a named property on a form or UserControl control and saves the file. The form is named in any loaded project, without regard to case; pass `project` when a group holds two documents of one name, and without it an ambiguous name is refused and the reply lists them. 'value' is written as the Properties window shows it; its own description gives the forms, and a refused value's reply says what that property takes. get_form_controls lists the controls, and with 'controlName' every property of one, its value as this tool takes it. A property is matched by the name the Properties window shows, without regard to case and to its parentheses, so 'Name' reaches '(Name)'. Setting Name renames the control under the rules the Properties window applies, whether or not the designer is open: not empty, and unique on the form; for the form itself also a valid VB6 name that no other form or module of the project has, because it renames the document. A rename the naming rules refuse changes nothing. The save writes the whole file, so text set earlier and not yet saved counts too: when the file's code page cannot hold a character anywhere in it, the value is set in the IDE but nothing is written and no dialog is shown, and the reply names the document, the code page, the character and the line that holds it. A document with no file yet saves through a native picker, which would stop this server answering, so it is refused before anything changes unless answer_next_file_dialog has been armed first. The reply's 'note' gives the value the property now holds, which can differ from the text sent, and 'project' and 'document' name the form as the IDE spells them.")]
    public async Task<MutateResult> SetControlPropertyAsync(
        string formName,
        [Description("The control: its name, or for an element of a control array name(index), such as lblTitle(1), "
                     + "with the 'index' get_form_controls lists for it. A bare array name is refused, naming the indexes, "
                     + "since the save would write to a guess.")]
        string controlName, string property,
        [Description("The value as the Properties window shows it: text, a number with '.' for decimals, True or False, "
                     + "a colour literal such as &H00C0FFC0&, or an enum by number, by name, or as '1 - Opaque'. A Font "
                     + "as get_form_controls shows it, such as 'Tahoma, 9.75 pt, bold, charset 0': a part not given is "
                     + "kept, but style words given are the whole style ('regular' clears it), and a new face without a "
                     + "charset takes the one VB6 gives it, 2 for Wingdings, Webdings, Symbol and Marlett, 255 for "
                     + "Terminal, Modern, Roman and Script, otherwise 0. A face whose name ends in a number or a style "
                     + "word, such as 'Wingdings 2', is a face when the control has it or it is installed here. A refused "
                     + "value's reply says why where it can, and lists what that property takes. Left, Top, Width and "
                     + "Height are in the unit the Properties window shows them in, which get_form_controls' row for "
                     + "each names: twips, or the container's ScaleMode. Not the pixels of get_form_controls' summary "
                     + "or of move_control.")]
        string value, string? project = null,
        CancellationToken ct = default)
    {
        // What the property holds after the set, which is not always what was sent: "0 - Transparent" stores 0. (#655)
        string? stored = null;
        DocumentIdentity? target = null;
        var (form, ownerModule, error) = await Dispatcher.UIThread.InvokeAsync<(FormDefinition?, ModuleDefinition?, string?)>(() =>
        {
            // In every loaded project, as every other tool that takes a document's name looks (#273). This one
            // looked in the startup project only, so a form in a group's other project was "not found", and the
            // replies quoted the caller's spelling of the form rather than the form's.
            var named = FindDesigner(formName, project);
            if (named is not { Form: { } form, Document: { } document })
                return (null, null, named.Error);
            target = document;

            // A UserControl's or PropertyPage's identity is its module, which holds the file the save writes.
            var ownerModule = document.Module;

            // When the form is open in the designer, use live VM state (form.Components is only
            // synced on save and won't reflect unsaved additions from add_control).
            var designerVm = ctx.DocumentDockService.OpenDocuments
                .OfType<HexIDE.VisualDesigner.FormEditViewModel>()
                .FirstOrDefault(d => d.FormDefinition == form);
            var components = designerVm is not null
                ? (IEnumerable<ComponentInstance>)designerVm.AllComponents.Select(v => v.Instance).ToList()
                : form.Components;

            // An element as name(index), as get_form_controls takes it. A bare array name wrote to its first element,
            // saved, and replied as if the caller's element was set, so it is refused, naming the indexes (#1252).
            var control = PropertyText.NamedOne(components, controlName, out var ambiguous);
            if (ambiguous is not null)
                return (null, null, $"Nothing was changed: {ambiguous}"
                    + (string.Equals(property.Trim('(', ')'), "Name", StringComparison.OrdinalIgnoreCase)
                        ? ". Renaming one element takes it out of the array, and nothing in the IDE joins it back yet (#1276)"
                        : ""));
            if (control is null)
                return (null, null, $"No control named '{controlName}' on {document.Display}");

            // As the Properties window shows them, case and the parentheses of '(Name)' aside: a caller writes
            // 'Name', and was told the property did not exist. (#494)
            var propClass = control.BaseClass.PropertiesByName.GetValueOrDefault(property)
                ?? control.BaseClass.Properties.FirstOrDefault(p => string.Equals(
                       p.Name.Trim('(', ')'), property.Trim('(', ')'), StringComparison.OrdinalIgnoreCase));
            if (propClass is null)
                return (null, null, $"Property '{property}' not found on {control.BaseClass.VBTypeName}");

            // Any spelling the Properties window shows for a value; colours and enums were refused outright. (#641)
            // With the value it holds, so a font given in part keeps the rest (#952).
            // A face named here is a face, whatever its name ends in: Wingdings 2 read as Wingdings at size 2 (#1311).
            if (!PropertyText.TryParse(propClass, value, out var parsed, out var why, control.GetBoxedPropertyOrDefault(propClass),
                    HexIDE.Runtime.BuiltinTypes.VBFontExtensions.IsNameable))
                return (null, null, $"Cannot set '{propClass.Name}' to '{value}': {(why is null ? "it" : why + ". It")} "
                                    + $"takes {PropertyText.Accepted(propClass)}");

            // A position is in the units its row shows: its container's ScaleMode, and a form's own size with its frame, in
            // twips (#1636). The model holds pixels.
            if (DesignPositions.IsPosition(propClass) && parsed is double shownPosition)
                parsed = DesignPositions.ToPixels(form, control, propClass, shownPosition);

            // Refused BEFORE the property is set, for the same reason as set_file_content. (#538)
            if (HexIDE.IDE.ScriptedFileDialogs.WouldShowPicker(ownerModule is not null ? ownerModule.AbsolutePath : form.AbsolutePath))
                return (null, null,
                    $"{(ownerModule is not null ? "UserControl" : "Form")} '{document.Name}' {HexIDE.IDE.ScriptedFileDialogs.PickerRefusal}");

            var before = control.GetBoxedPropertyOrDefault(propClass);
            // A renamed element leaves its array, keeping its Index, so the reply says so and how it joins back (#1276).
            var leaves = propClass == VBProperties.NameProperty && PropertyText.ArrayIndexOf(control) is { } leaving
                ? (Index: leaving, Others: PropertyText.Named(components, (string)before!).Count - 1)
                : ((int Index, int Others)?)null;

            // A rename passes the Properties window's rules on every route. With the designer open its handler
            // enforces them as the value is set, and throws; without it nothing would, so they are asked here.
            if (propClass == VBProperties.NameProperty && designerVm is null
                && HexIDE.VisualDesigner.ComponentNaming.RefusalFor(control, before as string, parsed as string,
                       form.Components, form, control.BaseClass is FormComponentClass, ctx.Localization) is { } refusal)
                return (null, null, $"'{controlName}' was not renamed: {refusal}");
            try
            {
                control.SetUntypedProperty(propClass, parsed);
            }
            catch (Avalonia.Data.DataValidationException ex)
            {
                return (null, null, $"'{controlName}' was not {(propClass == VBProperties.NameProperty ? "renamed" : "changed")}: {ex.Message}");
            }

            // With what VB6's designer changes along with it, applied with a designer open or not (#1428).
            var consequences = designerVm is not null
                ? designerVm.PushSetPropertyCommand(control, propClass, before, parsed)
                : Equals(before, parsed) ? [] : control.BaseClass.ApplyDesignerConsequences(control, propClass);

            // The third path that bypasses the designer's undo stack (#273 task 3.3). With a designer open
            // the push above commits, flushes and announces; with none there is nothing to push to, so the
            // model is changed and nothing tells the code window its header may have moved -- including
            // when the property set was the form's own (Name).
            //
            // It overlaps the save below almost entirely, and is not redundant with it: the save can be
            // refused (an unfaithful form), can have nowhere to go (a UserControl with no path), or can
            // throw. In those cases this is the only thing that keeps the buffer honest.
            if (designerVm is null)
            {
                form.NotifyRootPropertiesChanged();
                ctx.EventBus.Publish(new HexIDE.Events.FormLayoutChangedEvent(form));
            }

            // Read back AFTER the announcement, so the reply quotes the value as it now stands rather than
            // as it stood a notification ago -- a rename in particular is only visible once the model has
            // raised it.
            stored = $"{PropertyText.AddressedName(control)}.{propClass.Name.Trim('(', ')')} is now "
                     + (DesignPositions.IsPosition(propClass) && control.GetBoxedPropertyOrDefault(propClass) is double now
                         ? PropertyText.Display(DesignPositions.Shown(form, control, propClass, now)) + " "
                           + DesignPositions.Unit(form, control, propClass)
                         : PropertyText.Display(control.GetBoxedPropertyOrDefault(propClass)))
                     + ", and the file is saved.";
            foreach (var change in consequences)
                stored += $" Its {change.Property.Name} is now {PropertyText.Display(change.After)}, was "
                          + $"{PropertyText.Display(change.Before)}, as VB6's designer changes it with {propClass.Name.Trim('(', ')')}.";
            if (leaves is { Others: > 0 } left && !string.Equals(before as string, parsed as string, StringComparison.OrdinalIgnoreCase))
                stored += $" It was {before}({left.Index}), and has left that array, whose other {left.Others} "
                          + $"element{(left.Others == 1 ? "" : "s")} keep the name. It keeps Index {left.Index}, so naming it "
                          + $"{before} again joins it back.";
            if (leaves is { } joining && PropertyText.Named(components, (string)parsed!).Count > 1
                && !string.Equals(before as string, parsed as string, StringComparison.OrdinalIgnoreCase))
                stored += $" It is now element {joining.Index} of the {parsed} array.";
            return (form, ownerModule, null);
        });

        // Named whatever came of the call, once the name found the document: read when the reply is built, so a
        // rename of the form itself reports the name it now has.
        if (error is not null)
            return Naming(new MutateResult(false, error), target);

        // Before the save: an armed cancel keeps the document without a file, as the refusal offers (#755).
        var cancel = HexIDE.IDE.ScriptedFileDialogs.WatchForCancel(ownerModule is not null ? ownerModule.AbsolutePath : form!.AbsolutePath);
        try
        {
            // Same UI-thread requirement as the other write tool, and for the same reason. (#334)
            var rewrites = new HexIDE.Projects.SaveRewriteReport();
            var (written, refused) = await SaveQuietlyAsync(() => ownerModule is not null
                ? ctx.ProjectService.SaveModule(ownerModule, false, interactive: false, rewrites: rewrites)
                : ctx.ProjectService.SaveForm(form!, false, interactive: false, rewrites: rewrites));
            if (refused is not null)
                return Naming(new MutateResult(false, cancel.After($"'{property}' is set in the IDE, but the file was not written. {refused.Message}")), target);
            // See the note on the other write tool: a refusal must not come back as success. (#147)
            return Naming(written
                ? new MutateResult(true, null, cancel.After(stored + rewrites.Note))
                : new MutateResult(false, cancel.After("HexIDE cannot reproduce this file faithfully, so it was not "
                                        + "written and the copy on disk is unchanged.")), target);
        }
        catch (OperationCanceledException) when (cancel.Cancelled || cancel.Refused)
        {
            var kept = $"'{property}' is set in the IDE. {(ownerModule is not null ? "UserControl" : "Form")} '{target!.Name}' "
                       + $"still has no file: {cancel.HowAnswered}, so nothing was written. To write it, arm "
                       + "answer_next_file_dialog with a path and set a property again.";
            return Naming(cancel.Refused ? new MutateResult(false, cancel.After(kept)) : new MutateResult(true, null, kept), target);
        }
        catch (Exception ex)
        {
            // The property is already set in the IDE by now; a bare exception message would read as though it
            // were not.
            return Naming(new MutateResult(false, cancel.After($"'{property}' is set in the IDE but was not written: {ToolFailures.WithoutProfile(ex.Message)}")), target);
        }
    }

    [McpServerTool(Name = "open_file")]
    [Description("Opens a form, module or carried file by name in the IDE code editor. Use get_project_info to list available names. Searches every loaded project, without regard to case; pass `project` when a group holds two documents of one name, and without it an ambiguous name is refused and the reply lists them. The reply's 'project' and 'document' name what was opened, as the IDE spells them. A carried file is also found by its filename, which differs from its name when VB6 carried it on a code line (`Module=Notes; Notes.md` is named Notes). The editor gets keyboard focus, as opening it by hand gives it. The reply's 'note' names the tab now active and how many are open. Pass 'line', and 'column' if wanted, to put the caret there, as the code window numbers them, so type_text and press_key act at that point with no offset to work out; the note then gives the caret's line, column and offset.")]
    public async Task<MutateResult> OpenFileAsync(
        string name, string? project = null,
        [Description("Where to put the caret: a 1-based line, counted from the top of the file with its header included, "
                     + "as the code window, get_file_content and set_breakpoints count it. Not for a carried file. A line "
                     + "or column off the text is refused, and nothing is opened.")]
        int? line = null,
        [Description("The 1-based column on 'line', 1 by default, counted in characters as the status bar counts them, so a "
                     + "tab is one; one past the line's last character is its end.")]
        int? column = null,
        CancellationToken ct = default)
    {
        if (column is not null && line is null)
            return new MutateResult(false, "'column' needs 'line'; nothing was opened");
        DocumentIdentity? placedIn = null;
        RelatedDocumentDefinition? placedInCarried = null;
        var opened = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Carried files are the only project members with no other route in: the Project Explorer opens
            // one on a DOUBLE-CLICK, which no interaction tool could produce when this was written, and Add
            // File goes through a native dialog. Without them here a whole editor type is undrivable, which is
            // what blocked verifying #255 against the running IDE. See docs/mcp-server-gaps.md.
            var found = Find(name, project, carried: true);
            if (found.Document is { } document)
            {
                // Checked against the text get_file_content returns before anything opens, so a refusal changes nothing.
                if (line is { } wanted && HexIDE.Automation.CaretPlacement.Refusal(
                        HexIDE.IDE.DocumentContent.TextOf(document, CodeEditorOf(document)).Text, wanted, column ?? 1) is { } refusal)
                    return Naming(new MutateResult(false, $"Nothing was opened: in {document.Display}, {refusal}"), document);
                if (document.Form is { } form)
                    ctx.EditorService.EditCode(form);
                else
                    ctx.EditorService.EditCode(document.Module);
                if (line is { } at)
                {
                    if (CodeEditorOf(document) is not { } editor || !HexIDE.Automation.CaretPlacement.Place(editor, at, column ?? 1))
                        return Naming(new MutateResult(false,
                            $"{document.Display} was opened, but its code window could not be found to put the caret in"), document);
                    placedIn = document;
                }
                return Naming(new MutateResult(true, null, TabStateNote()), document);
            }
            if (found.Carried is { } carried)
            {
                // Placed as in a code window, against the text get_file_content returns (#2403). It was refused, opening
                // nothing, which left a caller to walk the caret there a key at a time.
                if (line is { } wanted)
                {
                    var (text, missing) = CarriedTextOf(carried);
                    if (text is null)
                        return Naming(new MutateResult(false, $"Nothing was opened: {missing}"), found);
                    if (HexIDE.Automation.CaretPlacement.Refusal(text, wanted, column ?? 1) is { } refusal)
                        return Naming(new MutateResult(false, $"Nothing was opened: in {carried.Name}, {refusal}"), found);
                }
                ctx.EditorService.EditRelatedDocument(carried);
                if (line is { } at)
                {
                    if (CarriedEditorOf(carried) is not { } editor || !HexIDE.Automation.CaretPlacement.Place(editor, at, column ?? 1))
                        return Naming(new MutateResult(false,
                            $"{carried.Name} was opened, but its editor could not be found to put the caret in"), found);
                    placedInCarried = carried;
                }
                return Naming(new MutateResult(true, null, TabStateNote()), found);
            }
            return new MutateResult(false, found.Error);
        });

        var focused = await FocusingTheActiveTabAsync(opened);
        if ((placedIn is null && placedInCarried is null) || !focused.Success)
            return focused;
        // Read back, not echoed (#700 review): once focus and the view's first layout have run, as the view opens a fold
        // around a caret navigated into it and the editor moves one it could not show.
        var where = await Dispatcher.UIThread.InvokeAsync(
            () => (placedIn is { } placed ? CodeEditorOf(placed) : (ISearchableDocument?)CarriedEditorOf(placedInCarried!)) is { } editor
                ? HexIDE.Automation.CaretPlacement.Where(editor, line!.Value, column ?? 1) : null,
            DispatcherPriority.Background);
        return where is null ? focused : focused with { Note = $"{focused.Note} {where}" };
    }

    [McpServerTool(Name = "view_designer")]
    [Description("Opens a form or UserControl by name in the visual designer, bringing it to the front. Useful before take_snapshot to ensure the designer surface is visible. Use get_project_info to list available names. Searches every loaded project, without regard to case; pass `project` when a group holds two documents of one name, and without it an ambiguous name is refused and the reply lists them. The reply's 'project' and 'document' name what was opened, as the IDE spells them. The designer gets keyboard focus, as opening it by hand gives it. The reply's 'note' names the tab now active and how many are open.")]
    public async Task<MutateResult> ViewDesignerAsync(string name, string? project = null, CancellationToken ct = default)
    {
        return await FocusingTheActiveTabAsync(await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var named = FindDesigner(name, project);
            if (named.Form is not { } form)
                return new MutateResult(false, named.Error);
            ctx.EditorService.EditForm(form);
            return Naming(new MutateResult(true, null, TabStateNote()), named.Document);
        }));
    }

    /// <summary>
    /// Gives the active tab's editor or designer keyboard focus, as opening or clicking it by hand does, and
    /// says so in the reply's note.
    /// </summary>
    /// <remarks>
    /// Most built-in menu items are routed commands that act on the focused control, so an item belonging to
    /// a document was refused after open_file until something else had put focus there. (#678) Retried
    /// briefly because a newly opened document's view does not exist until the next layout pass. A tool tab
    /// is left alone: it has no single control a click would focus.
    /// </remarks>
    private async Task<MutateResult> FocusingTheActiveTabAsync(MutateResult activated)
    {
        if (!activated.Success) return activated;

        // Not while a dialog is open over the IDE. Someone may be typing into it, and pulling focus behind it
        // would take their keystrokes or leave it unfocused; a person could not move focus there either.
        var dialog = await Dispatcher.UIThread.InvokeAsync(OpenDialogTitle);
        if (dialog is not null)
            return activated with
            {
                Note = activated.Note + $" A dialog is open ('{dialog}'), so keyboard focus was left in it; a menu "
                       + "item that acts on this document may be refused until the dialog is closed.",
            };

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var focused = await Dispatcher.UIThread.InvokeAsync(FocusActiveTab, DispatcherPriority.Background);
            if (focused is { } done)
                return done == FocusOutcome.NotAnEditor ? activated : activated with
                {
                    Note = activated.Note + (done == FocusOutcome.Focused
                        ? " Keyboard focus is in it."
                        : " Keyboard focus could not be put in it, so a menu item that acts on it may be refused "
                          + "until press_key reaches it."),
                };
            await Task.Delay(50);
        }
        return activated with
        {
            Note = activated.Note + " Keyboard focus could not be put in it, so a menu item that acts on it may be "
                   + "refused until press_key reaches it.",
        };
    }

    private enum FocusOutcome { Focused, Refused, NotAnEditor }

    /// <summary>The title of a dialog open over the IDE, or null when there is none.</summary>
    /// <remarks>
    /// A dialog is shown with an owner, which is how <see cref="HexIDE.IDE.ForegroundWindow"/> tells it apart; a
    /// running program's form has none, so it does not count here.
    /// </remarks>
    private static string? OpenDialogTitle()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
            { MainWindow: { } main } lifetime)
            return null;
        var front = HexIDE.IDE.ForegroundWindow.Pick(main, lifetime.Windows);
        return front != main && front.Owner is not null ? front.Title ?? "untitled" : null;
    }

    /// <summary>
    /// Puts focus in the active tab's own editor or designer surface, or says why not; null when its view is
    /// not built yet.
    /// </summary>
    private FocusOutcome? FocusActiveTab()
    {
        var tab = ctx.DocumentDockService.ActiveTab;
        if (tab is not (BaseEditorWindowViewModel or HexIDE.VisualDesigner.FormEditViewModel)) return FocusOutcome.NotAnEditor;
        if ((Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
            is not { } window) return FocusOutcome.Refused;

        var own = window.GetVisualDescendants().OfType<Control>()
            .Where(c => ReferenceEquals(c.DataContext, tab) && c.IsEffectivelyVisible && c.IsEffectivelyEnabled)
            .ToList();
        // The surface a click lands on: an editor's text area, or the form itself in a designer, which is
        // what FormEditView makes focusable and what a click on the canvas focuses.
        var surface = own.FirstOrDefault(c => c is AvaloniaEdit.Editing.TextArea)
                      ?? own.FirstOrDefault(c => c.Name == "FormContainer");
        return surface is null ? null : surface.Focus() ? FocusOutcome.Focused : FocusOutcome.Refused;
    }

    [McpServerTool(Name = "run_project")]
    [Description("Starts running the current VB6 project in the IDE. Returns an error if no project is loaded, if it is already running, or if the start never becomes a run: a syntax error in the startup form or in any standard or class module (the error names the module), no startup form, a Sub Main project with no usable or with two Sub Mains, a startup form that cannot be built, or a declaration that cannot be loaded — in those cases nothing is running, the IDE has opened a runtime-error dialog, and get_last_runtime_error returns the same text. step_into, step_over, step_out and run_to_cursor report a failed start from idle the same way. The reply's 'note' says, after up to 2 s, whether it paused (at a breakpoint or Stop, with the module and the line, counted from the top of the file as the code window numbers it), is running, or ended, and names any run-time error it raised, including reaching a construct HexIDE does not implement, which ends the run. It also says how much Debug.Print wrote, which 'printed' holds.")]
    public async Task<MutateResult> RunProjectAsync(CancellationToken ct)
    {
        var (refused, outcome) = await StartAndAwaitStopAsync(() =>
        {
            if (!ctx.ProjectRunnerService.CanStartDefaultProject)
            {
                var reason = ctx.ProjectManager.StartupProject is null
                    ? "No project loaded"
                    : "Project is already running";
                return new MutateResult(false, reason);
            }
            // So get_last_runtime_error answers "did THIS run raise" rather than "has anything ever".
            // The sequence survives, so a caller holding an older one can still tell something happened.
            ctx.RootViewModel.RuntimeErrors.Clear();
            return StartFailure(() => ctx.ProjectRunnerService.RunStartupProject());
        }, RunReplyWait, ct);
        if (refused is not null)
            return refused;

        return WithPrinted(new MutateResult(true, null, (outcome.Stop is { } at
            ? PausedAt(at)
            : outcome.Running ? $"Running; no pause within {RunReplyWait.TotalSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture)} s." : "The program ran and ended.") + RaisedNote(outcome)));
    }


    /// <summary>
    /// Runs a call that may start the project, and answers the failure if the start never became a run.
    /// </summary>
    /// <remarks>
    /// A startup form is built synchronously inside the start call, so a form that cannot be built has been
    /// reported by the time the call returns. Every tool that can start a run from idle used to answer success
    /// regardless, while nothing ran and the error reached only the log (#590). A Sub Main project's
    /// declarations are loaded synchronously in the same way, and a failure there is reported the same way
    /// (#754), and so is a syntax error found before the run begins (#759).
    /// </remarks>
    private MutateResult? StartFailure(Action start)
    {
        string? failure = null;
        void OnFailed(string message) => failure = message;
        ctx.ProjectRunnerService.StartFailed += OnFailed;
        try
        {
            start();
        }
        finally
        {
            ctx.ProjectRunnerService.StartFailed -= OnFailed;
        }
        return failure is null
            ? null
            : new MutateResult(false, HexIDE.IDE.MessageText.StartFailedReply(failure));
    }

    [McpServerTool(Name = "get_last_runtime_error")]
    [Description("Returns the last runtime error the running program raised, WITH ITS TEXT, and keeps it after the error dialog has been dismissed. That includes a compile error met while it ran, and the reason a start never became a run; a line such an error names counts from the top of the file, header included, as the code window numbers it. Use it after any run that might raise: stop_project and shutdown_ide both close open dialogs, so a run whose form silently did nothing is otherwise indistinguishable from a run whose error dialog was dismissed a moment earlier. run_project clears the message first, so a result here belongs to the most recent run. 'sequence' only ever increases and is not cleared — compare it across runs to tell 'no error' from 'the same error again', which message text cannot do. 'module' and 'line' say where it was raised, the line counted as the code window numbers it, when a statement of the program raised it; a start that never became a run has neither. 'at' is when it was raised, in UTC like every other stamp the tools return. Returns raised:false when the current run has raised nothing.")]
    public async Task<RuntimeErrorResult> GetLastRuntimeErrorAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var last = ctx.RootViewModel.RuntimeErrors.Last;
            return last is null
                ? new RuntimeErrorResult(false, null, null, ctx.RootViewModel.RuntimeErrors.Sequence)
                : new RuntimeErrorResult(true, last.Value.Message,
                                         last.Value.At.ToString("o"), last.Value.Sequence,
                                         last.Value.Where?.Module, last.Value.Where?.Line);
        });
    }

    [McpServerTool(Name = "stop_project")]
    [Description("Stops the currently running VB6 project in the IDE. The reply's 'note' says whether the IDE is back in design mode, and how much Debug.Print wrote since the run began or a reply last reported it, which 'printed' holds.")]
    public async Task<MutateResult> StopProjectAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!ctx.ProjectRunnerService.CanEndProject)
                return new MutateResult(false, "No project is currently running");
            ctx.ProjectRunnerService.EndProject();
            return WithPrinted(new MutateResult(true, null, ctx.ProjectRunnerService.IsRunning
                ? "Stop requested; the program has not ended yet. get_debug_state says when it has."
                : "Stopped; the IDE is back in design mode."));
        });
    }

    [McpServerTool(Name = "get_window_state")]
    [Description("Returns a window's state (Maximized/Normal/Minimized) and the reported position and client size in every state: the IDE's own window unless 'window' names another, such as a running form. x and y are in screen pixels and width and height in device-independent pixels, as set_window_state takes them; 'outer' (the whole window, on Windows with its invisible resize border), 'frame' (as drawn) and 'client' give each edge on the screen in device-independent pixels, with 'scale' beside them, and 'units' says so in the reply. 'name' is what to pass as 'window' to reach the same window again.")]
    public async Task<WindowStateResult> GetWindowStateAsync(
        [Description(WindowStateParameter)] string? window = null, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var (target, name, error) = ResolveStateWindow(window);
            return target is null ? new WindowStateResult("Unknown", 0, 0, 0, 0, null, error) : WindowStateOf(target, name!);
        });
    }

    private const string WindowStateParameter =
        "Which window. Omitted, or \"ide\", is the IDE's own, as before this parameter existed. A running form's name such "
        + "as \"Form2\", or a window's title, reaches that window, minimized ones included, and \"auto\" picks as "
        + "dump_visual_tree does. An unknown name is refused with the open windows listed.";

    // The window get_window_state and set_window_state act on: the IDE's own when 'window' is omitted, as it was before the
    // parameter existed, though the tree tools default to "auto" (#2357). Named as a caller passes it to reach it again.
    private static (Avalonia.Controls.Window? Window, string? Name, string? Error) ResolveStateWindow(string? window)
    {
        var (target, _, error) = ResolveActiveWindow(string.IsNullOrWhiteSpace(window) ? "ide" : window, ideByDefault: true);
        if (target is null)
            return (null, null, error);
        return (target, StateNameOf(target), null);
    }

    // A window as the window-state tools name it, which is what a caller passes as 'window' to reach it again.
    private static string StateNameOf(Avalonia.Controls.Window target)
    {
        var main = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        return target == main ? "ide"
            : HexIDE.Runtime.BuiltinControls.VBProps.GetName(target) is { Length: > 0 } form ? form
            : DescribeWindow(target);
    }

    internal static WindowStateResult WindowStateOf(Avalonia.Controls.Window window, string name)
    {
        var state = window.WindowState switch
        {
            Avalonia.Controls.WindowState.Maximized => "Maximized",
            Avalonia.Controls.WindowState.Minimized => "Minimized",
            _ => "Normal"
        };
        var pos  = window.Position;
        var size = window.ClientSize;
        // The window's whole rectangle, the frame a person sees and the client area, all in device-independent pixels on
        // the screen, beside the scale that turns them into screen pixels. x and y alone gave the platform's position in
        // screen pixels, which on Windows includes an invisible resize border, and width and height gave the client in
        // device-independent pixels, with nothing to say so or which edge each measured (#2454). They are kept, as
        // set_window_state takes them.
        var scale = window.DesktopScaling;
        var outer = HexIDE.Automation.NativeWindowFrame.OuterRect(window) is { } o ? WindowRect.Of(o, scale) : null;
        var frame = HexIDE.Automation.NativeWindowFrame.VisibleFrame(window) is { } f ? WindowRect.Of(f, scale) : null;
        var client = window.IsVisible && window.WindowState != Avalonia.Controls.WindowState.Minimized
            ? WindowRect.Of(window.PointToScreen(new Avalonia.Point(0, 0)), size, scale) : null;
        return new WindowStateResult(state, pos.X, pos.Y, (int)size.Width, (int)size.Height, name,
            Scale: Math.Round(scale, 3), Outer: outer, Frame: frame, Client: client, Units: WindowStateUnits);
    }

    /// <summary>What every get_window_state reply says of its numbers, since two of them are in one unit and two in another.</summary>
    internal const string WindowStateUnits =
        "x and y: the window's position as the platform reports it, in screen pixels, the top left of outer. width and "
        + "height: the client area in device-independent pixels. set_window_state takes all four as these. outer, frame "
        + "and client are in device-independent pixels on the screen (twips divided by 15); multiply by scale for screen "
        + "pixels. outer is the window's whole rectangle, which on Windows includes an invisible resize border; frame is "
        + "the window as drawn, title bar and border included; client is the area inside. None is given for a window "
        + "that is hidden or minimized, and Windows puts a minimized window's x and y at -32000. outer and frame are "
        + "also left out where the platform reports no frame.";

    /// <summary>
    /// Why a person couldn't make <paramref name="target"/> the state or size asked for: it has no minimize or maximize box,
    /// or a border that can't be dragged. Null when they could. Only a window other than the IDE's own is held to this.
    /// </summary>
    internal static string? WindowBoxRefusal(Avalonia.Controls.Window target, string name,
        Avalonia.Controls.WindowState state, bool resizes) => state switch
    {
        Avalonia.Controls.WindowState.Minimized when !target.CanMinimize =>
            $"'{name}' has no minimize box, so a person can't minimize it. On a running form, MinButton, ControlBox and "
            + "BorderStyle decide whether it has one; the program can still set its WindowState. Nothing was changed.",
        Avalonia.Controls.WindowState.Maximized when !target.CanMaximize =>
            $"'{name}' has no maximize box, so a person can't maximize it. On a running form, MaxButton, ControlBox and "
            + "BorderStyle decide whether it has one; the program can still set its WindowState. Nothing was changed.",
        Avalonia.Controls.WindowState.Normal when resizes && !target.CanResize =>
            $"'{name}' has a fixed border, so a person can't resize it; width and height are refused. x and y alone move "
            + "it. Nothing was changed.",
        _ => null,
    };

    [McpServerTool(Name = "set_window_state")]
    [Description("Sets a window to Maximized, Normal, or Minimized, the IDE's own unless 'window' names another, and replies with the resulting state, position and size as get_window_state reports them. Another window, such as a running form, changes as a click on its minimize, maximize or restore box would, so its Resize runs, and the reply waits for the program as interact's does. It is refused where a person couldn't do the same: no such box, a fixed border for a resize, a disabled window, or one under a modal dialog. Waits up to 1 s for the state, then up to 1 s for any requested move/resize; if unconfirmed, success is false and note explains how to re-read, or, on an X11 display with no window manager, that a minimize or maximize can never be applied there. When setting Normal, any of `x`, `y`, `width` and `height` that are passed are applied after a confirmed restore; each is optional, and one left out keeps its current value. `x` and `y` are screen pixels and `width` and `height` the client area in device-independent pixels, as get_window_state reports them. They apply only to Normal and are refused with any other state. `width` and `height` must be positive, and a position that would leave the window on no screen at all is refused with the screens' bounds. An invalid argument is refused before anything changes. A timed-out request may still be applied later.")]
    public async Task<WindowStateChangeResult> SetWindowStateAsync(
        string state,
        // Defaults, not just nullable types: a nullable parameter with no default is required in the schema,
        // so the "optional" the description promised was refused on the wire. (#582)
        int? x = null, int? y = null, int? width = null, int? height = null,
        [Description(WindowStateParameter)] string? window = null,
        CancellationToken ct = default)
    {
        UiAutomationDriver.ProgramWatch? watch = null;
        Avalonia.Controls.Window? acted = null;
        string? actedName = null;
        var result = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var (resolved, name, resolveError) = ResolveStateWindow(window);
            if (resolved is null)
                return new WindowStateChangeResult(false, resolveError, null);
            var main = resolved == (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

            var target = state.ToLowerInvariant() switch
            {
                "maximized" => Avalonia.Controls.WindowState.Maximized,
                "minimized" => Avalonia.Controls.WindowState.Minimized,
                "normal"    => Avalonia.Controls.WindowState.Normal,
                _ => (Avalonia.Controls.WindowState?)null
            };

            if (target is null)
                return new WindowStateChangeResult(false, $"Unknown state '{state}' — use Maximized, Normal, or Minimized", null);

            // Everything is checked before anything changes. A negative size used to throw out of the tool, which
            // reached the caller as a bare "An error occurred invoking"; x without y was dropped with success; and
            // a position on no screen was accepted, which lost the IDE window where nobody could reach it. (#602)
            var geometry = x is not null || y is not null || width is not null || height is not null;
            if (geometry && target != Avalonia.Controls.WindowState.Normal)
                return new WindowStateChangeResult(false,
                    $"x, y, width and height apply only to Normal, not {target}. Nothing was changed.", null);
            if (width is <= 0 || height is <= 0)
                return new WindowStateChangeResult(false,
                    $"width and height must be positive; got {(width is <= 0 ? $"width {width}" : $"height {height}")}. Nothing was changed.", null);
            // Another window is changed as a person changes it, so what a person couldn't do is refused (#2357). The IDE's
            // own is set as it always was.
            if (!main && (WindowBoxRefusal(resolved, name!, target.Value, width is not null || height is not null)
                          ?? UiAutomationDriver.WindowRefusal(resolved, "set_window_state", name)) is { } refusal)
                return new WindowStateChangeResult(false, refusal, null);

            // Validate before sending even the restore: an asynchronous backend cannot undo it reliably.
            if (geometry)
            {
                var position = new Avalonia.PixelPoint(x ?? resolved.Position.X, y ?? resolved.Position.Y);
                var size = Avalonia.PixelSize.FromSize(
                    new Avalonia.Size(width ?? resolved.ClientSize.Width, height ?? resolved.ClientSize.Height),
                    resolved.RenderScaling);
                var frame = new Avalonia.PixelRect(position, size);
                var screens = resolved.Screens.All;
                if (screens.Count > 0 && !screens.Any(s => s.WorkingArea.Intersects(frame)))
                    return new WindowStateChangeResult(false,
                        $"({position.X},{position.Y}) would put the window on no screen. Screens: "
                        + string.Join("; ", screens.Select(s => $"{s.WorkingArea.X},{s.WorkingArea.Y} {s.WorkingArea.Width}×{s.WorkingArea.Height}"))
                        + ". Nothing was changed.", null);
            }

            // Read before the change, as interact's is: what the program does with it, its Resize, runs after (#2357).
            if (!main)
                watch = UiAutomationDriver.WatchProgram(resolved, () => ctx.DebugController.CurrentStop);
            (acted, actedName) = (resolved, name);
            var applied = await HexIDE.Automation.WindowChange.ApplyAsync(resolved, target.Value, x, y, width, height, ct);
            if (applied)
                return new WindowStateChangeResult(true, null, WindowStateOf(resolved, name!), null);
            var (error, note) = Unconfirmed(target.Value, X11WindowManager.IsRunning());
            return new WindowStateChangeResult(false, error, WindowStateOf(resolved, name!), note);
        });
        if (watch is null || acted is null)
            return result;

        // Waited off the UI thread, as interact's is, then read again: the program's Resize may move or size the form.
        var waited = await UiAutomationDriver.WaitForProgramAsync(watch, new InteractOutcome(true, "window", "", null), ct: ct);
        var after = await Dispatcher.UIThread.InvokeAsync(() => WindowStateOf(acted, actedName!));
        var programNote = waited.Detail is { Length: > 0 } detail ? ProgramNote(detail) : null;
        return result with
        {
            Window = after,
            Note = result.Note is null ? programNote : programNote is null ? result.Note : result.Note + " " + programNote,
        };
    }

    // The program wait's parenthesised addition to a reply, as a sentence of its own for a note.
    internal static string ProgramNote(string detail)
    {
        var text = detail.Trim();
        if (text.StartsWith('(') && text.EndsWith(')'))
            text = text[1..^1];
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }

    /// <summary>
    /// set_window_state's error and note for a change nothing confirmed in time. With no window manager on an X11 display,
    /// nothing ever applies a minimize or a maximize, and saying the window manager had not confirmed sent the caller to
    /// re-read and retry (#2185). A restore with a position and size is the X server's own work, so it keeps the usual text.
    /// </summary>
    internal static (string Error, string Note) Unconfirmed(Avalonia.Controls.WindowState target, bool? windowManagerRunning) =>
        windowManagerRunning is false && target is not Avalonia.Controls.WindowState.Normal
            ? ($"No window manager is running on this display, so nothing will apply {target}.",
               "This X11 display's root window has no _NET_SUPPORTING_WM_CHECK, which a window manager sets, so minimize and "
               + "maximize can't happen here, however long you wait. Normal with x, y, width and height is applied by the X server "
               + "itself, and still works. The window field reports what is observed.")
            : ("The window manager has not confirmed the requested change.",
               "The window manager did not confirm the requested state or geometry within 1 s of that request; the window field "
               + "reports what is observed. Re-read with get_window_state.");

    [McpServerTool(Name = "get_tool_windows")]
    [Description("Returns the list of all registered tool panels with their current visibility.")]
    public async Task<ToolWindowsResult> GetToolWindowsAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var tools = ctx.RootViewModel.GetToolWindows()
                .Select(t => new ToolWindowInfo(t.Name, t.Visible))
                .ToArray();
            return new ToolWindowsResult(tools);
        });
    }

    [McpServerTool(Name = "set_tool_window_visible")]
    [Description("Shows or hides a named tool panel. Valid names: Toolbox, Properties, ProjectGroup, FormLayout, Immediate, Locals, Watches, CallStack, or an add-in tool window's title. The View menu's names for the same panels, such as 'Immediate Window' or 'Project Explorer', are accepted too, without regard to case. Hiding a window closes it alone, as its tab's close button does, not the other windows tabbed with it. The reply's 'note' says whether the window is now shown or hidden, by the name get_tool_windows gives it, and names any other window that opened or closed with it.")]
    public async Task<MutateResult> SetToolWindowVisibleAsync(string name, bool visible, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var (error, note) = ctx.RootViewModel.SetToolWindowVisible(name, visible);
            return error is null ? new MutateResult(true, null, note) : new MutateResult(false, error);
        });
    }

    [McpServerTool(Name = "get_undo_state")]
    [Description("The reply identifies an active source editor or designer by project and document. Returns the current undo/redo state of the active document: what kind it is, whether Undo and Redo are available, and, for a form designer, the step each would act on as the Edit menu names it. 'activeEditorKind' is FormDesigner, CodeEditor, Other (a document with no undo of its own, such as the Object Browser; canUndo and canRedo are then false) or None (no document is open). A code editor's steps have no names, so its descriptions are null; invoke_menu_item(\"Edit/Undo\") undoes there, and invoke_designer_undo is for a designer only.")]
    public async Task<UndoStateResult> GetUndoStateAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // The tab in front, not ActiveDocument, which is null while a tool such as the Object Browser is
            // in front and so reported that as no document open.
            var active = ctx.DocumentDockService.ActiveTab;
            if (active is HexIDE.VisualDesigner.FormEditViewModel designer)
                return new UndoStateResult(
                    "FormDesigner",
                    designer.CanUndo,
                    designer.CanRedo,
                    designer.UndoStack.UndoDescription,
                    designer.UndoStack.RedoDescription,
                    designer.OpenDocument?.Project.Name, designer.OpenDocument?.Name);

            // A code editor has undo of its own, and this said it could never undo; the kind was also the
            // document's .NET type name, a set no description could list. (#672)
            if (active is CodeEditorViewModel code)
                return new UndoStateResult(
                    "CodeEditor", code.Document.UndoStack.CanUndo, code.Document.UndoStack.CanRedo, null, null,
                    code.OpenDocument?.Project.Name, code.OpenDocument?.Name);

            return new UndoStateResult(active is null ? "None" : "Other", false, false, null, null);
        });
    }

    /// <summary>
    /// What Undo and Redo would now do, as the Edit menu names them. The undo tools replied {"success":true} and
    /// nothing else, so a caller had to call get_undo_state to learn which step had gone and which was next (#655).
    /// </summary>
    private static string UndoStackNote(HexIDE.VisualDesigner.FormEditViewModel designer) =>
        (designer.UndoStack.UndoDescription is { } undo ? $"Next undo: '{undo}'" : "Nothing left to undo")
        + "; " + (designer.UndoStack.RedoDescription is { } redo ? $"next redo: '{redo}'." : "nothing to redo.");

    [McpServerTool(Name = "invoke_designer_undo")]
    [Description("Invokes Undo on the active form designer. The reply's project and document identify it. Returns an error if no form designer is active or nothing is on the undo stack. The reply's 'note' names the step undone and what Undo and Redo would do next.")]
    public async Task<MutateResult> InvokeDesignerUndoAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ctx.DocumentDockService.ActiveDocument is not HexIDE.VisualDesigner.FormEditViewModel designer)
                return new MutateResult(false, "Active window is not a form designer");
            if (!designer.CanUndo)
                return Naming(new MutateResult(false, "Nothing to undo"), designer.OpenDocument);
            var undone = designer.UndoStack.UndoDescription;
            designer.UndoStack.Undo();
            return Naming(new MutateResult(true, null, $"Undid '{undone}'. " + UndoStackNote(designer)), designer.OpenDocument);
        });
    }

    [McpServerTool(Name = "invoke_designer_redo")]
    [Description("Invokes Redo on the active form designer. The reply's project and document identify it. Returns an error if no form designer is active or nothing is on the redo stack. The reply's 'note' names the step redone and what Undo and Redo would do next.")]
    public async Task<MutateResult> InvokeDesignerRedoAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ctx.DocumentDockService.ActiveDocument is not HexIDE.VisualDesigner.FormEditViewModel designer)
                return new MutateResult(false, "Active window is not a form designer");
            if (!designer.CanRedo)
                return Naming(new MutateResult(false, "Nothing to redo"), designer.OpenDocument);
            var redone = designer.UndoStack.RedoDescription;
            designer.UndoStack.Redo();
            return Naming(new MutateResult(true, null, $"Redid '{redone}'. " + UndoStackNote(designer)), designer.OpenDocument);
        });
    }

    [McpServerTool(Name = "move_control")]
    [Description("Moves or resizes a control on a form designer canvas by simulating a drag transaction. " +
        "Calls BeginDrag on the active designer, updates only the supplied position/size values on the ViewModel, " +
        "then calls EndDrag — so the operation lands as a single undo step on the designer undo stack. " +
        "The form must already be open in the visual designer (call view_designer first). " +
        "Use the form's own name as controlName to resize the form itself. " +
        "Each of `left`, `top`, `width` and `height` is optional; if all are omitted, EndDrag is still called (tests the no-change path). " +
        "All four are in PIXELS, as the designer draws: a .frm and VB6's Properties window use twips, 15 to a pixel, so a VB6 value in twips must be divided by 15 here. " +
        "left/top are CONTAINER-RELATIVE, as get_form_controls and the .frm have them: for a control inside a " +
        "Frame or PictureBox they are measured from that container, not from the form. A VB6 control " +
        "array shares one name across its elements (Options Dialog.frm has four picOptions): name an element as " +
        "picOptions(2), and a bare array name is refused, naming the indexes. The form is named in any loaded project, without regard to case; pass `project` when a group holds two documents of one name, and without it an ambiguous name is refused and the reply lists them. " +
        "The reply's 'note' gives the control's position and size as they now are, and says when it now lies outside its container, and 'project' and 'document' name the form as the IDE spells them.")]
    public async Task<MutateResult> MoveControlAsync(
        string formName,
        string controlName,
        double? left = null,
        double? top = null,
        double? width = null,
        double? height = null,
        string? project = null,
        CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // The name resolves to a document first, in every loaded project, and only then to the designer
            // open on it (#273). Matching open designers by name took the first of two forms of one name in a
            // group, which may not be the one the caller meant, and named a module "not open in the designer"
            // when it has no designer at all.
            var named = FindDesigner(formName, project);
            if (named is not { Form: { } form, Document: { } document })
                return new MutateResult(false, named.Error);

            var designer = DesignerOf(form);
            if (designer is null)
                return Naming(new MutateResult(false,
                    $"{document.Display} is not open in the visual designer — call view_designer first"), document);

            // As set_control_property resolves it (#1252 review): only the first element could be reached, and a
            // bare array name moved that one, which the next save wrote to disk.
            var instance = PropertyText.NamedOne(designer.AllComponents.Select(c => c.Instance), controlName, out var ambiguous);
            if (ambiguous is not null)
                return Naming(new MutateResult(false, $"Nothing was moved: {ambiguous}"), document);
            var target = designer.AllComponents.FirstOrDefault(c => ReferenceEquals(c.Instance, instance));

            if (target is null)
                return Naming(new MutateResult(false,
                    $"No control named '{controlName}' on {document.Display}"), document);

            designer.BeginDrag([target]);

            // Written to the MODEL, not through the view-model. The view-model's Left/Top are canvas
            // coordinates — they add the accumulated origin of every container above — while
            // get_form_controls and set_control_property both read and write the model's container-relative
            // values. Going through the view-model here would make move_control the only tool on the other
            // side of that boundary, so a control inside a Frame would move to a different place than the
            // number implies. Width and Height mean the same thing in either space.
            if (left.HasValue)   target.Instance.SetProperty(VBProperties.LeftProperty, left.Value);
            if (top.HasValue)    target.Instance.SetProperty(VBProperties.TopProperty, top.Value);
            if (width.HasValue)  target.Instance.SetProperty(VBProperties.WidthProperty, width.Value);
            if (height.HasValue) target.Instance.SetProperty(VBProperties.HeightProperty, height.Value);

            designer.EndDrag();

            var i = target.Instance;
            return Naming(new MutateResult(true, null,
                $"{PropertyText.AddressedName(target.Instance)} is at Left {PropertyText.Display(i.GetPropertyOrDefault(VBProperties.LeftProperty))}, "
                + $"Top {PropertyText.Display(i.GetPropertyOrDefault(VBProperties.TopProperty))}, "
                + $"Width {PropertyText.Display(i.GetPropertyOrDefault(VBProperties.WidthProperty))}, "
                + $"Height {PropertyText.Display(i.GetPropertyOrDefault(VBProperties.HeightProperty))}, "
                + "in pixels relative to its container, as get_form_controls reports them."
                + (ReferenceEquals(target, designer.Form) ? "" : OutsideContainerNote(target))), document);
        });
    }

    [McpServerTool(Name = "add_control")]
    [Description("Places a control of the given type on a named form's designer canvas at the given position and size, and SAVES the form. x, y, width and height are in PIXELS, as the designer draws and get_form_controls reports: a .frm and VB6's Properties window use twips, 15 to a pixel, so VB6's default CommandButton (1215 × 495 twips) is 81 × 33 here. The reply's 'note' gives where the control landed, and says when it lies outside the form. The form must be open in the visual designer (call view_designer first if needed). The form is named in any loaded project, without regard to case; pass `project` when a group holds two documents of one name, and without it an ambiguous name is refused and the reply lists them. Returns the auto-generated control name (e.g. 'Command1'), with 'project' and 'document' naming the form as the IDE spells them. A form with no file yet saves through a native picker, which would stop this server answering, so it is refused before anything is placed unless answer_next_file_dialog has been armed first; arm it with no path to keep the form without a file. Two refusals come AFTER the control is placed, and each comes back as success:false naming the control, which is then in the designer but not on disk: HexIDE cannot reproduce the form faithfully, or the form's code page cannot hold a character in text written earlier and not yet saved. The second shows no dialog, and the reply names the document, the code page, the character and the line that holds it; once that text is changed, set_control_property or set_file_content writes the form, the new control included.")]
    public async Task<AddControlResult> AddControlAsync(
        string formName,
        [Description("A class as get_toolbox_items lists it, such as CommandButton or VB.CommandButton. An in-box control is offered only while the project references its library, which Project > Components sets, and once for each such library: its type name picks the library, as ComctlLib.ProgressBar does, and a bare ProgressBar takes the first.")] string type,
        [Description("Pixels from the form's left. Where x and y fall inside a Frame or PictureBox, the control goes into the innermost one, as one drawn from that point does, and is held within it; x and y are still measured from the form, and the reply gives its place inside the container.")] double x,
        double y, double width, double height,
        string? project = null,
        CancellationToken ct = default)
    {
        var spawned = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // The name resolves to a document first, in every loaded project, and only then to the designer
            // open on it, as move_control does and for the same reasons (#273).
            var named = FindDesigner(formName, project);
            if (named is not { Form: { } form, Document: { } document })
                return new AddControlResult(false, null, named.Error);

            var designer = DesignerOf(form);
            if (designer is null)
                return new AddControlResult(false, null,
                    $"{document.Display} is not open in the visual designer — call view_designer first",
                    Project: document.Project.Name, Document: document.Name);

            // A type name picks its button first, so ComctlLib.ProgressBar reaches COMCTL32.OCX's where both libraries are
            // referenced; a bare class name takes the first button of that class (#1103).
            var tools = designer.ToolsBoxToolViewModel.Components.Where(c => c.BaseClass is not null).ToList();
            var tool = tools.FirstOrDefault(c =>
                           string.Equals(c.VBTypeName, type, StringComparison.OrdinalIgnoreCase) ||
                           string.Equals("VB." + type, c.VBTypeName, StringComparison.OrdinalIgnoreCase))
                       ?? tools.FirstOrDefault(c => string.Equals(c.BaseClass!.Name, type, StringComparison.OrdinalIgnoreCase));
            var componentClass = tool?.BaseClass;

            if (componentClass is null)
            {
                var known = string.Join(", ", tools.Select(c => c.VBTypeName!.Replace("VB.", "")));
                // An in-box control is in the toolbox only while the project references its library, as in VB6.
                var inBox = HexIDE.Runtime.Components.InBox.InBoxComponentClass.Implemented.FirstOrDefault(c =>
                    string.Equals(c.ControlClassName, type, StringComparison.OrdinalIgnoreCase)
                    || c.Libraries.Any(l => string.Equals(l.TypeName(c.ControlClassName), type, StringComparison.OrdinalIgnoreCase)));
                var files = inBox?.Libraries.Select(l => l.File).ToList() ?? [];
                var why = inBox is null
                    ? ""
                    : $" {inBox.ControlClassName} comes from {string.Join(" or ", files)}, and {document.Project.Name} "
                      + (files.Count == 1 ? "doesn't reference it" : files.Count == 2 ? "references neither" : "references none of them")
                      + ": check one in Project > Components first, as in VB6.";
                return new AddControlResult(false, null,
                    $"Unknown control type '{type}'. Known types: {known}.{why}",
                    Project: document.Project.Name, Document: document.Name);
            }

            // REFUSED before anything is placed, rather than placed and then blocked. This tool saves the
            // form, and a form with no file saves through the native picker, which stops this server
            // answering: the call never returned and every call behind it hung. An armed answer makes the
            // save safe to attempt, including an armed cancel, which keeps the form without a file. (#514)
            //
            // A UserControl or PropertyPage is saved through its MODULE, which holds the .ctl/.pag path; the
            // designer half it draws on has no path of its own (#474). Asking about that half refused a
            // document that has a file, and saving through it would have written a stray .frm.
            var owner = document.Module;
            if (HexIDE.IDE.ScriptedFileDialogs.WouldShowPicker(owner?.AbsolutePath ?? form.AbsolutePath))
                return new AddControlResult(false, null,
                    $"{(owner is not null ? owner.Kind.ToString() : "Form")} '{document.Name}' {HexIDE.IDE.ScriptedFileDialogs.PickerRefusal}",
                    Project: document.Project.Name, Document: document.Name);

            // As a drag begun at its top-left: inside a Frame or PictureBox, it goes into the innermost one there (#2039).
            designer.SpawnControlAt(componentClass, new Avalonia.Rect(x, y, width, height), new Avalonia.Point(x, y), tool!.Library);
            // Where it landed, and whether that is on the form at all: twips passed as pixels put a control
            // fifteen times too far out, and the reply used to be plain success. (#675)
            var placed = designer.SelectedComponent;
            var note = placed is null
                ? null
                : $"{placed.Name} is at Left {PropertyText.Display(placed.RelativeLeft)}, Top {PropertyText.Display(placed.RelativeTop)}, "
                  + $"Width {PropertyText.Display(placed.Width)}, Height {PropertyText.Display(placed.Height)}, in pixels"
                  + (placed.Instance.Container is null or { BaseClass: FormComponentClass }
                      ? "."
                      : $". It is inside {placed.ContainerName}, as a control drawn there is, so Left and Top are measured from {placed.ContainerName}.")
                  + OutsideContainerNote(placed);
            return new AddControlResult(true, placed?.Name, null, form, note, document.Project.Name, document.Name);
        });

        if (!spawned.Success || spawned.Form is null)
            return spawned with { Form = null };

        // PERSISTED, because until this it was not: nine controls were added through this tool and all nine
        // were lost when the IDE crashed, with the .frm on disk still holding a bare form. Its sibling
        // set_control_property saved on every call, so the pair disagreed about whether a designer edit was
        // durable, and the one that did not save is the one that creates things.
        //
        // On the UI thread: a save first publishes ApplyAllUnsavedChangesEvent, whose handler reads
        // AvaloniaEdit's Document.Text and throws off it, leaving the previous content to be written and
        // reported as success. (#334)
        // Before the save: an armed cancel is the route add_control's description offers for keeping the form
        // without a file, so a save it cancels is that outcome, not a failed save (#755).
        var cancel = HexIDE.IDE.ScriptedFileDialogs.WatchForCancel(
            OwnerModuleOf(spawned.Form)?.AbsolutePath ?? spawned.Form.AbsolutePath);
        try
        {
            // Through the owning module for a UserControl or PropertyPage, as set_control_property does. (#539)
            var rewrites = new HexIDE.Projects.SaveRewriteReport();
            var (written, refused) = await SaveQuietlyAsync(() =>
                OwnerModuleOf(spawned.Form) is { } owner
                    ? ctx.ProjectService.SaveModule(owner, false, interactive: false, rewrites: rewrites)
                    : ctx.ProjectService.SaveForm(spawned.Form, false, interactive: false, rewrites: rewrites));
            // The character is in text written earlier and never saved, since a new control brings none of its
            // own: the message quotes the line that holds it, which no read tool might otherwise show.
            if (refused is not null)
                return spawned with
                {
                    Success = false, Form = null, Note = null,
                    Error = cancel.After($"'{spawned.ControlName}' was added to the designer, but the file was NOT written. {refused.Message} "
                            + "Once that text is changed, set_control_property or set_file_content writes the file, "
                            + $"'{spawned.ControlName}' included."),
                };

            // A refusal must not come back as success. (#147) The control is real and in the designer --
            // saying otherwise would be its own wrong answer -- but the file on disk does not have it, and
            // a caller who is told "saved" will not find out until something else reads that file.
            return written
                ? spawned with { Form = null, Note = cancel.After(spawned.Note + rewrites.Note) }
                : spawned with
                {
                    Success = false, Form = null, Note = null,
                    Error = cancel.After($"'{spawned.ControlName}' was added to the designer but NOT written: HexIDE cannot reproduce "
                            + "this form faithfully, so the copy on disk is unchanged. Undo the add or the designer "
                            + $"and the file will stay out of step."),
                };
        }
        catch (OperationCanceledException) when (cancel.Cancelled || cancel.Refused)
        {
            var kept = $"{spawned.Form.Name} still has no file: {cancel.HowAnswered}, so nothing "
                       + "was written. To write it, arm answer_next_file_dialog with a path and save the form, or make "
                       + "any other change that saves it, such as set_control_property.";
            return cancel.Refused
                ? spawned with { Success = false, Form = null, Note = null, Error = cancel.After($"'{spawned.ControlName}' was added to the designer, but {kept}") }
                : spawned with { Form = null, Note = spawned.Note is null ? kept : spawned.Note + " " + kept };
        }
        catch (Exception ex)
        {
            return spawned with
            {
                Success = false, Form = null, Note = null,
                Error = cancel.After($"'{spawned.ControlName}' was added to the designer but the save failed: {ToolFailures.WithoutProfile(ex.Message)}"),
            };
        }
    }

    /// <summary>A sentence for a control that no longer lies inside its container, or "" when it does.</summary>
    private static string OutsideContainerNote(HexIDE.VisualDesigner.ComponentInstanceViewModel control)
    {
        if (control.LiesWithinContainer) return "";
        var bounds = control.ContainerBounds;
        // Twips passed as pixels start a control far off its container. One that starts inside and only runs past an
        // edge is what VB6's own designer draws at a container's edge (#2039), so the twips hint would mislead there.
        var startsOutside = control.Left >= bounds.Right || control.Top >= bounds.Bottom
                            || control.Left + control.Width <= bounds.Left || control.Top + control.Height <= bounds.Top;
        return $" It lies at least partly outside {control.ContainerName}, whose client area is "
               + $"{PropertyText.Display(bounds.Width)} × {PropertyText.Display(bounds.Height)} pixels, so part or all "
               + "of it will not be seen at run time."
               + (startsOutside ? " If these numbers came from a .frm or from VB6, they are twips: divide by 15." : "");
    }

    /// <summary>
    /// Why a tool that needs a paused program cannot answer, and what gets the caller there. The two cases
    /// need different moves and the refusal used to name neither, though get_debug_state tells them apart. (#683)
    /// </summary>
    private string NotPausedRefusal(string what) =>
        ctx.ProjectRunnerService.IsRunning
            ? $"The project is running, not paused, so {what}. break_program pauses it where it is, or "
              + "set_breakpoints on a line it will reach and it stops there."
            : $"No project is running, so {what}. set_breakpoints on the line you want, then run_project; "
              + "step_into starts it and breaks on the first statement.";

    /// <summary>The UserControl or PropertyPage module whose designer half this is, or null for a form.</summary>
    private ModuleDefinition? OwnerModuleOf(FormDefinition? form) =>
        form is null
            ? null
            : ctx.ProjectManager.LoadedProjects.SelectMany(p => p.Modules).FirstOrDefault(m => m.FormPart == form);

    [McpServerTool(Name = "invoke_format_command")]
    [Description("Invokes a Format menu command on the active form designer, named by project and document in the reply, which lands as one undo step. " +
        "Commands: AlignLefts, AlignRights, AlignTops, AlignBottoms, AlignCentersH, AlignCentersV, " +
        "MakeSameWidth, MakeSameHeight, MakeSameSize, MakeHorizontalSpacingEqual, IncreaseHorizontalSpacing, " +
        "DecreaseHorizontalSpacing, RemoveHorizontalSpacing, MakeVerticalSpacingEqual, IncreaseVerticalSpacing, " +
        "DecreaseVerticalSpacing, RemoveVerticalSpacing, SizeToGrid, CenterHorizontally, CenterVertically. Align*, MakeSame* and spacing commands require 2 selected controls; SizeToGrid and Center* require 1. Below the minimum the reply refuses the command and explains how to select more with interact. The reply's 'note' says whether a step landed and names the selection it acted on; a command with nothing to act on changes nothing and says so.")]
    public async Task<MutateResult> InvokeFormatCommandAsync(string command, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ctx.DocumentDockService.ActiveDocument is not HexIDE.VisualDesigner.FormEditViewModel designer)
                return new MutateResult(false, "Active window is not a form designer");

            // One table for dispatch and for the refusal, so the names a refusal lists are the names that work.
            // The refusal used to send the caller back to the description, for a name one letter away. (#586)
            var entry = FormatCommands.FirstOrDefault(c => string.Equals(c.Name, command, StringComparison.OrdinalIgnoreCase));
            if (entry.Of is null)
            {
                var near = command.Length == 0 ? [] : FormatCommands
                    .Where(c => c.Name.StartsWith(command, StringComparison.OrdinalIgnoreCase)).Select(c => c.Name).ToList();
                return Naming(new MutateResult(false,
                    $"Unknown command '{command}'."
                    + (near.Count > 0 ? $" Did you mean {string.Join(" or ", near.Select(n => $"'{n}'"))}?" : "")
                    + $" Commands: {string.Join(", ", FormatCommands.Select(c => c.Name))}."), designer.OpenDocument);
            }

            var selected = designer.SelectedComponents.Count > 0
                ? designer.SelectedComponents.Select(c => c.Name).ToList()
                : designer.SelectedComponent is { } one ? [one.Name] : [];
            var selection = selected.Count == 0
                ? "no control is selected"
                : $"the selection is {string.Join(", ", selected)}"
                  + (designer.SelectedComponent is { } primary && selected.Count > 1 ? $", aligned to {primary.Name}" : "");
            var rule = $"'{entry.Name}' requires at least {entry.MinimumSelection} selected control"
                + (entry.MinimumSelection == 1 ? "" : "s");
            if (selected.Count < entry.MinimumSelection)
                return Naming(new MutateResult(false, $"{rule}; {selection}. Use interact with action 'select' on a control, "
                    + "then 'add_to_selection' on each additional control."), designer.OpenDocument);

            // A command with nothing to act on returns without a word, and so does one whose controls already
            // satisfy it, so whether a step landed is the only reliable sign that anything moved. (#655)
            var landed = false;
            void OnChanged() => landed = true;
            designer.UndoStack.Changed += OnChanged;
            try { entry.Of(designer)(); }
            finally { designer.UndoStack.Changed -= OnChanged; }

            return Naming(new MutateResult(true, null, landed
                ? $"Applied '{designer.UndoStack.UndoDescription}' as one undo step; {selection}."
                : $"Nothing changed, and no undo step was added: {selection}. {rule}; "
                  + "a selection that already satisfies the command is left as it is."), designer.OpenDocument);
        });
    }

    [McpServerTool(Name = "get_bookmarks")]
    [Description("Returns the bookmarked line numbers for a form or module, named by its own VB6 name in any loaded project (case does not matter). Lines are 1-based, as the gutter and every other line tool count them, and counted from the top of the file with its header included (a form's designer block, a class's header and the Attribute lines), as the code window numbers them. Pass `project` when a group holds two documents of one name; without it an ambiguous name is refused and the reply lists them. Empty array if none. A reply that lists lines gives them in its 'note' too, with their numbering. The reply's 'project' and 'document' name it as the IDE spells them, and 'uri' is the name a language server knows it by: its file: URI, or untitled:<project>/<name>.<ext> while it has no file, as get_diagnostics reports it.")]
    public async Task<BookmarksResult> GetBookmarksAsync(string name, string? project = null, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var lookup = ResolveDocument(name, project);
            if (lookup.Document is not { } document)
                return new BookmarksResult(null, null, null, [], lookup.Error);

            var lines = HexIDE.IDE.MarkLineRules.BookmarksShown(ctx.BookmarkService.GetBookmarks(document));
            // The numbering said in the reply, as set_bookmarks says it: a session still holding this tool's description
            // from before #1245 reads lines counted from 0, and nothing else here would set it right (#1261).
            return new BookmarksResult(document.Project.Name, document.Name, DocumentWireName.For(document), lines, null,
                HexIDE.IDE.MarkLineRules.BookmarksNote(document.Display, lines));
        });
    }

    [McpServerTool(Name = "set_bookmarks")]
    [Description("Replaces all bookmarks for a form or module with the supplied 1-based line numbers, as the gutter and every other line tool count them, counted from the top of the file with its header included (a form's designer block, a class's header and the Attribute lines), as the code window numbers them. Named by its own VB6 name in any loaded project (case does not matter); pass `project` to say which when a group holds two of one name. Pass an empty array to clear them. A line the document does not have is refused, and so is a new bookmark on a line of the header or of a member's Attribute lines, which never run; then nothing is changed, and the reply says where the code starts. The note reports what the document holds afterwards, and 'project' and 'document' name it as the IDE spells them.")]
    public async Task<MutateResult> SetBookmarksAsync(string name, int[] lines, string? project = null, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var lookup = ResolveDocument(name, project);
            if (lookup.Document is not { } document)
                return new MutateResult(false, lookup.Error);

            // Counted from 1 here and from 0 in the store (#571); MarkLineRules converts both ways.
            var (outside, toStore) = HexIDE.IDE.MarkLineRules.BookmarkRequest(
                HexIDE.IDE.DocumentContent.TextOf(document, CodeEditorOf(document)), document.Display, lines,
                ctx.BookmarkService.GetBookmarks(document));
            if (outside is not null)
                return Naming(new MutateResult(false, outside), document);

            ctx.BookmarkService.SetBookmarks(document, toStore);
            return Naming(new MutateResult(true, null, Held(document, HexIDE.IDE.MarkLineRules.BookmarksShown(ctx.BookmarkService.GetBookmarks(document)), "bookmark")), document);
        });
    }

    // ---- Debugger ----

    [McpServerTool(Name = "get_breakpoints")]
    [Description("Returns the breakpoint line numbers for a form or module, named by its own VB6 name in any loaded project (case does not matter). Lines are 1-based and counted from the top of the file with its header included (a form's designer block, a class's header and the Attribute lines), as the code window numbers them. Pass `project` when a group holds two documents of one name; without it an ambiguous name is refused and the reply lists them. Empty array if none. The reply's 'project' and 'document' name it as the IDE spells them, and 'uri' is the name a language server knows it by: its file: URI, or untitled:<project>/<name>.<ext> while it has no file, as get_diagnostics reports it.")]
    public async Task<BreakpointsResult> GetBreakpointsAsync(string name, string? project = null, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var lookup = ResolveDocument(name, project);
            if (lookup.Document is not { } document)
                return new BreakpointsResult(null, null, null, [], lookup.Error);

            return new BreakpointsResult(document.Project.Name, document.Name, DocumentWireName.For(document),
                [.. ctx.BreakpointService.GetBreakpoints(document)], null);
        });
    }

    [McpServerTool(Name = "set_breakpoints")]
    [Description("Replaces all breakpoints for a form or module with the supplied 1-based line numbers, counted from the top of the file with its header included (a form's designer block, a class's header and the Attribute lines), as the code window numbers them. Named by its own VB6 name in any loaded project (case does not matter); pass `project` to say which when a group holds two of one name. Pass an empty array to clear them. A line the document does not have is refused, and so is a new breakpoint on a line of the header or of a member's Attribute lines, which never run; then nothing is changed, and the reply says where the code starts. Takes effect immediately if that project is the one running. The note reports what the document holds afterwards, and 'project' and 'document' name it as the IDE spells them.")]
    public async Task<MutateResult> SetBreakpointsAsync(string name, int[] lines, string? project = null, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ctx.RootViewModel.ExternalDebugger?.IsActive == true)
                return new MutateResult(false, ctx.Localization.GetString("Str.Debug.External.Unsupported"));
            var lookup = ResolveDocument(name, project);
            if (lookup.Document is not { } document)
                return new MutateResult(false, lookup.Error);

            if (OutsideDocument(document, lines, first: 1, "breakpoint", ctx.BreakpointService.GetBreakpoints(document)) is { } outside)
                return Naming(new MutateResult(false, outside), document);

            ctx.BreakpointService.SetDocument(document, lines);
            return Naming(new MutateResult(true, null, Held(document, ctx.BreakpointService.GetBreakpoints(document), "breakpoint")), document);
        });
    }

    [McpServerTool(Name = "clear_all_breakpoints")]
    [Description("Removes every breakpoint the IDE holds, in EVERY loaded project rather than only the startup one. With a group open that is more than it sounds; to clear one document, call set_breakpoints with an empty array. The reply's 'note' says how many breakpoints were cleared, in how many documents.")]
    public async Task<MutateResult> ClearAllBreakpointsAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ctx.RootViewModel.ExternalDebugger?.IsActive == true)
                return new MutateResult(false, ctx.Localization.GetString("Str.Debug.External.Unsupported"));
            var before = ctx.BreakpointService.All();
            var lines = before.Values.Sum(l => l.Count);
            ctx.BreakpointService.ClearAll();
            return new MutateResult(true, null, lines == 0
                ? "There were no breakpoints to clear."
                : $"Cleared {lines} breakpoint{(lines == 1 ? "" : "s")} in {before.Count} document{(before.Count == 1 ? "" : "s")}.");
        });
    }

    [McpServerTool(Name = "break_program")]
    [Description("Pauses the running project at the next executed statement (VB6 Break / Ctrl+Break). Error if not running or already paused. The reply's 'note' gives where it paused (the module and the line, counted from the top of the file as the code window numbers it) or, when the program is waiting for an event, says it will pause at the next statement that runs.")]
    public async Task<MutateResult> BreakProgramAsync(CancellationToken ct)
    {
        var (refused, outcome) = await StartAndAwaitStopAsync(() =>
        {
            if (!ctx.ProjectRunnerService.CanBreakProject)
                return new MutateResult(false, ctx.ProjectRunnerService.IsRunning ? "Already paused" : "No project is running");
            ctx.ProjectRunnerService.BreakCurrentProject();
            return null;
        }, StepReplyWait, ct);
        if (refused is not null)
            return refused;

        return WithPrinted(new MutateResult(true, null, (outcome.Stop is { } at
            ? PausedAt(at)
            : outcome.Running
                ? "Break requested, and no statement has run since: the program is waiting for an event, and pauses "
                  + "at the next statement that runs."
                : "The program ended.") + RaisedNote(outcome)));
    }

    [McpServerTool(Name = "continue_program")]
    [Description("Resumes a paused project (VB6 Continue / F5 in break mode). Error if not currently paused. The reply's 'note' says, after up to 2 s, whether it paused again (with the module and the line, counted from the top of the file as the code window numbers it), is running, or ended.")]
    public async Task<MutateResult> ContinueProgramAsync(CancellationToken ct)
    {
        var (refused, outcome) = await StartAndAwaitStopAsync(() =>
        {
            if (!ctx.ProjectRunnerService.CanContinueProject)
                return new MutateResult(false, "Project is not paused");
            ctx.ProjectRunnerService.ContinueProject();
            return null;
        }, RunReplyWait, ct);
        if (refused is not null)
            return refused;

        return WithPrinted(new MutateResult(true, null, (outcome.Stop is { } at
            ? PausedAt(at)
            : outcome.Running ? $"Running; no pause within {RunReplyWait.TotalSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture)} s." : "The program ended.") + RaisedNote(outcome)));
    }

    /// <summary>How long a step's reply waits for the pause it causes before saying the program is still going.</summary>
    private static readonly TimeSpan StepReplyWait = TimeSpan.FromSeconds(2);

    /// <summary>How long a run or continue waits for a pause (a breakpoint, a Stop) before saying it is running.</summary>
    private static readonly TimeSpan RunReplyWait = TimeSpan.FromSeconds(2);

    /// <summary>
    /// What a run-control tool's start led to: the pause it caused, if one came within the wait; whether the
    /// program is still running; and a run-time error raised since, as its message reads on one line.
    /// </summary>
    private readonly record struct RunOutcome(StoppedInfo? Stop, bool Running, string? NewError);

    /// <summary>
    /// Runs <paramref name="startOnUi"/> on the UI thread and waits up to <paramref name="wait"/> for the pause it
    /// causes. The run-control tools replied before anything had happened, so a caller could not tell a run that
    /// hit a breakpoint from one that was running or one that had already failed (#655).
    /// </summary>
    /// <remarks>
    /// Subscribed to <c>Stopped</c> before the start, so a pause that lands at once is not missed; and it waits for
    /// THAT pause rather than reading the state, which straight after a step still shows the previous one.
    /// <paramref name="startOnUi"/> returns a refusal, or null once it has started something.
    /// </remarks>
    private async Task<(MutateResult? Refused, RunOutcome Outcome)> StartAndAwaitStopAsync(
        Func<MutateResult?> startOnUi, TimeSpan wait, CancellationToken ct)
    {
        var stopped = new TaskCompletionSource<StoppedInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStopped(StoppedInfo info) => stopped.TrySetResult(info);
        int? errorBefore = null;

        var refused = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ctx.RootViewModel.ExternalDebugger?.IsActive == true)
                return new MutateResult(false, ctx.Localization.GetString("Str.Debug.External.Unsupported"));
            errorBefore = ctx.RootViewModel.RuntimeErrors.Last?.Sequence;
            ctx.DebugController.Stopped += OnStopped;
            var refusal = startOnUi();
            if (refusal is not null)
                ctx.DebugController.Stopped -= OnStopped;
            return refusal;
        });
        if (refused is not null)
            return (refused, default);

        try
        {
            StoppedInfo? stop = await Task.WhenAny(stopped.Task, Task.Delay(wait, ct)) == stopped.Task
                ? await stopped.Task
                : null;
            var (running, error) = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var last = ctx.RootViewModel.RuntimeErrors.Last;
                return (ctx.ProjectRunnerService.IsRunning,
                        last is { } raised && raised.Sequence != errorBefore
                            ? System.Text.RegularExpressions.Regex.Replace(raised.Message, @"\s+", " ").Trim()
                              + (raised.Where is { } where ? $" (in {where.Module}, line {where.Line})" : "")
                            : null);
            });
            return (null, new RunOutcome(stop, running, error));
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => ctx.DebugController.Stopped -= OnStopped);
        }
    }

    private string PausedAt(StoppedInfo at) => $"Paused at {ctx.ProjectRunnerService.RunningProject?.Name}/{at.Module} line {at.Line} ({at.Reason}).";

    /// <summary>A successful run, step, break or stop reply with what the program printed since its run began or a reply
    /// last reported it, in 'printed', and its note saying how much or that there was none (#2531).</summary>
    private static MutateResult WithPrinted(MutateResult result)
    {
        var (printed, note) = ProgramPrinted.Report(ProgramPrinted.Shared.Take());
        return result with { Note = result.Note + note, Printed = printed };
    }

    private static string RaisedNote(RunOutcome outcome) =>
        outcome.NewError is { } error ? $" It raised {error}; get_last_runtime_error has it." : "";

    /// <summary>Starts a step and replies with where it paused.</summary>
    /// <remarks>
    /// A statement that takes longer than <see cref="StepReplyWait"/> is reported as still running, which is also
    /// what stepping out of an outermost event handler looks like: the program then waits for the next event.
    /// </remarks>
    private async Task<MutateResult> StepAndReportAsync(Func<bool> can, Action start, CancellationToken ct)
    {
        var (refused, outcome) = await StartAndAwaitStopAsync(
            () => can() ? StartFailure(start) : new MutateResult(false, "No project to step — load a project first"),
            StepReplyWait, ct);
        if (refused is not null)
            return refused;

        return WithPrinted(new MutateResult(true, null, (outcome.Stop is { } at
            ? PausedAt(at)
            : outcome.Running
                ? $"Still running {StepReplyWait.TotalSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture)} s after the step began: the statement has not "
                  + "finished, or the program is waiting for its next event. get_debug_state says where it breaks."
                : "The program ended before it reached another statement.") + RaisedNote(outcome)));
    }

    [McpServerTool(Name = "step_into")]
    [Description("Step Into (F8): from idle, starts the project and breaks at the first executed statement; while paused, executes the next statement and breaks (descending into any called Sub/Function); while running, breaks at the next statement. The reply's 'note' says where it paused (module, line counted from the top of the file as the code window numbers it, and reason) or, when no pause comes within 2 s, whether the program is still running or has ended.")]
    public Task<MutateResult> StepIntoAsync(CancellationToken ct) =>
        StepAndReportAsync(() => ctx.ProjectRunnerService.CanStepIntoProject, () => ctx.ProjectRunnerService.StepIntoProject(), ct);

    [McpServerTool(Name = "step_over")]
    [Description("Step Over (Shift+F8): while paused, executes the next statement and breaks in the SAME frame — a called Sub/Function runs to completion without descending (unlike step_into). On a non-call statement it behaves like step_into. From idle, starts the project and breaks at the first statement. The reply's 'note' says where it paused (module, line counted from the top of the file as the code window numbers it, and reason) or, when no pause comes within 2 s, whether the program is still running or has ended.")]
    public Task<MutateResult> StepOverAsync(CancellationToken ct) =>
        StepAndReportAsync(() => ctx.ProjectRunnerService.CanStepOverProject, () => ctx.ProjectRunnerService.StepOverProject(), ct);

    [McpServerTool(Name = "step_out")]
    [Description("Step Out (Ctrl+Shift+F8): while paused, runs the rest of the current procedure and breaks at the statement in the CALLER after it returns. Stepping out of the outermost frame runs that event/procedure to completion. From idle it starts the project (like Step Into); while running it requests a pause. The reply's 'note' says where it paused (module, line counted from the top of the file as the code window numbers it, and reason) or, when no pause comes within 2 s, whether the program is still running or has ended.")]
    public Task<MutateResult> StepOutAsync(CancellationToken ct) =>
        StepAndReportAsync(() => ctx.ProjectRunnerService.CanStepOutProject, () => ctx.ProjectRunnerService.StepOutProject(), ct);

    [McpServerTool(Name = "run_to_cursor")]
    [Description("Run To Cursor (Ctrl+F8): run until (module, 1-based line, counted from the top of the file with its header included (a form's designer block, a class's header and the Attribute lines), as the code window numbers them) then break — a one-shot temporary breakpoint. While paused it continues to the target; while running it arms the target; from idle it starts the project and runs to the target (a real breakpoint hit first stays paused there; continue proceeds toward the target). The reply's 'note' says where it paused or, within 2 s, that it has not got there yet, and 'project' and 'document' name the target's document as the IDE spells them. Pass `project` when a group holds two documents of one name; without it an ambiguous name is refused and the reply lists them.")]
    public async Task<MutateResult> RunToCursorAsync(string module, int line, string? project = null, CancellationToken ct = default)
    {
        string? target = null;
        DocumentIdentity? named = null;
        var (refused, outcome) = await StartAndAwaitStopAsync(() =>
        {
            if (!ctx.ProjectRunnerService.CanRunToCursor)
                return new MutateResult(false, "No project to run — load a project first");

            var lookup = ResolveDocument(module, project);
            if (lookup.Document is not { } document)
                return new MutateResult(false, lookup.Error);
            named = document;

            // A line the module does not have can never be reached, so the run would be a plain run under a
            // note naming a target that does not exist. Refused before anything starts, as set_breakpoints
            // refuses one (#569, #591).
            if (OutsideDocument(document, [line], first: 1, "line") is { } outside)
                return Naming(new MutateResult(false, outside), document);

            target = $"{document.Display} line {line}";
            return StartFailure(() => ctx.ProjectRunnerService.RunToCursorProject(document, line));
        }, StepReplyWait, ct);
        if (refused is not null)
            return Naming(refused, named);

        return WithPrinted(Naming(new MutateResult(true, null, (outcome.Stop is { } at
            ? PausedAt(at)
            : outcome.Running
                ? $"Running to {target}; not reached within {StepReplyWait.TotalSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture)} s. get_debug_state says when it is."
                : $"The program ended without reaching {target}.") + RaisedNote(outcome)), named));
    }

    [McpServerTool(Name = "set_next_statement")]
    [Description("Set Next Statement (Ctrl+F9): move the execution point to (module, 1-based line, counted from the top of the file with its header included (a form's designer block, a class's header and the Attribute lines), as the code window numbers them) WITHOUT running the statements in between — the next step_into/continue executes from there. Only while paused, and only to a TOP-LEVEL statement of the currently paused procedure (a target nested inside an If/For/Do/Select block, or a move while paused inside such a block, is refused — a tree-walker limit, not VB6's). Returns an error result if refused. The reply's 'note' names the new next statement, and 'project' and 'document' name its document as the IDE spells them. Pass `project` when a group holds two documents of one name; without it an ambiguous name is refused and the reply lists them.")]
    public async Task<MutateResult> SetNextStatementAsync(string module, int line, string? project = null, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var lookup = ResolveDocument(module, project);
            if (lookup.Document is not { } document)
                return new MutateResult(false, lookup.Error);
            if (OutsideDocument(document, [line], first: 1, "line") is { } outside)
                return Naming(new MutateResult(false, outside), document);

            // Scoped to the run, because the controller is told a bare name and a group's other project may
            // hold a module called the same thing — which would move the execution point of the running
            // project to a line of a document that is not executing.
            if (ctx.ProjectRunnerService.RunningProject is not { } running)
                return Naming(new MutateResult(false, "No project is running"), document);
            if (!document.IsIn(running))
                return Naming(new MutateResult(false,
                    $"{document.Display} is not in the running project ('{running.Name}')"), document);

            return Naming(ctx.DebugController.SetNextStatement(document.Name, line)
                ? new MutateResult(true, null, $"Next statement is {document.Display} line {line}.")
                : new MutateResult(false, "Refused — must be paused, and the target must be a top-level statement of the paused procedure (not nested in a block)"), document);
        });
    }

    [McpServerTool(Name = "get_debug_state")]
    [DescribesEnum(typeof(DebugState))]
    [DescribesEnum(typeof(StopReason))]
    [Description("Returns the interpreter debug state: whether a project is running, the controller state (Running/Paused/Stopped), and — when paused — the break location (module, 1-based line counted from the top of the file, header included, as the code window and set_breakpoints number it, and 'project', the running project the module is in) and reason. 'stopReason' is Breakpoint (a breakpoint, or the line Run To Cursor was aiming for; a breakpoint wins even mid-step), Watch (a Break When True or Break When Changed watch fired), Step (a step completed), StopStatement (a VB6 Stop statement ran), Assertion (a Debug.Assert whose condition did not hold), Pause (a break was requested, as by break_program) or RuntimeError (a run-time error nothing handled raised the run-time error dialog, Debug was chosen there, and the break is on the statement that raised it, which runs again on continue_program); it is null unless Paused.")]
    public async Task<DebugStateResult> GetDebugStateAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ctx.RootViewModel.ExternalDebugger is { IsActive: true } external)
            {
                var frame = external.Frames.FirstOrDefault();
                return new DebugStateResult(true, external.State == HexIDE.Dap.DapSessionState.Stopped ? "Paused" : external.State.ToString(),
                    external.Stop?.Reason, frame.ValueKind == System.Text.Json.JsonValueKind.Object && frame.TryGetProperty("source", out var source) && source.TryGetProperty("path", out var path) ? path.GetString() : null,
                    frame.ValueKind == System.Text.Json.JsonValueKind.Object ? frame.GetProperty("line").GetInt32() : null);
            }
            var stop = ctx.DebugController.CurrentStop;
            // A controller that has no session reads Running until its first run ends: it starts there so the
            // headless interpreter path needs no Reset(). That is the controller's business, not the IDE's, and
            // reporting it gave {"running":false,"state":"Running"} on every freshly launched IDE (#590).
            var state = ctx.DebugController.IsSessionActive ? ctx.DebugController.State : DebugState.Stopped;
            return new DebugStateResult(
                ctx.ProjectRunnerService.IsRunning,
                state.ToString(),
                stop?.Reason.ToString(),
                stop?.Module,
                stop?.Line,
                // The module is a bare name, and a group may hold two of it: the running project says which.
                stop is null ? null : ctx.ProjectRunnerService.RunningProject?.Name);
        });
    }

    [McpServerTool(Name = "get_locals")]
    [Description("Returns the paused frame's Locals as a tree (Expression/Value/Type), depth-capped. Valid only while paused (get_debug_state.state == Paused) — otherwise Success is false. 'project' is the running project holding the context's module. 'context' is the Module.Procedure header; each row has hasChildren and, down to `maxDepth`, nested children (arrays/UDTs/objects expand; a class instance's Me/fields appear under a Me/module root).")]
    public async Task<LocalsResult> GetLocalsAsync(int maxDepth = 3, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ctx.RootViewModel.ExternalDebugger?.IsActive == true)
                return new LocalsResult(false, ctx.Localization.GetString("Str.Debug.External.Unsupported"), null, null);
            var scope = ctx.DebugController.GetLocals();
            if (scope is null)
                return new LocalsResult(false, NotPausedRefusal("there are no locals to read"), null, null);
            int cap = Math.Clamp(maxDepth, 1, 8);
            int[] budget = { MaxLocalsNodes };   // total-node budget across the whole eager projection
            var rows = scope.Locals.Select(n => MapLocalsNode(n, cap, 1, budget)).ToArray();
            return new LocalsResult(true, null, scope.Context, rows, ctx.ProjectRunnerService.RunningProject?.Name);
        });
    }

    // Bound the eager depth-projection: even with the per-array element cap + depth cap, a pathologically wide
    // nested tree could realize a lot of nodes on the UI thread. Stop after this many.
    private const int MaxLocalsNodes = 5000;

    // Depth-bounded projection of the lazy DebugNode tree into serializable rows. Children below max_depth are
    // omitted (hasChildren still signals they exist); a truncated array tail becomes a "… N more" row.
    private static LocalsRow MapLocalsNode(DebugNode node, int maxDepth, int depth, int[] budget)
    {
        LocalsRow[]? children = null;
        if (node.HasChildren && depth < maxDepth && budget[0] > 0)
        {
            var kids = new List<LocalsRow>();
            foreach (var c in node.Expand())
            {
                if (budget[0]-- <= 0)
                    break;
                kids.Add(MapLocalsNode(c, maxDepth, depth + 1, budget));
            }
            children = kids.ToArray();
        }
        string expression = node.TruncatedRemaining > 0 ? $"… {node.TruncatedRemaining} more" : node.Name;
        return new LocalsRow(expression, node.Value, node.TypeName, node.HasChildren, children);
    }

    [McpServerTool(Name = "get_call_stack")]
    [Description("Returns the call stack at the current break — the chain of running procedure activations, current/deepest frame first, each with proc, module, and 1-based line counted from the top of the file, header included, as the code window and set_breakpoints number it. 'project' is the running project, which holds every frame's module. Valid only while paused (get_debug_state.state == Paused) — otherwise Success is false with an empty list.")]
    public async Task<CallStackResult> GetCallStackAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ctx.RootViewModel.ExternalDebugger?.IsActive == true)
                return new CallStackResult(false, ctx.Localization.GetString("Str.Debug.External.Unsupported"), []);
            if (ctx.DebugController.State != HexIDE.Runtime.Debugging.DebugState.Paused)
                return new CallStackResult(
                    false, NotPausedRefusal("there is no call stack to read"), System.Array.Empty<CallStackFrameRow>());
            var frames = ctx.DebugController.GetCallStack()
                .Select(f => new CallStackFrameRow(f.ProcName, f.Module, f.Line))
                .ToArray();
            return new CallStackResult(true, null, frames, ctx.ProjectRunnerService.RunningProject?.Name);
        });
    }

    [McpServerTool(Name = "add_watch")]
    [DescribesEnum(typeof(HexIDE.Debugging.WatchType))]
    [Description("Adds a watch expression to the Watches window. `watchType` is one of Expression (default; display the value), BreakWhenTrue (breaks at each statement while the expression is true), or BreakWhenChanged (breaks when its value changes); all three show their value while paused. Matched without regard to case. Any other value is refused and nothing is added, rather than quietly becoming an Expression watch. Returns the full watch list, after adding or not. 'project' names the running project in which expressions are evaluated; context is the stored Module.Procedure header, or (All Procedures).")]
    public async Task<WatchesResult> AddWatchAsync(string expression, string? watchType = null, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (ctx.RootViewModel.ExternalDebugger?.IsActive == true)
                return new WatchesResult(false, ctx.Localization.GetString("Str.Debug.External.Unsupported"), []);
            // An unrecognised value is REFUSED. It used to become Expression, so a misspelt BreakWhenTrue gave a
            // watch that never breaks, reported as though the call had done what was asked. (#546) Null and
            // empty still mean the documented default.
            HexIDE.Debugging.WatchType? type = watchType?.Trim().ToLowerInvariant() switch
            {
                null or "" or "expression"                               => HexIDE.Debugging.WatchType.Expression,
                "breakwhentrue" or "break_when_true" or "true"          => HexIDE.Debugging.WatchType.BreakWhenTrue,
                "breakwhenchanged" or "break_when_changed" or "changed" => HexIDE.Debugging.WatchType.BreakWhenChanged,
                _ => null,
            };
            if (type is not { } watchKind)
                return (await BuildWatchesResult()) with
                {
                    Success = false,
                    Error = $"'{watchType}' is not a watch type, so no watch was added. Use Expression (the default), " +
                            "BreakWhenTrue or BreakWhenChanged.",
                };

            var context = ctx.DebugController.GetLocals()?.Context ?? HexIDE.Debugging.WatchExpression.AllProceduresContext;
            ctx.RootViewModel.Watches.Service.Add(new HexIDE.Debugging.WatchExpression(expression, watchKind, context));
            return await BuildWatchesResult();
        });
    }

    [McpServerTool(Name = "get_watches")]
    [DescribesEnum(typeof(HexIDE.Debugging.WatchType))]
    [Description("Returns every watch (expression, watch type, stored Module.Procedure context or (All Procedures)) and its current value/type. 'project' names the running project in which expressions are evaluated, absent when no project is running. Values are live only while paused (get_debug_state.state == Paused); otherwise value is '<Out of context>'. 'watchType' is Expression (display only), BreakWhenTrue or BreakWhenChanged, matching the three choices in the Add Watch dialog.")]
    public async Task<WatchesResult> GetWatchesAsync(CancellationToken ct = default)
        => await Dispatcher.UIThread.InvokeAsync(BuildWatchesResult);

    // Snapshot the watch list, evaluating each against the paused frame (live only while Paused).
    private async Task<WatchesResult> BuildWatchesResult()
    {
        if (ctx.RootViewModel.ExternalDebugger?.IsActive == true)
            return new WatchesResult(false, ctx.Localization.GetString("Str.Debug.External.Unsupported"), []);
        var rows = new List<WatchRow>();
        foreach (var w in ctx.RootViewModel.Watches.Service.Watches)
        {
            var result = await ctx.DebugController.EvaluateScopedWatchAsync(w.Expression, w.Context);
            rows.Add(new WatchRow(
                w.Expression, w.Type.ToString(), w.Context,
                result?.Display ?? "<Out of context>", result?.TypeName ?? "", result?.Ok ?? false));
        }
        return new WatchesResult(true, null, rows.ToArray(), ctx.ProjectRunnerService.RunningProject?.Name);
    }

    [McpServerTool(Name = "evaluate")]
    [Description("Runs an Immediate-window line against the paused frame. A leading ?/Print/Debug.Print EVALUATES and returns what the VB6 Immediate window prints for it, Print formatting and all: \"?count\" gives \" 42 \", a number with a space where its sign would go and one after, and \"?a; b\" or \"?a, b\" prints the list as Debug.Print does. A bare expression returns the value alone (\"count\" gives \"42\"); use it, or add_watch and get_watches, to compare values. A BARE assignment or Set (e.g. \"count = 7\", \"Set obj = Nothing\") is EXECUTED and mutates the paused frame (returns empty) — whereas \"?count = 7\" compares. User Sub/Function calls are still rejected (they would deadlock the paused gate). Valid only while paused (get_debug_state.state == Paused). Returns the formatted result / empty (for a statement) / a VB6-style error message.")]
    public async Task<EvaluateResult> EvaluateAsync(string expression, CancellationToken ct = default)
    {
        if (await Dispatcher.UIThread.InvokeAsync(() => ctx.RootViewModel.ExternalDebugger?.IsActive == true))
            return new EvaluateResult(false, ctx.Localization.GetString("Str.Debug.External.Unsupported"), null);
        string? result = await Dispatcher.UIThread.InvokeAsync(() => ctx.DebugController.EvaluateAsync(expression));
        return result is null
            ? new EvaluateResult(false, NotPausedRefusal("there is no frame to evaluate against"), null)
            : new EvaluateResult(true, null, result);
    }

    [McpServerTool(Name = "get_toolbox_items")]
    [Description("Returns the names of all controls currently in the Toolbox, in order, each with the type name a control placed from it takes. Includes both built-in controls and any add-in registered controls. An in-box control, such as the ProgressBar, is there once for each of its libraries the project references, which Project > Components sets: MSComctlLib.ProgressBar from MSCOMCTL.OCX, ComctlLib.ProgressBar from COMCTL32.OCX.")]
    public async Task<ToolboxItemsResult> GetToolboxItemsAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var items = ctx.ToolBoxViewModel.Components
                .Select(c => new ToolboxItem(c.Name, c.VBTypeName))
                .ToArray();
            return new ToolboxItemsResult(items);
        });
    }

    [McpServerTool(Name = "get_new_project_templates")]
    [Description("Returns the list of project templates that would appear in the New Project dialog, including personality templates and any add-in registered templates.")]
    public Task<NewProjectTemplatesResult> GetNewProjectTemplatesAsync(CancellationToken ct)
    {
        var personality = ctx.PersonalityService.AvailableProjectTypes
            .Select(t => new TemplateInfo(t.Name, t.Supported, "personality"))
            .ToArray();
        var addin = ctx.AddinProjectTemplateService.Templates
            .Select(t => new TemplateInfo(t.Name, t.Supported, "addin"))
            .ToArray();
        return Task.FromResult(new NewProjectTemplatesResult([.. personality, .. addin]));
    }

    [McpServerTool(Name = "shutdown_ide")]
    [Description("Shuts down the HexIDE application cleanly, triggering all shutdown handlers. " +
                 "Stops a running project and closes any dialogs still open first — otherwise the app " +
                 "keeps running (its shutdown mode is last-window-close) and the next build fails on a " +
                 "file lock. The reply says what was torn down; it CANNOT confirm the process exited, " +
                 "because the reply has to be sent before it does — poll /health until it stops " +
                 "answering for that. " +
                 "`force` (default true) skips the save-changes prompt and DISCARDS unsaved edits — " +
                 "without it that prompt would block the shutdown and wedge automation. " +
                 "Pass force=false to get exactly what a user closing the window sees, including the " +
                 "prompt; the IDE then stays up if the user cancels.")]
    public async Task<ShutdownResult> ShutdownIdeAsync(bool force = true, CancellationToken ct = default)
    {
        var response = httpContextAccessor?.HttpContext?.Response
            ?? throw new InvalidOperationException("Shutdown requires an HTTP response to finish before closing the IDE.");

        // Tear down synchronously so the reply says what happened. The main window stays open until the
        // response completes: merely posting its close can still end the process before the reply is sent.
        var (projectStopped, dialogsClosed) = await Dispatcher.UIThread.InvokeAsync(() =>
            IdeShutdown.Prepare(ctx, force));

        response.OnCompleted(() =>
        {
            Dispatcher.UIThread.Post(IdeShutdown.CloseMainWindow);
            return Task.CompletedTask;
        });

        return new ShutdownResult(true, projectStopped, dialogsClosed,
            "Shutdown requested. Poll /health until it stops answering to confirm the process exited.");
    }

    private static readonly (string Name, int MinimumSelection, Func<HexIDE.VisualDesigner.FormEditViewModel, Action> Of)[] FormatCommands =
    [
        ("AlignLefts", 2, d => d.AlignLefts),
        ("AlignRights", 2, d => d.AlignRights),
        ("AlignTops", 2, d => d.AlignTops),
        ("AlignBottoms", 2, d => d.AlignBottoms),
        ("AlignCentersH", 2, d => d.AlignCentersH),
        ("AlignCentersV", 2, d => d.AlignCentersV),
        ("MakeSameWidth", 2, d => d.MakeSameWidth),
        ("MakeSameHeight", 2, d => d.MakeSameHeight),
        ("MakeSameSize", 2, d => d.MakeSameSize),
        ("MakeHorizontalSpacingEqual", 2, d => d.MakeHorizontalSpacingEqual),
        ("IncreaseHorizontalSpacing", 2, d => d.IncreaseHorizontalSpacing),
        ("DecreaseHorizontalSpacing", 2, d => d.DecreaseHorizontalSpacing),
        ("RemoveHorizontalSpacing", 2, d => d.RemoveHorizontalSpacing),
        ("MakeVerticalSpacingEqual", 2, d => d.MakeVerticalSpacingEqual),
        ("IncreaseVerticalSpacing", 2, d => d.IncreaseVerticalSpacing),
        ("DecreaseVerticalSpacing", 2, d => d.DecreaseVerticalSpacing),
        ("RemoveVerticalSpacing", 2, d => d.RemoveVerticalSpacing),
        ("SizeToGrid", 1, d => d.SizeToGrid),
        ("CenterHorizontally", 1, d => d.CenterHorizontally),
        ("CenterVertically", 1, d => d.CenterVertically),
    ];

    // Named as LocalizationService names them when developer mode lists them.
    private static readonly (string Id, string? DisplayName)[] PseudoLanguages =
        [("pseudo", "Pseudo (LTR)"), ("pseudo-rtl", "Pseudo (RTL)")];

    [McpServerTool(Name = "set_ide_language")]
    [Description("Switches the IDE chrome language by pack id ('en', 'pseudo', 'pseudo-rtl', or an installed pack id), driving the exact live-apply + countdown-revert confirmation gate the Options dropdown uses. Returns immediately; the gate stays open and auto-reverts after its countdown. Keep on the gate also saves the language for later starts, as OK in Options does; Revert or the countdown saves nothing. Asking for the language already in use opens no gate and saves it. While a gate is open, another call is refused and names the language it is waiting on. Call take_snapshot right after to capture the gate, or wait for it to time out to see the reverted chrome.")]
    public async Task<MutateResult> SetIdeLanguageAsync(string id, CancellationToken ct)
    {
        // Checked first against what the Language page offers, languages and their regions. An unknown id used
        // to be applied anyway, which falls back to English, and answered success: the caller asked for one
        // language, got another, and was told it had worked. (#585)
        //
        // The two pseudo-locales are accepted whether or not the Language page lists them. It lists them only
        // in developer mode, but switching to one is how unkeyed strings are found (CLAUDE.md, localization
        // step 5), and this server is DEBUG-only: refusing them outside developer mode broke that loop.
        var languages = ctx.LanguageSwitch.AvailableLanguages;
        var ids = languages.Select(l => l.Id).Concat(PseudoLanguages.Select(p => p.Id))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var known = ids
            .Concat(languages.SelectMany(l => ctx.LanguageSwitch.RegionsFor(l.Id).Select(r => r.Id)));
        var match = known.FirstOrDefault(k => string.Equals(k, id, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return new MutateResult(false,
                $"No language pack '{id}'. Languages: {string.Join(", ", ids)}. "
                + "A region of one of them, such as 'fr-CA', is accepted too.");

        // Checked and started in one step on the UI thread, so nothing can open a gate in between. The switch
        // marks its gate pending before its first await, then returns here at once, leaving the modal gate
        // open for take_snapshot to capture (awaiting it would block until the gate resolved).
        var (pending, previous) = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ctx.LanguageSwitch.PendingLanguage is { } open) return (open, (string?)null);
            var before = ctx.LanguageSwitch.ActiveLanguage;
            _ = SaveIfKeptAsync(ctx.LanguageSwitch.SwitchWithGateAsync(match), match);
            return ((string?)null, before);
        });
        if (pending is not null)
            return new MutateResult(false,
                $"The confirmation gate for '{pending}' is still open, so nothing was changed. A second gate would "
                + "revert to '" + pending + "', which nobody confirmed. Answer the open one with interact (find its "
                + "Keep and Revert buttons with dump_visual_tree), or wait for its countdown, then call again.");

        var shown = languages.FirstOrDefault(l => l.Id == match)?.DisplayName
                    ?? PseudoLanguages.FirstOrDefault(p => p.Id == match).DisplayName
                    ?? languages.Select(l => (Language: l, Region: ctx.LanguageSwitch.RegionsFor(l.Id).FirstOrDefault(r => r.Id == match)))
                        .Where(x => x.Region is not null)
                        .Select(x => $"{x.Language.DisplayName} ({x.Region!.DisplayName})")
                        .FirstOrDefault()
                    ?? match;
        if (string.Equals(previous, match, StringComparison.OrdinalIgnoreCase))
            return new MutateResult(true, null,
                $"{shown} ('{match}') is already the IDE's language, so no confirmation gate was opened. It is saved "
                + "for later starts.");
        return new MutateResult(true, null,
            $"Switching to {shown} ('{match}'). A confirmation gate is open: Keep saves {shown} for later starts, as "
            + $"OK in Options does, and Revert or its countdown goes back to '{previous}' and saves nothing. "
            + "take_snapshot captures it. Another switch is refused until it closes.");
    }

    // The Options page saves its language only when the dialog closes with OK, so a gate this tool opens, which no
    // Options dialog stands behind, kept its language for the session and lost it at the next start (#2268). Saved
    // here and not in the switch service, because there Cancel in Options still takes back a kept language.
    private async Task SaveIfKeptAsync(Task<bool> switching, string id)
    {
        try
        {
            if (!await switching) return;
            ctx.Settings.ActiveLanguage = id;
            ctx.Settings.Save();
        }
        catch (Exception e)
        {
            Serilog.Log.Warning(e, "set_ide_language: '{Id}' was kept but could not be saved", id);
        }
    }

    [McpServerTool(Name = "take_snapshot")]
    [Description("Captures the window's client area, without its title bar or frame, as a PNG, or with 'includeFrame' the whole window as Windows renders it, even where another window covers it. 'captureArea' says which in the reply; 'title' is the actual window title, including the IDE's. 'activeDialog' is retained for compatibility (null for the IDE). Returns the file path so the caller can read the image, with its size in pixels ('width', 'height') and its 'scale', the pixels per device-independent unit. Open popups can extend the image left or above the window: 'windowOffsetX' and 'windowOffsetY' give the window origin in full-image pixels (otherwise zero). For inspect_element's window-relative 'boundingRect', image x = x * scale + windowOffsetX, likewise y. For the inline copy, x = (x + windowOffsetX / scale) * inlineScale, likewise y. If any other window is open (a dialog, the Find window, or a running program's form) it is captured in preference to the IDE's own, whether or not it is on top; otherwise the main window is captured. 'window' selects which top-level window to address, as its description says: under \"auto\" (default) a running program's form, INCLUDING while it is paused at a breakpoint, is usually picked over the IDE; pass \"ide\" for the IDE itself, or a window's title or a running form's name for that window. Each call writes a new file and returns its path; earlier captures are left in place, so a path from an earlier call still holds that image, and nothing deletes them. The path is on the IDE's machine: a client attached from another machine, through a tunnel, passes 'inline' true to get the image itself as well, in a copy fitted to 1568 pixels described by 'inlineWidth', 'inlineHeight' and 'inlineScale'. A popup reported on no screen, or so far from the window that the image would pass 16,384 × 16,384 pixels' worth, is left out, and the 'note' says what it held, where it was and why. The image is the compositor's, as on screen; should it give none within 5 s, a render target draws it and the 'note' says so.")]
    public async Task<SnapshotResult> TakeSnapshotAsync(
        [Description(WindowParameter)] string? window = null,
        // The tool holds an inline copy, which InlineSnapshot attaches after the reply (#957).
        [Description("True to return the PNG itself as image content after the reply, as well as its path, for a client "
                     + "that cannot open a file on the IDE's machine. The copy is fitted to 1568 pixels on its longest side, and "
                     + "'inlineWidth', 'inlineHeight' and 'inlineScale', its pixels per device-independent unit, describe it. "
                     + "Default false: an image costs a few thousand tokens of context.")]
        bool inline = false,
        [Description("'includeFrame' true captures the whole window, title bar and frame included, as Windows renders it, "
                     + "even where another window covers it, with its open popups drawn over it. Windows only; refused, with no "
                     + "client-only fallback, for a window hidden or minimized. Should Windows render nothing, the frame is "
                     + "copied from the screen and the 'note' says how much of it other windows cover, since that part shows "
                     + "them. Default false renders the client area and its popups, even if occluded, and refuses a minimized window, whose last frame is stale.")]
        bool includeFrame = false,
        CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // Through the shared resolver, so 'window' means the same thing here as in every other tool.
            // Any other window, a dialog, Find or a running form, is captured in preference to the IDE's own (#1264),
            // whether or not it is on top; see ForegroundWindow.Pick for which wins among several.
            var (active, label, error) = ResolveActiveWindow(window);
            if (active is null)
                return new SnapshotResult(null, error, null, CaptureArea: null);

            var activeDialog = label == "MainWindow" ? null : label;

            // A dropped-down menu, a combo's list, a flyout: each is realised in its own top-level root,
            // so rendering the window alone gives a menu bar with no menu. The composer draws them in
            // their real positions — gap 12 in docs/mcp-server-gaps.md.
            // Drawn by the compositor, as the screen is: a render target above 100% scaling left out text drawn after a
            // popped clip (#2293). With includeFrame, Windows draws the window, frame and all, even where another window covers
            // it: a copy of the screen showed whatever was on top, with the reply of a clean capture (#2182).
            HexIDE.Automation.SnapshotComposer.Snapshot? snapshot;
            if (includeFrame)
            {
                (snapshot, var frameError) = await HexIDE.Automation.SnapshotComposer.CaptureFrameAsync(active);
                if (snapshot is null)
                    return new SnapshotResult(null, frameError, activeDialog, Title: active.Title, CaptureArea: null);
            }
            // A minimized window is refused as the frame capture refuses it: the compositor still held its last frame, which
            // came back as a capture of the window as it is (#2416).
            else if (active.WindowState == Avalonia.Controls.WindowState.Minimized)
                return new SnapshotResult(null, HexIDE.Automation.SnapshotComposer.MinimizedRefusal(StateNameOf(active)),
                    activeDialog, Title: active.Title, CaptureArea: null);
            else
                snapshot = await HexIDE.Automation.SnapshotComposer.CaptureAsync(active);
            using var captured = snapshot;
            if (snapshot is null)
                return new SnapshotResult(null, "Window has no size", activeDialog, Title: active.Title, CaptureArea: null);
            var (bitmap, windowOffset, leftOut) = (snapshot.Image, snapshot.WindowOffset, snapshot.LeftOut);

            // A file per capture, so the path an earlier call returned still holds its image (#756).
            var path = HexIDE.Automation.SnapshotComposer.NewCapturePath(System.IO.Path.GetTempPath(), DateTime.UtcNow);
            bitmap.Save(path);

            // The window origin includes any space left for a popup above or to its left (#648).
            var result = new SnapshotResult(path, null, activeDialog,
                bitmap.PixelSize.Width, bitmap.PixelSize.Height, active.DesktopScaling,
                WindowOffsetX: windowOffset.X, WindowOffsetY: windowOffset.Y,
                Note: string.Join(" ", new[] { snapshot.Frame, LeftOutNote(leftOut), snapshot.Fallback }.OfType<string>()) is { Length: > 0 } note ? note : null,
                Title: active.Title);
            if (includeFrame)
                result = result with { CaptureArea = FrameCaptureArea };
            if (!inline)
                return result;

            // Encoded here, from the image in hand, and attached by InlineSnapshot: nothing is read back (#957).
            var (width, height) = InlineSnapshot.Fit(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
            // A render target cannot be scaled directly, so a larger image is encoded and decoded again at the fitted width.
            using var png = new System.IO.MemoryStream();
            bitmap.Save(png);
            if (width != bitmap.PixelSize.Width)
            {
                png.Position = 0;
                using var fitted = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(
                    png, width, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
                height = fitted.PixelSize.Height;
                png.SetLength(0);
                fitted.Save(png);
            }
            InlineSnapshot.Hold(path, png.ToArray());
            return result with
            {
                InlineWidth = width,
                InlineHeight = height,
                InlineScale = active.DesktopScaling * width / bitmap.PixelSize.Width,
            };
        });
    }

    /// <summary>The 'window' parameter as the tree tools describe it, in the parameter's own schema entry.</summary>
    // The reply's wait for a running program (#1232), shared by the tools that act on its forms.
    // A frame capture's 'captureArea' (#1595, #2182).
    private const string FrameCaptureArea = "The whole window, title bar and frame included, as Windows renders it, even "
        + "where another window covers it; open popups are drawn over it unless the note says otherwise.";

    // What a caller needs before the call: that the reply waits, and what counts as the program waiting for input. Why a
    // reply stopped short, and which forms the program opened, its own note says when it happens (#2511, #2567).
    private const string ProgramWaitNote =
        "On a running VB6 program's form or its MsgBox, the reply waits up to 3 s for what the action set off; a loop "
        + "already at DoEvents, or a Timer's, counts as waiting for input. A note says why when it stops sooner.";

    private const string WindowParameter =
        "Which top-level window the path is resolved against: \"auto\" (default) is any visible window other than the "
        + "IDE's own, whether or not it is on top: a dialog, the Find window, or a running VB6 program's form, INCLUDING "
        + "while it is paused at a breakpoint; among several, a dialog wins, then the one with focus, then the one opened "
        + "last, else the IDE's own. \"ide\" reaches the IDE's own window; a window's title, or a running form's name such as \"Form2\", reaches that "
        + "window, which is how a form behind another form of the program is reached. An unknown name is refused with "
        + "the open windows listed, each form by its name and title: ask for an unknown name to discover them; the name is the stable handle.";

    [McpServerTool(Name = "dump_visual_tree")]
    [Description("Walks the live control tree of the active window (any window over the IDE's own; see 'window') and returns a node tree for finding and addressing controls. Uses the UIA 'control view': None-type wrappers without focus, AutomationId or non-scroll capability are collapsed; x:Name alone keeps no wrapper. An adorner, such as a control's grab handles, is under the control it adorns. Overflow never inserts a path segment. Each node carries its addressing 'path' (feed it back as a target), automation ControlType, Name, AutomationId, ClassName, the DataContext ViewModel type, supported interaction providers, and enabled/offscreen flags. Providers and their interact actions: invoke → invoke, selection and selectionItem → select and remove_from_selection, multiSelectItem → add_to_selection and remove_from_selection, value → set_value, toggle → toggle, expandCollapse → expand/collapse (also a context menu or flyout the control owns), rangeValue → set_range_value, scroll → scroll. Only usable actions are reported: a list that fits has no scroll; a progress bar has no rangeValue. Range capability keeps a node even with unusable bounds. A .NET type used as a name is reported as 'typeNameAsName' instead; it cannot name a path. 'isHidden': true marks an effectively-invisible control; absent when showing. isOffscreen reports clipping and scroll position instead. A long ListBox makes rows only for what is in view, so its dump lists those, and a row out of view with keyboard focus; interact 'select' with a value finds the rest by scrolling to them. Top-level 'focusedPath' names the keyboard focus in the whole window, even with root/match filters; null with 'focusReason' when none is in this window. Scope a large dump with 'root', or find nodes with 'match'; see 'maxDepth' and 'interactiveOnly' for what it keeps and when a node is 'truncated'. A tree over about 40,000 characters, more than some clients take inline, keeps the active document, cutting hidden children then other branches; the 'note', written first, explains cuts.")]
    public async Task<VisualTreeResult> DumpVisualTreeAsync(
        [Description("A path to dump only that subtree; omit for the whole window. For deeply nested or large areas, "
                     + "scope the dump this way. name is x:Name when set; label is the peer text when different. A segment accepts either.")]
        string? root = null,
        [Description("How many levels to walk, counted in control-view levels rather than raw visuals; at least 1. "
                     + "Default 20. A node whose children lie below the cut-off carries \"truncated\": true and an "
                     + "empty children list: dump again with its path as root to see them. Without \"truncated\", an "
                     + "empty list means the walk reached everything below, but under interactiveOnly the parts that "
                     + "are not interactive, such as a button's caption, are left out; pass interactiveOnly false to "
                     + "see them.")]
        int maxDepth = 20,
        [Description("Default true: keeps only nodes that are interactive or have an interactive descendant, and every "
                     + "control a running program names, a Label among them. Pass "
                     + "false to also see unnamed text and parts, the IDE's and a running form's; on a large form the default "
                     + "keeps nearly everything, so scope it with 'root'. A node cut off by maxDepth is kept, "
                     + "marked \"truncated\", since what lies below it was not looked at; it may turn out to hold "
                     + "nothing interactive. A node truncated to fit a large tree into the reply, as the 'note' says, "
                     + "does hold something.")]
        bool interactiveOnly = true,
        [Description(WindowParameter)]
        string? window = null,
        [Description("Text to find instead of the tree: returns, as 'matches', the nodes whose Name, label, AutomationId, ClassName, "
                     + "ControlType or typeNameAsName contains it, ignoring case, or whose DataContext type does where "
                     + "that context starts (not on every node below that inherits it); each with its path and without "
                     + "its subtree, up to 50. Use it to find a path to start from: 'TextEditor' finds the text editors "
                     + "on screen, the selected code tab's and the Immediate window's among them, and 'PropertiesTool' "
                     + "the Properties window. Only what is in the tree is found, so not a document in a tab that is not "
                     + "selected, or a dialog page that is not selected. Every node is searched, interactive or not, to "
                     + "'maxDepth' below 'root'; the reply's "
                     + "'note' says how many matched and what was not searched.")]
        string? match = null,
        CancellationToken ct = default)
    {
        // Below 1 the start node itself is the cut-off, so it comes back truncated with nothing walked, and the
        // advice to dump again from its path would give the same reply forever.
        if (maxDepth < 1)
            return new VisualTreeResult($"maxDepth must be at least 1; it was {maxDepth}", null, null);
        if (match is not null && string.IsNullOrWhiteSpace(match))
            return new VisualTreeResult("'match' is empty, which every node would match; omit it for the whole tree.", null, null);

        string? activeDocumentPath = null;
        var result = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var (active, label, error) = ResolveActiveWindow(window);
            if (active is null)
                return new VisualTreeResult(error, null, null);

            Control start = active;
            var basePath = "Window";
            if (root is not null)
            {
                var (resolved, resolveError) = ResolveIn(active, window, root);
                if (resolved is null)
                    return new VisualTreeResult(resolveError, label, null);
                start = resolved;
                basePath = root;
            }

            var focus = UiAutomationDriver.FocusIn(active);
            if (match is null)
            {
                activeDocumentPath = ActiveDocumentPath(start, basePath, ctx.DocumentDockService.ActiveTab);
                return new VisualTreeResult(null, label, UiAutomationDriver.Dump(start, basePath, maxDepth, interactiveOnly),
                    FocusedPath: focus.FocusedPath, FocusReason: focus.FocusReason);
            }

            // Every node, interactive or not: a Label or a pane is as much an answer as a button.
            var (matches, total, unsearched) = UiAutomationDriver.Find(
                UiAutomationDriver.Dump(start, basePath, maxDepth, interactiveOnly: false), match, MatchLimit);
            var options = active.GetVisualDescendants().OfType<Control>().Prepend(active)
                .Select(c => c.DataContext).OfType<OptionsViewModel>().FirstOrDefault();
            return new VisualTreeResult(null, label, null, [.. matches], MatchNote(match, total, unsearched,
                    label == "MainWindow" ? ctx.DocumentDockService : null, options?.RootNodes, options?.SelectedNode),
                focus.FocusedPath, focus.FocusReason);
        });
        // Off the UI thread: the tree is plain data by now, and sizing a large one takes a few serializations.
        if (result is { Root: { } tree, Matches: null })
        {
            var (fitted, fitNote) = FitToReply(tree, activeDocumentPath);
            result = result with { Root = fitted, Note = fitNote };
        }
        return result;
    }

    // Enough to choose from; past it the caller is told to narrow the text or the root, not handed the whole tree again.
    private const int MatchLimit = 50;

    /// <summary>
    /// The most characters a dump's tree is given. Claude Code (2.1.310, read from its code) saves an MCP tool's text reply
    /// over 50,000 characters to a file for the model to read in chunks, counting characters, not tokens. So the first
    /// call a caller makes, the whole IDE window at 66,000 to 84,000 characters, gave nothing it could read in the reply
    /// (#1982). This leaves 10,000 for the rest of the reply: the note, focusedPath and any modalDialog.
    /// </summary>
    internal const int TreeCharacterBudget = 40_000;

    /// <summary>
    /// The tree as it is when it fits <see cref="TreeCharacterBudget"/>; otherwise omit hidden nodes' children first,
    /// then cut other branches at the most levels down that fit, or at one, keeping the active document.
    /// The note says what was cut and how to see the rest.
    /// </summary>
    internal static (UiNode Tree, string? Note) FitToReply(UiNode tree, string? activeDocumentPath = null)
    {
        var full = SizeOf(tree);
        if (full <= TreeCharacterBudget)
            return (tree, null);

        static string N(int n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        // Keep the requested root's children so dumping again from a hidden node still makes progress.
        var showing = tree with { Children = [.. tree.Children.Select(c => CutHidden(c, activeDocumentPath))] };
        var hiddenCut = SizeOf(showing) != full;
        var hiddenNote = hiddenCut ? "First, children below hidden nodes were omitted. " : "";
        if (hiddenCut)
            tree = showing;
        if (SizeOf(tree) <= TreeCharacterBudget)
            return (tree, $"This tree is about {N(full)} characters, more than some clients take inline in a reply. "
                          + hiddenNote + "A node marked \"truncated\" holds more: dump again with its path as 'root' to see below it, "
                          + "or use 'match' to find a control by its name or type.");

        var documentNote = activeDocumentPath is null ? "" : "The active document subtree was kept to the requested depth. ";
        var deepest = DepthOf(tree);
        // Only one level below it, so there is no level to cut: it is its children that are many.
        if (deepest <= 1)
            return (tree, hiddenNote + $"The tree is about {N(full)} characters, more than some clients take inline in a reply, though it "
                          + "goes only one level down, since this node has many children: scope it with 'root', or use "
                          + "'match' to find a control by its name or type.");

        // A tree's size grows with every level kept, so the most levels that fit are found by halving.
        var (low, high) = (1, deepest - 1);
        var (cut, size) = (CutAt(tree, 1, activeDocumentPath), 0);
        size = SizeOf(cut);
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            var candidate = CutAt(tree, mid, activeDocumentPath);
            var candidateSize = SizeOf(candidate);
            if (candidateSize <= TreeCharacterBudget)
                (low, cut, size) = (mid, candidate, candidateSize);
            else
                high = mid - 1;
        }

        var note = hiddenNote + documentNote + $"This tree is about {N(full)} characters, more than some clients take inline in a reply, so "
                   + (activeDocumentPath is null ? "it was cut " : "other branches were cut ") + $"{low} level{(low == 1 ? "" : "s")} down, about {N(size)} characters. A node marked \"truncated\" "
                   + "holds more: dump again with its path as 'root' to see below it, or use 'match' to find a control by "
                   + "its name or type.";
        if (size > TreeCharacterBudget)
            note += activeDocumentPath is null
                ? " It is still over the limit one level down, since this node has many children: scope it with 'root'."
                : " It is still over the limit with other branches cut one level down and the active document kept: scope it with 'root'.";
        return (cut, note);
    }

    internal static string? ActiveDocumentPath(Control start, string basePath, object? document)
    {
        if (document is null)
            return null;
        var view = start.GetVisualDescendants().OfType<UserControl>().FirstOrDefault(c =>
            c.IsEffectivelyVisible && ReferenceEquals(c.DataContext, document));
        return view is null ? null : UiAutomationDriver.PathWithin(start, basePath, view);
    }

    // Inside the active document too: what is hidden there shows no more than anywhere else, and the designer's own hidden
    // draw preview and rubber band are in every form's tree (#2231). The document's own node is never cut.
    private static UiNode CutHidden(UiNode node, string? documentPath) => node.IsHidden == true && node.Path != documentPath
        ? CutAt(node, 0)
        : node with { Children = [.. node.Children.Select(c => CutHidden(c, documentPath))] };

    private static int SizeOf(UiNode tree) => System.Text.Json.JsonSerializer.Serialize(tree, ReplyJson.Options).Length;

    private static int DepthOf(UiNode node) => node.Children.Length == 0 ? 0 : 1 + node.Children.Max(DepthOf);

    // Keeps 'levels' levels below the node, and marks a node whose children are cut away truncated. Not the same as a
    // dump with that maxDepth under interactiveOnly, which also keeps a node at the cut-off whose children it never
    // looked at: here they were looked at, so a branch with nothing interactive is already gone (#1982 review).
    private static UiNode CutAt(UiNode node, int levels, string? documentPath = null)
    {
        if (node.Path == documentPath)
            return node;
        var ancestor = documentPath?.StartsWith(node.Path + "/", StringComparison.Ordinal) == true;
        return levels <= 0 && !ancestor
            ? node with { Children = [], Truncated = node.Children.Length > 0 || node.Truncated == true ? true : null }
            : node with { Children = [.. node.Children.Select(c => CutAt(c, Math.Max(0, levels - 1), documentPath))] };
    }

    internal static string MatchNote(string match, int total, int unsearched,
        IDocumentDockService? dock = null,
        IEnumerable<OptionsNodeViewModel>? pages = null,
        OptionsNodeViewModel? selectedPage = null)
    {
        var note = total switch
        {
            0 => $"Nothing matches '{match}'. Name, AutomationId, ClassName, ControlType, typeNameAsName and the "
                 + "DataContext type are searched, as text that contains it, ignoring case.",
            1 => $"1 node matches '{match}'.",
            _ when total > MatchLimit => $"{total} nodes match '{match}'; the first {MatchLimit}, in tree order, are listed. "
                                         + "Narrow 'match', or scope with 'root'.",
            _ => $"{total} nodes match '{match}'.",
        };
        if (unsearched > 0)
            note += $" {unsearched} {(unsearched == 1 ? "branch" : "branches")} stopped at maxDepth and {(unsearched == 1 ? "was" : "were")} "
              + "not searched: raise maxDepth, or search again from a truncated node's path as 'root'.";
        var tabs = dock?.AllTabs.Where(t => !ReferenceEquals(t, dock.ActiveTab)).Select(t => t.Title ?? "").ToArray() ?? [];
        if (tabs.Length > 0)
            note += $" {tabs.Length} open tab(s) are not selected, and what they hold is not in the tree: "
                    + string.Join(", ", tabs.Select(t => $"'{t}'")) + ". activate_document_tab brings one to the front.";

        IEnumerable<string> UnselectedPages(IEnumerable<OptionsNodeViewModel> nodes,
            string prefix = "")
        {
            foreach (var node in nodes)
            {
                var title = prefix + node.Title;
                if (node.Page is not null && !ReferenceEquals(node, selectedPage))
                    yield return title;
                foreach (var child in UnselectedPages(node.Children, title + " > "))
                    yield return child;
            }
        }
        var omittedPages = UnselectedPages(pages ?? []).ToArray();
        if (omittedPages.Length > 0)
            note += $" {omittedPages.Length} page(s) are not selected, and what they hold is not in the tree: "
                    + string.Join(", ", omittedPages.Select(p => $"'{p}'"))
                    + ". Use dump_visual_tree to find the category tree, then interact 'expand' on its category "
                    + "and 'select' on a page to search its contents.";
        return note;
    }

    [McpServerTool(Name = "inspect_element")]
    [DescribesEnum(typeof(Avalonia.Automation.ExpandCollapseState))]
    [DescribesEnum(typeof(WindowDecorations))]
    [DescribesEnum(typeof(WindowState))]
    [DescribesEnum(typeof(HexIDE.Automation.PointerSource))]
    [Description("Deep-inspects one control addressed by 'target' (a path from dump_visual_tree): identity, providers, 'isFocused', 'isKeyboardFocusable', and 'boundingRect' (x, y, width, height in device-independent units from the window's top-left; map into a snapshot with take_snapshot's scale and offsets). Reports value with 'isReadOnly' (with a value provider), toggle state, 'expandCollapseState' (Collapsed, Expanded, PartiallyExpanded, LeafNode, with an expand/collapse provider), 'selectionItems', what is selected, not the list's items, which 'items' gives: each one's text for select, off-screen ones too (a dropdown only while open); 'itemCount', how many items a list, dropdown or tab holds, hidden ones too, a scroll bar/slider's 'range' (value, minimum, maximum, isReadOnly), and 'pointer': a running program's mousePointer, number and name, what it 'shows', and 'setBy' (Own, Override as Screen.MousePointer sets, Style) on 'setOn'; absent if none is set. A Window also reports 'frame': canResize, windowDecorations (None, BorderOnly, Full), canMinimize, canMaximize, showInTaskbar, windowState (Normal, Minimized, Maximized, FullScreen). On Windows, nativeStyle and nativeExStyle are the HWND's GWL_STYLE/GWL_EXSTYLE masks, or nativeStyleNote says why not. Lists the DataContext ViewModel's public commands, Command-bound methods and properties for interact's reflection actions, with values readable as text. A control that inherits its DataContext lists only its own resolved Command, if any, with binding details in 'dataContextNote'; 'dataContextOwner' gives the owner's path, and 'dataContextNote' counts omitted Dock base-type members. An editor's Document reads as a line count: get_file_content returns the whole text that CaretOffset, SelectionStart and SelectionLength count into; the Immediate window's reads as its text. Values over 4000 characters are cut and give their full length. Pass the same 'window' as the dump the path came from. 'isHidden' is true for a control in the tree that does not show, and absent when it does.")]
    public async Task<InspectResult> InspectElementAsync(
        string target, [Description(WindowParameter)] string? window = null, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var (active, label, error) = ResolveActiveWindow(window);
            if (active is null)
                return new InspectResult(error, null, null);

            var (control, resolveError) = ResolveIn(active, window, target);
            if (control is null)
                return new InspectResult(resolveError, label, null);

            return new InspectResult(null, label, UiAutomationDriver.Inspect(control, target, active));
        });
    }

    [McpServerTool(Name = "interact")]
    [Description("Drives a live control at 'target' (a dump_visual_tree path) through UI Automation. 'action' lists the verbs; 'value' describes their arguments. A provider action needs its reported provider and fails without it, with no fallback to reflection: choose invoke_command/set_property explicitly. Every verb except invoke_command/set_property refuses disabled targets, naming a disabling container or blocking dialog to answer first. click needs no provider: it raises pointer input at the point itself, on whatever a real press there would reach, moving there first unless the last click left it there, so a form's MouseMove precedes MouseDown, releasing 50 ms later as separate input so the press handler runs first; it waits up to a second for the next frame, since hit-testing needs a drawn window; its reply names the button, the keys held, press coordinates and landing, and, with to, release coordinates and recipient (capturer or hit-tested control); a point where nothing takes the pointer is refused, and nothing is pressed. Actions are real, with no confirmation, but every verb except invoke_command and set_property is refused on a control that is not showing (\"isHidden\": true in dump_visual_tree), as no person could reach it; those two reach its view model or its own Command. toggle, expand, collapse, set_value and set_property reply with the state read back. set_value on a non-editable ComboBox selects item text ignoring case; a miss names items. Other read-only values are refused. Find the target and what it supports with dump_visual_tree/inspect_element first. While a VB6 program runs or is paused 'auto' usually picks its form: pass 'window' \"ide\" to act on the IDE. " + ProgramWaitNote)]
    public async Task<InteractOutcome> InteractAsync(
        [Description("A path from dump_visual_tree.")]
        string target,
        [Description("One of: invoke (click a Button or menu item); select (pick a ComboBox or ListBox item, a DataGrid row or a tree node; with 'value', a row out of view is scrolled to first; a dropdown's items exist only while it is open, so expand it, dump_visual_tree(root=combo), then select, which closes it as a click does); add_to_selection / remove_from_selection (add a multi-select list item or remove a list/tree item without replacing the selection; reply lists the selection and names the primary when several remain. The primary is the last control added); double_click (raise DoubleTapped, selecting the target first as a real double-click does, or commit a completion item; how the Project Explorer opens an item); click (press and release a pointer on the target as a real click does, so a running form raises MouseDown, Click and MouseUp; the only verb that presses; to drags, and hold stops before letting go; any control, a Label or a form's area included); release (let go of that held button); set_value (set a TextBox's text, or a fixed ComboBox's item; a box bound to a property is committed as Enter would, and the reply says whether it held or was refused); toggle (flip a CheckBox); expand / collapse (a dropdown, tree node, expander or menu, or else the context menu or flyout the control owns); set_range_value (set a Slider or ScrollBar, as dragging its thumb would; a read-only bar or out-of-range value is refused, not clamped); scroll (moves the target's own scroller or bars, so an editor or a DataGrid works, else the nearest container that scrolls that way; the reply gives where it now is); invoke_command and set_property (reflection: use the DataContext members inspect_element lists, or value Command for the target's resolved command; invoke_command checks CanExecute, and set_property coerces to the property's type); close (the target's window, as its close box does; a Yes/No or Abort/Retry/Ignore MsgBox refuses, a form's QueryUnload may; reply says if still open; not the IDE's main window).")]
        string action,
        [Description("Required for set_value (the text), set_range_value (a number, '.' for decimals), scroll (up, down, left or right for a page; line_up, line_down, line_left or line_right for a line; home or end for the top or bottom) and the reflection actions (invoke_command: Command runs the target’s resolved Command with its CommandParameter, checking CanExecute; otherwise the name of an ICommand property, or of a method XAML binds as a Command with no CommandParameter, refused when its Can<name>(object) returns false; set_property: \"PropertyName=NewValue\"). For select, the item text to match; omit it when 'target' already points at the item. Optional for click: a point 'x,y' relative to the target's top-left ('.' for decimals; the centre when omitted), a button, left, right or middle (left when omitted), any of shift, ctrl and alt to hold, and double for two clicks, in any order and separated by spaces, as in '10,5 right double' or 'double'; append to x,y or to <path>:x,y to drag (paste a dump_visual_tree path, including spaces); release coordinates are relative to that control, or the target without a path; to cannot accompany double; add hold to a drag to stop before its release, with the button still down at the to point until action release. While it is held, every reply's heldButton says where, a snapshot or a dump shows the drag in progress, click, double_click and hover are refused, and keys still go. For release: x,y relative to the target, or <path>:x,y, moves the held button there first; with no value it lets go where it is held. Example: click '20,20 to 100,60 hold' on the designer canvas shows the draw preview to 100,60, then release '140,90' draws to 140,90. With no call for 120 s a hold is abandoned: Escape, then a move back to the press and a release there. A Dock drag then docks nothing and a designer move ends where it began, but a resize snaps to the grid, and a draw still places a 2 by 2 control; the next reply's heldButton names any undo step recorded, which Undo takes back.")]
        string? value = null,
        [Description(WindowParameter)]
        string? window = null,
        CancellationToken ct = default)
    {
        Control? acted = null;
        UiAutomationDriver.ProgramWatch? watch = null;
        var outcome = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var (active, _, error) = ResolveActiveWindow(window);
            if (active is null)
                return new InteractOutcome(false, "peer", null, error);

            var (control, resolveError) = ResolveIn(active, window, target);
            if (control is null)
                return new InteractOutcome(false, "peer", null, resolveError);

            acted = control;
            // Read before the action: a MsgBox it answers closes, and loses what ties it to the program (#1232).
            watch = UiAutomationDriver.WatchProgram(active, () => ctx.DebugController.CurrentStop);
            // A click waits for its window's next frame, which a window opened a moment ago has yet to draw (#1065).
            return await UiAutomationDriver.InteractAsync(control, action, value);
        });

        // A click's release is its own input, raised once what the press set off has run.
        outcome = await UiAutomationDriver.FinishAsync(outcome);

        // A committed value is only known to have held once the source has reacted, and a refusal reacts from a
        // posted callback; this hop runs after it. (#625)
        if (outcome is { Success: true, Committed: true } && acted is not null && value is not null)
        {
            var refused = await Dispatcher.UIThread.InvokeAsync(
                () => UiAutomationDriver.RefusedCommit(acted, value), DispatcherPriority.Background);
            if (refused is not null)
                outcome = UiAutomationDriver.WithRefusal(outcome, refused);
        }
        // A running form's handler gives up the thread part way through, so the reply waits for it (#1232).
        outcome = await UiAutomationDriver.WaitForProgramAsync(watch, outcome, ct: ct);
        // Whether a close closed is known only now: a form's close box asks the program, which unloads it first (#829).
        if (acted is not null && UiAutomationDriver.IsVerb(action, "close"))
            outcome = await Dispatcher.UIThread.InvokeAsync(() => UiAutomationDriver.AfterClose(acted, outcome, watch));
        return outcome;
    }

    [McpServerTool(Name = "type_text")]
    [Description("Types text into the control at 'target' (a path from dump_visual_tree) by inserting at the caret via the control's own API, writing over a standing selection as a person's typing does (the reply says how many characters it replaced), reported as mechanism \"document\" because it is an edit and not key events ('mode' \"keyboard\" sends key events instead, as a person types, Shift's own key around a capital); a read-only or disabled surface is refused, as a person's typing would be — works on the code editor (AvaloniaEdit), which has no value provider for 'interact set_value'. See 'target' for descendant surface selection and refusals. See 'text' for line breaks. Use press_key for Enter/Tab/commands. In a code window a selection reaching the file's header or a member's Attribute lines, which only the IDE changes, has only its editable parts replaced, and the reply says what stayed; with nothing editable, or the caret in such a region, it is refused, naming the region and giving an editable offset into the whole file (get_file_content). At the end of a declaration whose Attribute lines follow it, the text from its first line break on goes after those lines, with its line breaks written as the file's, as a person's paste does there; the reply says so and gives the caret's new offset. 'window' picks the top-level window the path is resolved against; its own description lists the values. " + ProgramWaitNote)]
    public async Task<InteractOutcome> TypeTextAsync(
        [Description("A path from dump_visual_tree. A target that is not showing is refused. If the target is not itself a text surface, a descendant editor/textbox is used (AvaloniaEdit is preferred over incidental textboxes); the reply gives the path of the surface that took the text. A target holding more than one showing surface of the chosen kind is refused, with their paths listed. In the default mode a dropdown that is not editable is refused with a pointer to select.")]
        string target,
        [Description("The text to type. In the default mode, text over several lines is inserted verbatim: include \\n "
                     + "for each line break. It is exact, reliable, and not altered by live auto-indent or IntelliSense. "
                     + "For a typing cadence, call this once per line.")]
        string text,
        [Description(WindowParameter)] string? window = null,
        [Description("\"document\" (the default) inserts the text at the caret in one edit, exact and untouched by "
                     + "auto-indent, completion or auto-close. \"keyboard\" types it as a person does, a key at a time, to "
                     + "the focused control: KeyDown, TextInput and KeyUp per character, \\n as Enter and \\t as Tab, with "
                     + "key bindings tried first, so what typing drives runs. In a code window that is the completion "
                     + "request a letter makes, parameter info on ( and , and auto-indent and the End a line break adds; "
                     + "after each key the language server's answer is waited for, up to 2 s. The text that lands may "
                     + "differ from what was sent, so read it back. A Tab that moves focus sends the rest to where focus "
                     + "went. The typing stops, and the reply says after how many keys, at a key a protected line refuses, "
                     + "or when a dialog opens, over this window or another. Nothing is typed while a modal dialog is open "
                     + "over the window, as a person's keys would go to it. A control with no text box that takes focus, "
                     + "such as a running form's RichTextBox, is sent the keys too, and the reply says if it took none of "
                     + "their text. Its mechanism is \"keyboard\".")]
        string? mode = null,
        CancellationToken ct = default)
    {
        var keyboard = (mode ?? "document").Trim().ToLowerInvariant() switch
        {
            "document" or "" => false,
            "keyboard" => true,
            _ => (bool?)null,
        };
        if (keyboard is null)
            return new InteractOutcome(false, UiAutomationDriver.TypedMechanism, null,
                $"unknown mode '{mode}': use \"document\" or \"keyboard\"; nothing was typed");
        var mechanism = keyboard.Value ? UiAutomationDriver.KeyboardMechanism : UiAutomationDriver.TypedMechanism;

        UiAutomationDriver.ProgramWatch? watch = null;
        var outcome = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var (active, _, error) = ResolveActiveWindow(window);
            if (active is null)
                return new InteractOutcome(false, mechanism, null, error);

            var (control, resolveError) = ResolveIn(active, window, target);
            if (control is null)
                return new InteractOutcome(false, mechanism, null, resolveError);

            // Read before the action: a MsgBox it answers closes, and loses what ties it to the program (#1232).
            watch = UiAutomationDriver.WatchProgram(active, () => ctx.DebugController.CurrentStop);
            return keyboard.Value
                ? await UiAutomationDriver.TypeKeysAsync(control, text, target, ct)
                : UiAutomationDriver.TypeText(control, text, target);
        });
        return await UiAutomationDriver.WaitForProgramAsync(watch, outcome, ct: ct);
    }

    [McpServerTool(Name = "hover")]
    [Description("Moves the pointer onto the control at 'target' (a path from dump_visual_tree) by sending a mouse move through the window's own input, as the platform delivers a real one, watches for a tip, and reports its text: a declared ToolTip.Tip, such as a toolbar button's, and a tip a control raises from its own pointer handler, such as LSP quick-info and the debugger's Auto Data Tips in the code editor. A tip that is a control reads as its visible text blocks, joined with ' / '. The move is hit-tested as a real one is: when something else is drawn at the point the reply says what it landed on, and reports that control's tip, as that is what a person would be shown; a disabled target shows a tip only with ToolTip.ShowOnDisabled, and the reply says it is disabled. When nothing is drawn at the point, or Avalonia's input cannot be reached, the move is raised on the control instead, which opens only the second kind, and the reply says so. Without 'x' and 'y' a code editor is hovered at its CARET, which is how a particular identifier is hovered; see 'x'. 'dwellMs' is how long to watch, and 'window' picks the window. When no tip opens, the reply adds the 'declared tip' of the control under the hovered point or its nearest ancestor that sets one, which is the text a human would be shown there. The pointer then leaves the window, closing the tip, so assert the reply, not a snapshot: 'tipBounds' places an opened tip in the window as inspect_element's boundingRect does, and 'detail' gives its offset from the point hovered. The cursor and pointer-over stay the hover's until the real mouse next moves.")]
    public async Task<InteractOutcome> HoverAsync(
        [Description("A path from dump_visual_tree. A target that is not a code editor but contains one is hovered at "
                     + "that editor, the first showing under it, and the reply then names the editor and gives its path, "
                     + "as dump_visual_tree would print it.")]
        string target,
        [Description("With 'y', the point to hover, relative to the target's own top-left. Omit both and the point is "
                     + "the CARET for a code editor, so position it first with interact set_property CaretOffset, an "
                     + "offset into the whole file get_file_content returns, which is how you hover a particular "
                     + "identifier; and the centre of anything else.")]
        double? x = null,
        [Description("With 'x', the point to hover, relative to the target's own top-left; see 'x' for what omitting "
                     + "both does.")]
        double? y = null,
        [Description("How long to watch for a tip, in milliseconds: default 1500, from 0 to 10000. It must exceed the "
                     + "400ms quick-info dwell plus the language server's round trip.")]
        int dwellMs = 1500,
        [Description(WindowParameter)] string? window = null,
        CancellationToken ct = default)
    {
        if (dwellMs is < 0 or > MaxHoverDwellMs)
            return new InteractOutcome(false, "pointer", null,
                $"dwellMs must be between 0 and {MaxHoverDwellMs}; it was {dwellMs}");

        UiAutomationDriver.HoverLanding? landing = null;

        // The move is hit-tested against what the window last drew, as click's press is: a window opened a moment ago has
        // drawn nothing, so its next frame is waited for first (#1265).
        var resolved = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var (active, _, error) = ResolveActiveWindow(window);
            if (active is null) return (null, error);
            var (control, resolveError) = ResolveIn(active, window, target);
            return (control, resolveError);
        });
        if (resolved.Item1 is not { } found)
            return new InteractOutcome(false, "pointer", null, resolved.Item2);
        await Dispatcher.UIThread.InvokeAsync(() => UiAutomationDriver.NextFrameDrawnAsync(found, UiAutomationDriver.ClickFrameWait));

        var raised = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var outcome = UiAutomationDriver.Hover(found, x, y, target, out var landed);
            landing = landed;
            return outcome;
        });

        if (!raised.Success || landing is not { } at) return raised;
        var hovered = at.Receiver;
        // The real mouse, or another hover running at the same time, takes the tip, and only the window's input says so (#2376).
        var watch = at.ThroughWindowInput
            ? await Dispatcher.UIThread.InvokeAsync(() =>
                TopLevel.GetTopLevel(hovered) is { } top && at.Receiver.TranslatePoint(at.Point, top) is { } point
                    ? new UiAutomationDriver.ForeignPointerWatch(top, point) : null)
            : null;
        string? moved = null;

        // POLLED. A tip opens after a dwell, and the move raised on the control in the fallback never set the pointer
        // over anything, so Avalonia closed the editor's tip again shortly after it opened: looking once when the dwell
        // expired reported "no tip" for a tip a human plainly saw on screen.
        //
        // Waited off the UI thread throughout: every tip this exists to surface comes from an async
        // continuation -- a 400ms dwell, then an await -- so holding the dispatcher would stop the very
        // thing being waited for and then report, accurately, that nothing happened.
        string? tip = null;
        string? declared = null;
        var enabled = true;
        // Where the tip was while it was open, beside the point hovered, both in the window: it closes when the hover ends,
        // so this is the only time its placement can be read (#1303).
        (Rect Bounds, Point Hovered)? placed = null;
        try
        {
            var deadline = Environment.TickCount64 + dwellMs;
            // A tip that is a control is open a moment before its text blocks have their text, so one read that soon found
            // none: a tip read as having no text keeps the poll going, and is reported only if no text comes (#2336).
            string? textless = null;
            while (tip is null && Environment.TickCount64 < deadline)
            {
                await Task.Delay(50, ct);
                (tip, placed) = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (DescribeOpenToolTip(hovered) is not { } open)
                        return ((string?)null, placed);
                    var window = TopLevel.GetTopLevel(hovered);
                    return (open.Text,
                        window is not null && UiAutomationDriver.OpenTipBounds(open.Owner, window) is { } bounds
                            && at.Receiver.TranslatePoint(at.Point, window) is { } point
                            ? (bounds, point) : placed);
                });
                if (tip is not null && tip.EndsWith(UiAutomationDriver.NoTextToRead, StringComparison.Ordinal))
                    (textless, tip) = (tip, null);
            }
            tip ??= textless;

            // Nothing OPENED. Say so plainly -- and then, separately, say what the control declares, because the
            // caller's real question is usually "has this button the right tooltip?" and that is answerable from the
            // attached property even when Avalonia will not show it. The two are reported under different words on
            // purpose: "tip:" means a popup was observed open, "declared tip:" means only that the text is set.
            // Collapsing them would turn a limitation into a passing assertion.
            if (tip is null)
                (declared, enabled) = await Dispatcher.UIThread.InvokeAsync(
                    () => (UiAutomationDriver.DeclaredToolTipAt(at.Receiver, at.Point), at.Receiver.IsEffectivelyEnabled));
        }
        finally
        {
            if (watch is not null)
                moved = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var what = watch.Moved;
                    watch.Dispose();
                    return what;
                });
            // The pointer then leaves, closing the tip, so the next action finds the window with nothing over it: a tip
            // left open covered what a click was aimed at, and the click's press, raised on the control, never closed it.
            if (at.ThroughWindowInput)
                await Dispatcher.UIThread.InvokeAsync(() => UiAutomationDriver.EndHover(hovered));
        }

        if (tip is not null)
            return placed is not { } where
                ? raised with { Detail = raised.Detail + "; tip: " + tip }
                : raised with
                {
                    Detail = raised.Detail + "; tip: " + tip + " (" + TipPlacement(where.Bounds, where.Hovered) + ")",
                    TipBounds = [Round(where.Bounds.X), Round(where.Bounds.Y), Round(where.Bounds.Width), Round(where.Bounds.Height)],
                };

        // Said as a fact when input the hover did not send moved the pointer during the watch, whether or not a tip is
        // declared: quick info and Auto Data Tips declare none, and missed the same way (#2376). Not for a disabled target,
        // which opened none to take, and whose reply already says why.
        var took = enabled ? moved : null;
        var unopened = raised.Detail + "; no tip opened within " + dwellMs + "ms" + (took is null ? string.Empty : " (" + took + ")");
        return raised with
        {
            Detail = declared is null
                ? unopened
                : unopened
                  + (took is not null ? string.Empty
                      : !at.ThroughWindowInput
                      ? " (the move was raised on the control, which Avalonia's ToolTipService never sees)"
                      : enabled ? " (its ToolTip.ShowDelay may be longer than the watch)" : string.Empty)
                  + "; declared tip: " + declared,
        };
    }

    // The watch is bounded so a mistyped value cannot hold a call open for minutes. Refused rather than
    // clamped, because a clamped value left the reply quoting a dwell that never happened (#610).
    private const int MaxHoverDwellMs = 10_000;

    /// <summary>
    /// The text of a tooltip currently OPEN on a control, one of its ancestors, or one of its
    /// descendants; null when none is open.
    /// </summary>
    /// <remarks>
    /// <b>Reads the attached property rather than looking for a ToolTip in the window's visual tree, and the
    /// difference is the whole reason this works.</b> On the desktop a tooltip is realised in its own popup
    /// <c>TopLevel</c>, so it is NOT a visual descendant of the window that owns it — the same parenting that
    /// makes a context menu invisible to <c>take_snapshot</c> (see docs/mcp-server-gaps.md). A headless test
    /// does not show this, because headless popups are in-window overlays and a descendant walk finds them.
    ///
    /// <para>
    /// Ancestors are searched because the tip is set on whichever control owns it: the code editor attaches
    /// quick-info to its <c>TextEditor</c>, which may be above the element a caller addressed.
    /// </para>
    /// </remarks>
    /// <summary>Where a tip opened, from the point hovered: "its top-left 4 right of and 21 below the point hovered".</summary>
    internal static string TipPlacement(Rect tip, Point hovered)
    {
        var (dx, dy) = (Round(tip.X - hovered.X), Round(tip.Y - hovered.Y));
        static string N(double v) => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        return $"its top-left {N(Math.Abs(dx))} {(dx < 0 ? "left" : "right")} of and {N(Math.Abs(dy))} "
               + $"{(dy < 0 ? "above" : "below")} the point hovered";
    }

    private static double Round(double v) => Math.Round(v, 1);

    private static (string Text, Control Owner)? DescribeOpenToolTip(Control? from)
    {
        if (from is null) return null;

        // Ancestors AND descendants. The tip is set on whichever control owns it, which is rarely the one
        // a caller addressed: the code editor attaches quick-info to its TextEditor, several levels BELOW
        // the CodeEditorView a path resolves to. Walking only upwards finds nothing and reports no tip for
        // one that is plainly open.
        for (var c = from; c is not null; c = c.Parent as Control)
            if (TipOn(c) is { } fromAncestor) return (fromAncestor, c);

        foreach (var c in from.GetVisualDescendants().OfType<Control>())
            if (TipOn(c) is { } fromDescendant) return (fromDescendant, c);

        return null;
    }

    private static string? TipOn(Control c) =>
        ToolTip.GetIsOpen(c) && UiAutomationDriver.TipText(ToolTip.GetTip(c)) is { } text ? text : null;
    [McpServerTool(Name = "press_key")]
    [Description("Presses a key on the control at 'target' through the window's platform input, so access keys (Alt+letter), menus and key bindings see it with optional 'modifiers', whose own keys go down before it and up after, as a person's chord, and types the key's character if it has one; 'window' selects the top-level window. If target contains the keyboard focus, presses there without moving it. Otherwise resolves to the DEEPEST input surface under 'target' (in the code editor, its TextArea) and focuses it first; falls back to the first focusable descendant, and FAILS when nothing under 'target' can take keyboard focus. The reply says 'moved focus from <path or nothing> to <path>' when it moved focus before delivery, which raises a running program's focus events. The mechanism is 'window-input', or 'routed-key' when focus or platform input was unavailable and KeyDown/KeyUp were raised on the surface instead, which skips access keys; a refusal reads 'keyboard'. In a code window, Delete or Back over a selection deletes only its editable parts, and says what was left. Any other change to the file's header or a member's Attribute lines, which only the IDE changes, is refused; the reply names the region and gives an editable offset into the whole file (get_file_content), or advice for a backward deletion. A key deleting the whole of a declaration's line deletes its Attribute lines with it, as the code window's keys do, and is not refused. Navigation keys are never refused. Refused on a disabled control, and while a modal dialog is open over the window, which would take a person's keys. " + ProgramWaitNote)]
    public async Task<InteractOutcome> PressKeyAsync(
        [Description("A control's path, as dump_visual_tree prints it.")] string target,
        [Description("An Avalonia Key name: Enter, Tab, Back, Delete, Escape, Down, S, ... A chord written as one, such as 'Ctrl+H', is read as key 'H' with modifiers 'Ctrl', and the reply says so.")] string key,
        [Description("Optional: 'Ctrl', 'Shift', 'Alt', or a combination such as 'Ctrl+Shift'.")] string? modifiers = null,
        [Description(WindowParameter)] string? window = null,
        CancellationToken ct = default)
    {
        UiAutomationDriver.ProgramWatch? watch = null;
        var outcome = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var (active, _, error) = ResolveActiveWindow(window);
            if (active is null)
                return new InteractOutcome(false, "keyboard", null, error);

            var (control, resolveError) = ResolveIn(active, window, target);
            if (control is null)
                return new InteractOutcome(false, "keyboard", null, resolveError);

            // Read before the action: a MsgBox it answers closes, and loses what ties it to the program (#1232).
            watch = UiAutomationDriver.WatchProgram(active, () => ctx.DebugController.CurrentStop);
            return UiAutomationDriver.PressKey(control, key, modifiers, target);
        });
        return await UiAutomationDriver.WaitForProgramAsync(watch, outcome, ct: ct);
    }

    // Active window for the automation tools: any visible window over the main window, modal or not, such as Find
    // (#1264) (mirrors take_snapshot) so dialogs are addressable with no extra parameter.
    private static (Window? window, string? label, string? error) ResolveActiveWindow(string? scope = null, bool ideByDefault = false)
    {
        var lifetime = Avalonia.Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var mainWindow = lifetime?.MainWindow;
        if (mainWindow is null)
            return (null, null, "No main window");

        var (window, error) = HexIDE.IDE.ForegroundWindow.Pick(scope, mainWindow, lifetime!.Windows, ideByDefault);
        if (window is null)
            return (null, null, error);

        var label = window != mainWindow ? DescribeWindow(window) : "MainWindow";
        return (window, label, null);
    }

    /// <summary>
    /// <paramref name="path"/> resolved in <paramref name="active"/>, with a failure naming that window when "auto" picked
    /// one over the IDE's own. See <see cref="HexIDE.IDE.ForegroundWindow.Resolve"/>.
    /// </summary>
    private static (Control? control, string? error) ResolveIn(Window active, string? scope, string path)
    {
        var main = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        return main is null
            ? UiAutomationDriver.Resolve(active, path)
            : HexIDE.IDE.ForegroundWindow.Resolve(path, scope, active, main, DescribeWindow(active));
    }

    /// <summary>
    /// A name the caller can act on. Title first, but it cannot be relied on: a VB6 `MsgBox` reaches the
    /// runtime with an empty caption (issue #131), and a blank label reads as "no dialog is open" — the
    /// exact confusion #61 existed to remove. Fall back to what the window is showing.
    /// </summary>
    private static string DescribeWindow(Avalonia.Controls.Window window)
    {
        if (!string.IsNullOrWhiteSpace(window.Title))
            return window.Title;
        return window.Content?.GetType().Name is { Length: > 0 } content ? content : "Dialog";
    }

    /// <summary>
    /// What a snapshot left out and why, or null when it drew every open popup: a popup reported on no screen, or so far
    /// from the window that both would not fit one image, which threw an unhandled "Positive number required" (#1960).
    /// </summary>
    internal static string? LeftOutNote(IReadOnlyList<HexIDE.Automation.SnapshotComposer.LeftOutPopup> leftOut) =>
        leftOut.Count == 0 ? null
            : $"Left out of this image: {string.Join("; ", leftOut.Select(p =>
                $"a {p.Kind} popup reported at screen pixels ({p.Rect.X}, {p.Rect.Y}), {p.Rect.Width} × {p.Rect.Height}, {p.Reason}"))}. "
              + "The window and every other open popup are drawn, and dump_visual_tree still lists a left-out popup's controls.";

    /// <summary>A document tab's kind, as get_document_tabs reports it.</summary>
    private static string TabType(object tab) => tab switch
    {
        HexIDE.VisualDesigner.FormEditViewModel => "designer",
        BaseEditorWindowViewModel => "code",
        _ => "tool",
    };

    private DocumentTabInfo TabInfo(Dock.Model.Core.IDockable tab)
    {
        var editor = tab as BaseEditorWindowViewModel;
        var document = editor?.OpenDocument;
        // IsModified is what the tab's `*` is drawn from (#1797), and the title leaves the `*` out (#1945). A tool tab
        // or a read-only view holds nothing to save, so it reports nothing rather than a false that reads as "saved".
        return new DocumentTabInfo(tab.Title ?? "", TabType(tab),
            ReferenceEquals(tab, ctx.DocumentDockService.ActiveTab), document?.Project.Name, document?.Name,
            editor is { CanHoldUnsavedChanges: true } ? editor.IsModified : null);
    }

    /// <summary>
    /// The document region after a tool changed it: the active tab and how many are open. The tab tools replied
    /// {"success":true} and nothing else, so a caller had to call get_document_tabs to learn what was now in
    /// front (#655).
    /// </summary>
    private string TabStateNote()
    {
        var count = ctx.DocumentDockService.AllTabs.Count();
        return ctx.DocumentDockService.ActiveTab is { } active
            ? $"Active tab: '{active.Title}' ({TabType(active)})"
              + ((active as BaseEditorWindowViewModel)?.OpenDocument is { } document ? $" [{document.Display}]" : "")
              + $"; {count} open."
            : $"No tab is active; {count} open.";
    }

    [McpServerTool(Name = "get_document_tabs")]
    [Description("Returns EVERY tab in the document region with its title, type, project, document, whether it is the active one, and isModified: whether the tab has unsaved changes. The tab marks those with a `*` that the title leaves out, so the title stays the one activate_document_tab and close_document_tab match. Project and document name a form or module's source, and only such a tab has them. isModified is absent where nothing can be saved: a tool tab, and the read-only project and archive views. It is per tab: a form's designer and code tabs each have one, and they can differ, so the form is unsaved if either is true. 'type' is 'designer' for a form or UserControl designer, 'code' for a source editor, and 'tool' for a document that is not an editor at all — the Object Browser, the language-server connection list, the protocol inspector. Those three are real tabs in the same strip and used to be missing from this answer, which made an automation client believe a tab it could see on screen did not exist. get_open_editors answers the narrower question: the editors only, and which of them is active.")]
    public async Task<DocumentTabsResult> GetDocumentTabsAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // The DOCK's tabs, not the editor service's list. The service tracks the editors it was asked
            // to open; the shell adds other documents to the same dock directly, and reporting only the
            // first set answers a different question from the one asked.
            var tabs = ctx.DocumentDockService.AllTabs
                .Select(TabInfo)
                .ToArray();
            return new DocumentTabsResult(tabs);
        });
    }

    [McpServerTool(Name = "activate_document_tab")]
    [Description("Brings a tab to the front, giving an editor keyboard focus. Pass `name` (the document's VB6 name) and optional `project`, matched without regard to case through the same lookup as open_file. An ambiguous document is refused with candidates. If both code and designer tabs are open, pass `type`: code or designer. Alternatively pass `title` from get_document_tabs, including for tool tabs (type tool); a duplicate title is refused. Pass either title or name, not both. The reply names the target's project and document when it has one, and its note identifies the active tab and count afterward.")]
    public async Task<MutateResult> ActivateDocumentTabAsync(string? title = null, string? name = null,
        string? project = null, string? type = null, CancellationToken ct = default)
    {
        return await FocusingTheActiveTabAsync(await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var lookup = name is null ? default : ResolveDocument(name, project);
            var (tab, error) = FindTab(title, lookup, type, project);
            if (tab is null)
                return Naming(new MutateResult(false, error), lookup);
            var found = ctx.DocumentDockService.TryActivateAny(d => ReferenceEquals(d, tab));
            return Naming(new MutateResult(found, null, TabStateNote()),
                (tab as BaseEditorWindowViewModel)?.OpenDocument);
        }));
    }

    private (Dock.Model.Core.IDockable? Tab, string? Error) FindTab(
        string? title, NamedDocument lookup, string? type, string? project)
    {
        if (title is not null && (lookup.Document is not null || lookup.Error is not null || project is not null))
            return (null, "Pass either `title` or `name` with optional `project`, not both.");
        if (lookup.Error is not null)
            return (null, lookup.Error);
        if (title is null && lookup.Document is null)
            return (null, "Pass `name` with optional `project` for a document, or `title` for a tool tab. get_document_tabs lists both.");
        if (type is not null && type is not ("code" or "designer" or "tool"))
            return (null, "Use `type`: code, designer or tool, as get_document_tabs reports it.");
        var matches = ctx.DocumentDockService.AllTabs.Where(d =>
            (lookup.Document is { } document
                ? (d as BaseEditorWindowViewModel)?.OpenDocument == document
                : string.Equals(d.Title, title, StringComparison.OrdinalIgnoreCase))
            && (type is null || TabType(d) == type)).ToArray();
        return matches.Length switch
        {
            1 => (matches[0], null),
            0 => (null, lookup.Document is { } document
                ? $"No open tab for {document.Display}" + (type is null ? "." : $" of type '{type}'.")
                : NoSuchTab(title!)),
            _ => (null, "More than one tab matches. Pass `name` and `project` to identify the document, and `type` (code or designer) to choose its tab. get_document_tabs lists them."),
        };
    }

    /// <summary>Says which tabs there are, rather than only that this one is not among them.</summary>
    /// <remarks>
    /// A bare "no tab called X" leaves a caller unable to tell a typo from a tab that never opened, and
    /// costs a second call to find out. The tabs are already in hand.
    /// </remarks>
    private string NoSuchTab(string title)
    {
        var open = ctx.DocumentDockService.AllTabs
            .Select(d => d.Title)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();

        return open.Length == 0
            ? $"No document tab with title '{title}'. Nothing is open in the document region."
            : $"No document tab with title '{title}'. Open tabs: {string.Join(", ", open)}.";
    }

    [McpServerTool(Name = "close_document_tab")]
    [Description("Closes a tab; a save prompt can leave it open, which the note reports. Pass `name` (the document's VB6 name) and optional `project`, matched without regard to case through the same lookup as open_file. An ambiguous document is refused with candidates. If both code and designer tabs are open, pass `type`: code or designer. Alternatively pass `title` from get_document_tabs, including for tool tabs (type tool); a duplicate title is refused. Pass either title or name, not both. The reply names the target's project and document when it has one, and its note identifies the active tab and count afterward.")]
    public async Task<MutateResult> CloseDocumentTabAsync(string? title = null, string? name = null,
        string? project = null, string? type = null, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var lookup = name is null ? default : ResolveDocument(name, project);
            var (tab, error) = FindTab(title, lookup, type, project);
            if (tab is null)
                return Naming(new MutateResult(false, error), lookup);
            var document = (tab as BaseEditorWindowViewModel)?.OpenDocument;
            var closed = ctx.DocumentDockService.TryCloseAny(d => ReferenceEquals(d, tab));
            if (!closed)
                return Naming(new MutateResult(false, "The tab could not be closed. " + TabStateNote()), document);
            var stillOpen = ctx.DocumentDockService.AllTabs.Any(d => ReferenceEquals(d, tab));
            return Naming(new MutateResult(true, null, stillOpen
                ? $"'{tab.Title}' is still open: closing it is waiting on an answer, most likely a save prompt, which "
                  + "dump_visual_tree shows. " + TabStateNote()
                : $"Closed '{tab.Title}'. " + TabStateNote()), document);
        });
    }

    [McpServerTool(Name = "invoke_menu_item")]
    [Description("Invokes a menu item by slash-separated path, e.g. 'Tools/Hello from TestAddin' or 'Add-Ins/TestAddin/Do Something'. Each segment is the text the menu displays, matched case-insensitively: 'Project/Add Module' reaches the item whose header is 'Add _Module' (the underscore marks the access key, wherever it falls), and a trailing '...' may be left off, so 'Tools/Options' reaches 'Options...'. A menu need not be open first: each submenu on the path gets its SubmenuOpened, as opening it does, so one filled then holds its items. An item whose header is a control is matched by its automation name, as dump_visual_tree names it. If a segment is not found, the error names the menu it looked in and every item that menu holds. Most built-in items are routed commands, which act on the control that has keyboard focus, as a real click does: one that belongs to a document (Tools/Add Procedure to a code window) needs focus in that document, which open_file, view_designer and activate_document_tab give it, as opening it by hand does; the refusal names what has focus instead. A checkable item with no command of its own, such as View/Minimap, is ticked or unticked, as a click does. Returns an error if the path cannot be resolved or the item cannot execute. The reply's 'note' names the item invoked, as its menu shows it.")]
    public async Task<MutateResult> InvokeMenuItemAsync(string path, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var lifetime = Avalonia.Application.Current!.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime;
            var window = lifetime?.MainWindow;
            if (window is null)
                return new MutateResult(false, "No main window");

            var menu = window.FindDescendantOfType<Menu>();
            if (menu is null)
                return new MutateResult(false, "No menu bar found in main window");

            // Through the resolved command rather than the MenuItem: an entry of a submenu bound to ItemsSource,
            // such as Recent Projects, has no MenuItem until that submenu is opened. (#544)
            var found = MenuPath.Resolve(menu.Items, path);
            if (found.Error is not null)
                return new MutateResult(false, found.Error);

            // A checkable item such as View/Minimap has no command: a click moves its tick, which its binding carries
            // to the setting (#947).
            if (found.Command is null && found.Item is { HasSubMenu: false, ToggleType: not MenuItemToggleType.None } checkable)
            {
                if (!checkable.IsEffectivelyEnabled)
                    return new MutateResult(false, $"'{path}' cannot execute now: the item is disabled.");
                var ticked = UiAutomationDriver.ClickMenuItem(checkable);
                return new MutateResult(true, null, $"Invoked '{found.Header ?? path}'{ticked}.");
            }

            if (found.Command is not { } command)
                return new MutateResult(false, $"'{path}' is a submenu or has no command");

            if (!command.CanExecute(found.CommandParameter))
                return new MutateResult(false,
                    MenuCommandRefusal.Describe(found, path, () => FocusedElementName(window)));

            var note = UiSaveFeedback.Execute(command, found.CommandParameter, $"Invoked '{found.Header ?? path}'.");
            return new MutateResult(true, null, note);
        });
    }

    /// <summary>What has keyboard focus in <paramref name="window"/>, for a refusal that depends on it.</summary>
    private static string FocusedElementName(Window window)
    {
        if (TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement() is not Control focused)
            return "nothing has focus";
        // The nearest name up the tree: the focused control itself is usually an unnamed part of a template.
        var owner = focused.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(c => !string.IsNullOrEmpty(c.Name))?.Name;
        var context = focused.DataContext?.GetType().Name;
        return $"a {focused.GetType().Name}" + (owner is not null ? $" named '{owner}'" : "")
               + (context is not null ? $" showing {context}" : "");
    }

    /// <summary>
    /// The form, module or class a caller named, in any loaded project, or the reason it could not be named.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaces a helper that searched the startup project only and then built a key by interpolating
    /// the <em>caller's</em> spelling of the name (hexide-io/HexIDE#467). The stores compared their keys
    /// ordinally, so a client that wrote <c>form1</c> for <c>Form1</c> created a second entry the gutter
    /// never read, and the reply echoed that spelling back, which reads as confirmation. A name now
    /// resolves to a document before anything is keyed, and what comes back is the document's own name.
    /// </para>
    /// <para>
    /// The rule is <see cref="NamedDocuments"/>'s, in the IDE assembly, because nothing references this
    /// executable and a rule only a tool body holds cannot be tested.
    /// </para>
    /// </remarks>
    private NamedDocument ResolveDocument(string name, string? project) =>
        NamedDocuments.Resolve(ctx.ProjectManager.LoadedProjects, name, project);

    /// <summary>
    /// The project and document a reply names, as the IDE spells them, added to <paramref name="reply"/>.
    /// </summary>
    /// <remarks>
    /// A tool normalises the name it is given, since it matches without regard to case, so the reply has to
    /// carry the normalised form: the difference between the two is the whole of what a caller cannot otherwise
    /// see (#467). Read when the reply is built, so a rename the call made is the name reported.
    /// </remarks>
    private static MutateResult Naming(MutateResult reply, DocumentIdentity? document) =>
        document is null ? reply : reply with { Project = document.Project.Name, Document = document.Name };

    /// <summary>
    /// The project and document a lookup found, a carried file included, added to <paramref name="reply"/>; the
    /// reply unchanged when it found nothing.
    /// </summary>
    private static MutateResult Naming(MutateResult reply, NamedDocument found) =>
        found.Name is null ? reply : reply with { Project = found.Project, Document = found.Name };

    /// <summary>
    /// What a document holds after a mark was set on it, for the reply's note.
    /// </summary>
    /// <remarks>
    /// A reply that mutates reports the new state, and reports it when that state is empty: "cleared" is
    /// the answer a caller who passed an empty array is owed, and an empty note would read as a tool that
    /// did nothing.
    /// </remarks>
    private static string Held(DocumentIdentity document, IReadOnlyList<int> lines, string what) =>
        lines.Count == 0
            ? $"{document.Display} now has no {what}s."
            // The numbering said in the reply too, so a caller still holding a description from before #1245, when
            // bookmarks counted from 0, reads that it is one out.
            : $"{document.Display} now has {HexIDE.IDE.MarkLineRules.Marks(what, lines)}.";

    /// <summary>
    /// A name as the document tools resolve it: a form, module or class in any loaded project, else, where the
    /// tool takes one, a carried file. See <see cref="NamedDocuments.Find"/>.
    /// </summary>
    private NamedDocument Find(string name, string? project, bool carried) =>
        NamedDocuments.Find(ctx.ProjectManager.LoadedProjects, name, project, carried);

    /// <summary>
    /// A form, UserControl or property page to show or change in the designer, or why the name is not one. See
    /// <see cref="NamedDocuments.FindDesigner"/>.
    /// </summary>
    private NamedDesigner FindDesigner(string name, string? project) =>
        NamedDocuments.FindDesigner(ctx.ProjectManager.LoadedProjects, name, project);

    /// <summary>The code editor open on this document, if any, matched by identity rather than by name.</summary>
    private CodeEditorViewModel? CodeEditorOf(DocumentIdentity document) =>
        ctx.DocumentDockService.OpenDocuments.OfType<CodeEditorViewModel>().FirstOrDefault(e => e.Identity == document);

    private RelatedDocumentEditorViewModel? CarriedEditorOf(RelatedDocumentDefinition carried) =>
        ctx.DocumentDockService.OpenDocuments.OfType<RelatedDocumentEditorViewModel>().FirstOrDefault(e => e.RelatedDocument == carried);

    /// <summary>
    /// A carried file's text as its editor holds it, or else as it is on disk; or why there is none. The text
    /// get_file_content returns, so open_file's line is checked against the lines a caller read (#572, #2403).
    /// </summary>
    private (string? Text, string? Error) CarriedTextOf(RelatedDocumentDefinition carried)
    {
        if (CarriedEditorOf(carried) is { } open)
            return (open.Document.Text, null);
        if (carried.AbsolutePath is not { } path || !File.Exists(path))
            return (null, $"'{carried.Name}' is carried by {carried.Owner.Name}, but its file is not on disk"
                          + (carried.AbsolutePath is { } missing ? $" at {missing}" : "") + ".");
        var bytes = File.ReadAllBytes(path);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return (new System.Text.UTF8Encoding(false).GetString(bom ? bytes.AsSpan(3) : bytes.AsSpan()), null);
    }

    /// <summary>
    /// Why some of <paramref name="lines"/> cannot be marked in <paramref name="document"/>, or null when all can:
    /// a line it does not have, or a new mark on a read-only line. See <see cref="HexIDE.IDE.MarkLineRules.Refusal"/>.
    /// </summary>
    /// <remarks>
    /// Lines are counted as the code window counts them, from the top of the file with the header included: in
    /// the open window's buffer, else in the model's whole file. Counting the model's code section, as this did
    /// until #273 task 3.12, gave a different range depending on whether the window happened to be open.
    /// The text is <see cref="HexIDE.IDE.DocumentContent.TextOf"/>, the one set_file_content's reply counts and
    /// get_file_content returns, so the range this accepts and the range those state cannot drift apart.
    /// </remarks>
    private string? OutsideDocument(DocumentIdentity document, int[] lines, int first, string what, IReadOnlyCollection<int>? held = null)
    {
        var text = HexIDE.IDE.DocumentContent.TextOf(document, CodeEditorOf(document));
        return HexIDE.IDE.MarkLineRules.Refusal(text, document.Display, lines, held ?? [], first, what);
    }

    // ── The recorded language-server conversation ────────────────────────────
    //
    // Four thin wrappers. Everything they do lives in HexIDE.Core's CaptureQueries, for two reasons that
    // are both requirements rather than taste: the automation server is compiled out of distributed builds
    // and the capture is not, so logic the capture needs in order to be readable belongs on the other side
    // of that line; and nothing in the tree references this executable, so anything implemented in a tool
    // body cannot be reached by any test.

    [McpServerTool(Name = "list_lsp_messages")]
    [DescribesEnum(typeof(ConversationEntryKind))]
    [DescribesEnum(typeof(ConversationDirection))]
    [DescribesEnum(typeof(ConversationOutcome), "None")]
    [Description("Lists recorded language-server envelopes — time, direction, method, id, size, outcome and latency — without content. Envelopes are recorded always, armed or not; call it with no arguments for the whole timeline.\n\nIF THE ANSWER IS EMPTY, READ 'note': it says which reason applies. The commonest is that no server has started, because a server starts on the first document of a language it claims, so open a file first.\n\n'direction' is Sent, Received (including a line the server wrote to standard error or standard output) or Local (observed, not exchanged). 'kind' is Request, Response, ErrorResponse or Notification on the wire, plus six found nowhere else: Lifecycle (a process starting or stopping, and its exit code), StandardError (a line the server wrote there, verbatim, often its only word on a crash), StandardOutput (a line a named-pipe server wrote to stdout, verbatim), NeverSent (a message this client declined or could not serialize, and why), Unconsumed (a capability the server advertised that this client does not use) and Note (the capture's own remarks, such as a frame it could not decode). 'detail' carries their text. 'outcome', on a request, is Answered, Failed (an error object), Cancelled (by this client), Abandoned (the connection went away first) or TimedOut (the local request deadline expired); absent while it waits.\n\nSequence numbers have GAPS that are not lost frames: a reply or an ending completes its request's row and uses up the sequence in 'completedSequence'. 'framesDropped' is the only report of real loss.\n\nA REQUEST'S REPLY IS ON THE REQUEST'S ROW: 'answerSizeBytes' is its size, and 'answerSequence', present only when its body was kept, is what get_lsp_message reads it by.\n\nNarrow with 'connectionId', 'method' and 'failuresOnly'. TO POLL, pass the last reply's 'latestSequence' as 'afterSequence': you get what was recorded since and, on their own rows, requests answered or ended since. 'truncated' and 'matched' say what 'limit' left out. Nothing returns bodies in bulk: list, then get_lsp_message.")]
    public async Task<LspMessagesResult> ListLspMessagesAsync(
        [Description("One server's connection, as get_lsp_capture_state lists them; omit for every connection.")]
        string? connectionId = null,
        [Description("An exact method name, such as textDocument/hover; omit for every method.")]
        string? method = null,
        [Description("True for error responses, requests that failed, were cancelled or never came back, and rows marked 'fault': a message this client could not serialize or decode, a connection it could not make, a server that went away unasked or exited non-zero.")]
        bool failuresOnly = false,
        [Description("Only what was recorded, answered or ended after this sequence: pass the last reply's 'latestSequence' to poll for what is new.")]
        long? afterSequence = null,
        [Description("How many rows at most, the newest when more match; a poll keeps the earliest changes instead, and its latestSequence continues from them. A poll keeps requests ended together together, since one ending takes one sequence for all of them, so it can return more rows than this; 'truncated' says whether any were left out. Default 200.")]
        int? limit = null,
        CancellationToken ct = default)
    {
        var page = await CaptureQueries.ListAsync(ctx.Capture, new EnvelopeFilter(
            ConnectionId: string.IsNullOrWhiteSpace(connectionId) ? null : connectionId,
            Method: string.IsNullOrWhiteSpace(method) ? null : method,
            FailuresOnly: failuresOnly,
            AfterSequence: afterSequence,
            Limit: limit ?? 200));

        var rows = page.Entries.Select(e => new LspMessageRow(
            e.Sequence,
            e.ConnectionId,
            e.Timestamp.ToString("o"),
            e.Direction.ToString(),
            e.Kind.ToString(),
            e.Method,
            e.CorrelationId,
            e.SizeBytes,
            e.Outcome == ConversationOutcome.None ? null : e.Outcome.ToString(),
            e.Elapsed?.TotalMilliseconds,
            e.Detail,
            HasBody: ctx.Capture.Body(e.ConnectionId, e.Sequence) is not null,
            AnswerSizeBytes: e.AnswerSizeBytes,
            AnswerSequence: e.AnswerSequence,
            CompletedSequence: e.CompletedSequence,
            Fault: e.Fault ? true : null)).ToArray();

        return new LspMessagesResult(
            rows, page.Matched, page.Truncated, ctx.Capture.QueueDropped, page.Note, page.LatestSequence);
    }

    [McpServerTool(Name = "get_lsp_message")]
    [Description("Returns one recorded message's body by its sequence number, as the bytes that crossed the wire. Get the sequence from list_lsp_messages; rows there carry 'hasBody', so you can tell before asking.\n\nTO READ A REPLY, pass the row's 'answerSequence' rather than its 'sequence' — a reply has no row of its own, because it completes its request's. The answer comes back with 'isAnswer' true and 'answerTo' naming the request; its 'method' is the request's method, since a response carries none on the wire.\n\nSEQUENCE NUMBERS ARE UNIQUE ACROSS THE WHOLE RECORD, not per connection — if you name the wrong connection for a real sequence, the reply says which connection it actually belongs to.\n\nBodies are kept only for a connection that has been armed (see arm_lsp_capture, or launch the IDE with --capture-lsp), except for the opening of every connection, which is always kept because a handshake cannot be captured after the fact. When there is no body, 'unavailable' explains which of the possible reasons applies as far as the record knows — including when the record cannot tell them apart, which it says rather than guessing.\n\nA body longer than the per-frame limit comes back as a head and a tail with the true length stated. It is NOT valid JSON in that case, and pretending otherwise would be a lie about what was sent. Not redacted: this is the developer's own machine, and export is where redaction belongs.")]
    public async Task<LspMessageBodyResult> GetLspMessageAsync(
        string connectionId, long sequence, CancellationToken ct)
    {
        if (await CaptureQueries.FetchAsync(ctx.Capture, connectionId, sequence) is not { } view)
        {
            return new LspMessageBodyResult(
                sequence, connectionId, null, 0, null, null, false,
                await CaptureQueries.ExplainMissingBodyAsync(ctx.Capture, connectionId, sequence));
        }

        return new LspMessageBodyResult(
            view.Sequence, view.ConnectionId, view.Method, view.TrueLength,
            view.Head, view.Tail, view.Tail is not null, null, view.IsAnswer, view.AnswerTo);
    }

    [McpServerTool(Name = "arm_lsp_capture")]
    [Description("Arms or disarms retention of message BODIES for one language-server connection, or for every connection when `connectionId` is omitted. Pass `armed` false to disarm; it defaults to true. Envelopes are always recorded whatever this says; arming decides only whether CONTENT is kept, which is the entire privacy boundary — so if you only need to know what was sent and what came back, you do not need this at all.\n\nConnection ids come from list_lsp_messages or get_lsp_capture_state; the reply lists every connection it knows with its new state, so a misspelled id is visible rather than silent.\n\nArming is session-scoped and is lost when the IDE restarts — launch with --capture-lsp to arm before the first connection is made, which is what the rebuild-and-relaunch loop needs. It cannot reach a handshake that has already happened, so a server already running keeps only what follows.")]
    public async Task<LspCaptureStateResult> ArmLspCaptureAsync(
        string? connectionId = null, bool armed = true, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            foreach (var id in ctx.Capture.ConnectionIds) ctx.Capture.Arm(id, armed);
        }
        else
        {
            ctx.Capture.Arm(connectionId, armed);
        }

        return await CaptureStateAsync();
    }

    [McpServerTool(Name = "get_lsp_capture_state")]
    [Description("Reports what is being recorded: every known language-server connection, whether its message bodies are being kept, how many envelopes it holds, and what it has had to discard. Read-only — ask this rather than arming something to find out what is armed. 'armsEveryConnection' is true when the IDE was launched with --capture-lsp, which arms connections as they appear rather than waiting to be asked.")]
    public async Task<LspCaptureStateResult> GetLspCaptureStateAsync(CancellationToken ct = default) =>
        await CaptureStateAsync();

    [McpServerTool(Name = "clear_lsp_capture")]
    [Description("Discards the recorded conversation for one connection, or for every connection when `connectionId` is omitted, and reports how many envelopes went. Whatever was armed stays armed: this throws away what has been watched, not the decision to watch. Use it between iterations so the next thing you exercise is the only thing in the record. It does not hand a connection a fresh opening allowance — a server that has been running for an hour is not new because its record is empty.")]
    public async Task<LspCaptureClearedResult> ClearLspCaptureAsync(
        string? connectionId = null, CancellationToken ct = default)
    {
        var discarded = await ctx.Capture.ClearAsync(
            string.IsNullOrWhiteSpace(connectionId) ? null : connectionId);

        return new LspCaptureClearedResult(discarded, await CaptureStateAsync());
    }


    [McpServerTool(Name = "answer_next_file_dialog")]
    [Description("Pre-answers the next file dialog the IDE opens, so a Save As / Open / Export flow can be driven end to end. A file picker is a NATIVE operating-system dialog: it is outside the control tree, dump_visual_tree cannot see it, interact cannot address it, and a modal one stops this server answering at all — so without this every feature ending in a file dialog needed a person to click.\n\nPass the absolute path the dialog should return as `path`. For an Open that lets a person select several files, pass them all as `paths` instead, one answer; a save, or an Open that takes one file, answers that as cancelled and a reply says so. Omit both to answer as CANCELLED, which is a distinct path through most of these flows and the one least likely to have been exercised by hand. The answer is consumed by ONE dialog and is not sticky: arm it immediately before the action that opens the picker, or it will be spent by whichever dialog opens first. set_file_content, set_control_property and add_control say when their save took no answer and one armed before the call is still pending. So do invoke_menu_item for Save Project / Save Project As and interact on project-save toolbar buttons. A document save reached through invoke_menu_item, press_key or interact can still be waiting on formatting when they reply: check with clear_file_dialog_answers, but a pending count then can mean the picker has not opened yet.\n\nThis does not simulate the dialog. Everything downstream of the picker — the writing, the naming, the refusals — is the same code a real click reaches; only the part a person performs is skipped. Nothing is created: name a path in a directory that exists, or the flow under test will report the failure it would really report.")]
    public Task<FileDialogAnsweredResult> AnswerNextFileDialogAsync(
        string? path = null, string[]? paths = null, CancellationToken ct = default)
    {
        // A refusal reached outside a save tool has no reply of its own, and the caller arms again next (#2408).
        var earlier = HexIDE.IDE.ScriptedFileDialogs.TakeUnreportedRefusal();
        FileDialogAnsweredResult Refuse(string error) => new(false, null, HexIDE.IDE.ScriptedFileDialogs.Pending,
            "Nothing was armed." + earlier, Error: error);

        if (paths is not null)
        {
            if (!string.IsNullOrWhiteSpace(path))
                return Task.FromResult(Refuse("Pass path for one file or paths for several, not both."));
            if (paths.Length == 0 || paths.Any(string.IsNullOrWhiteSpace))
                return Task.FromResult(Refuse("paths must name at least one file, and none of them blank. To answer "
                                              + "as cancelled, omit both path and paths."));
            HexIDE.IDE.ScriptedFileDialogs.AnswerNextWithMany(paths);
            return Task.FromResult(new FileDialogAnsweredResult(
                true, paths.Length == 1 ? paths[0] : null, HexIDE.IDE.ScriptedFileDialogs.Pending,
                (paths.Length == 1
                    ? $"The next file dialog will return '{paths[0]}'."
                    : $"The next file dialog will return these {paths.Length} files, in this order, as a person selecting "
                      + "them in an Open that allows several would. A save, or an Open that takes one file, cannot take "
                      + "them: it answers as cancelled, and a reply says so.")
                + " Nothing has been created there." + earlier,
                Paths: paths));
        }

        HexIDE.IDE.ScriptedFileDialogs.AnswerNextWith(path);

        var cancels = string.IsNullOrWhiteSpace(path);
        return Task.FromResult(new FileDialogAnsweredResult(
            true,
            cancels ? null : path,
            HexIDE.IDE.ScriptedFileDialogs.Pending,
            (cancels
                ? "The next file dialog will answer as cancelled."
                : $"The next file dialog will return '{path}'. Nothing has been created there.") + earlier,
            Paths: cancels ? null : [path!]));
    }

    [McpServerTool(Name = "clear_file_dialog_answers")]
    [Description("Discards every armed file-dialog answer. Use it after a step that did not open the dialog it was expected to, so a leftover answer cannot be spent by an unrelated save later on. Reports how many were discarded, which is also how you find out that a step you thought opened a picker did not.")]
    public Task<FileDialogAnsweredResult> ClearFileDialogAnswersAsync(CancellationToken ct = default)
    {
        var discarded = HexIDE.IDE.ScriptedFileDialogs.Clear();

        return Task.FromResult(new FileDialogAnsweredResult(
            true, null, 0,
            (discarded == 0
                ? "Nothing was armed."
                : $"Discarded {discarded} armed answer(s); the next file dialog will be shown for real.")
            + HexIDE.IDE.ScriptedFileDialogs.TakeUnreportedRefusal()));
    }

    [McpServerTool(Name = "set_lock_key")]
    [DescribesEnum(typeof(HexIDE.Runtime.BuiltinControls.LockKey))]
    [DescribesEnum(typeof(HexIDE.Runtime.BuiltinControls.LockKeySource))]
    [Description("Sets a lock key's toggle as programs running in HexIDE read it, so a StatusBar's key panels (Style 1 to 4) can be checked on and off. The machine's keyboard is untouched: press_key raises key events but can't change the toggle Windows keeps, which is what a StatusBar reads there. 'key' is CapsLock, NumLock, Insert or ScrollLock. 'state' is On, Off, or Keyboard, which returns the key to the keyboard's own toggle. Both are matched without regard to case. A state set here lasts until it is set to Keyboard or the IDE exits, and a StatusBar on screen repaints at once. Called with neither argument, nothing changes. Every reply lists all four keys, each with 'on' as a running program reads it and a 'source': Set (by this tool), Keyboard (the keyboard's toggle, on Windows), or Window (on another host, where each window follows the presses it has seen, so 'on' is null). Code that calls GetKeyState through a Declare reads the real keyboard, not this.")]
    public Task<LockKeysResult> SetLockKeyAsync(
        [Description("CapsLock, NumLock, Insert or ScrollLock.")] string? key = null,
        [Description("On, Off, or Keyboard to return the key to the keyboard's own toggle.")] string? state = null,
        CancellationToken ct = default)
    {
        static LockKeysResult Reply(bool success, string? error, string note) => new(success, error,
            Enum.GetValues<HexIDE.Runtime.BuiltinControls.LockKey>().Select(k =>
            {
                if (HexIDE.Runtime.BuiltinControls.LockKeys.Override(k) is { } set)
                    return new LockKeyRow(k, set, HexIDE.Runtime.BuiltinControls.LockKeySource.Set);
                return HexIDE.Runtime.BuiltinControls.LockKeys.Keyboard(k) is { } keyboard
                    ? new LockKeyRow(k, keyboard, HexIDE.Runtime.BuiltinControls.LockKeySource.Keyboard)
                    : new LockKeyRow(k, null, HexIDE.Runtime.BuiltinControls.LockKeySource.Window);
            }).ToArray(), note);

        const string Vocabulary = "'key' is CapsLock, NumLock, Insert or ScrollLock, and 'state' is On, Off or Keyboard";
        if (key is null && state is null)
            return Task.FromResult(Reply(true, null, "Nothing was changed. These are the four keys as a running program reads them."));
        if (key is null || state is null)
            return Task.FromResult(Reply(false,
                $"Nothing was changed: {(key is null ? "'state' was given without 'key'" : "'key' was given without 'state'")}. "
                + $"Pass both, or neither to read the keys; {Vocabulary}.", "The keys are as before."));
        // By name only: TryParse would also take "1" as NumLock.
        if (!key.Trim().All(char.IsLetter)
            || !Enum.TryParse<HexIDE.Runtime.BuiltinControls.LockKey>(key.Trim(), ignoreCase: true, out var lockKey))
            return Task.FromResult(Reply(false, $"Nothing was changed: '{key}' is not a lock key; {Vocabulary}.", "The keys are as before."));
        bool? on;
        switch (state.Trim().ToLowerInvariant())
        {
            case "on": on = true; break;
            case "off": on = false; break;
            case "keyboard": on = null; break;
            default:
                return Task.FromResult(Reply(false, $"Nothing was changed: '{state}' is not a state; {Vocabulary}.", "The keys are as before."));
        }

        HexIDE.Runtime.BuiltinControls.LockKeys.SetOverride(lockKey, on);
        return Task.FromResult(Reply(true, null, on is { } set
            ? $"{lockKey} now reads {(set ? "on" : "off")} to a running program, whatever the keyboard says, until it is set to Keyboard or the IDE exits. The machine's own keyboard is unchanged."
            : $"{lockKey} now follows the keyboard again."));
    }

    internal string ConversationExportDirectory { get; set; } = System.IO.Path.GetTempPath();

    [McpServerTool(Name = "export_lsp_conversation")]
    [Description("Writes the recorded conversation to two files in a fresh temporary directory per call, preserving earlier exports, and returns their paths: one JSON-RPC message per line, plus a manifest carrying the envelope table with timings, the limits the record was taken under, and everything it had to discard. This is the form to attach to an issue or send to whoever wrote the server.\n\nALWAYS PSEUDONYMISED. Document names, paths, workspace folders and server launch configuration are replaced with stable, session-scoped fake names — consistently, so two spellings of one name stay distinguishable and a normalisation bug survives the redaction. That includes the name of a document with no file on disk, which goes on the wire as untitled:<project>/<document>: both segments are the developer's own words and both are replaced. Extensions and drive letters survive, so what a message was about is still legible. Use get_lsp_message instead if you need the real bytes for your own inspection on this machine; that one is raw and is not for sharing. The manifest states which of the two it is, because an export that does not say is worse than one that never redacted.\n\nEvery envelope gets a line, including those whose body was never kept, because a file that omitted them would read exactly like a shorter conversation. A truncated body is written as head, tail and true length rather than as something that parses — pretending otherwise would misdescribe what was sent. Omit `connectionId` for the whole interleaved timeline.")]
    public async Task<LspExportResult> ExportLspConversationAsync(
        string? connectionId = null, CancellationToken ct = default)
    {
        // ALWAYS redacting, and the opt-out is deliberately not a parameter here. The design records that
        // a non-pseudonymising mode should exist and that its surface is an open question needing "a big
        // red flag" wherever it lands; adding a boolean to this tool would settle that question quietly,
        // by accident, in the one place with no way to show a flag. The raw path already exists for
        // inspection — get_lsp_message — so nothing is unreachable, only unshareable.
        var redactor = new ConversationRedactor(ctx.Pseudonyms);

        // No `unobservable` map: what a transport cannot show is already IN the record, written once per
        // connection as a Note envelope when it connects, so it travels in the envelope table like
        // everything else rather than being reassembled here from a second source.
        var export = await ConversationExporter.ExportAsync(
            ctx.Capture, redactor, string.IsNullOrWhiteSpace(connectionId) ? null : connectionId);

        // A fresh directory per export, under ConversationExportDirectory, so an export never replaces an earlier one
        // (#2504). The writes are still staged and flushed (#2503).
        var directory = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(
            ConversationExportDirectory, "hexide-lsp-export-" + Guid.NewGuid().ToString("N")));
        var stem = System.IO.Path.Combine(directory.FullName, "hexide_lsp_conversation");
        var messages = stem + ".jsonl";
        var manifest = stem + ".manifest.json";

        ct.ThrowIfCancellationRequested();
        AtomicFileWriter.WriteAllText(messages, export.Messages);
        ct.ThrowIfCancellationRequested();
        AtomicFileWriter.WriteAllText(manifest, export.Manifest);

        return new LspExportResult(
            messages, manifest, export.Lines, export.BodiesPresent, export.BodiesAbsent,
            export.BodiesReserialized, ctx.Pseudonyms.Assigned,
            export.Lines == 0
                ? "Nothing was recorded, so both files describe an empty conversation. Language servers "
                + "start on the first document of a language they claim — open a file and export again."
                : null);
    }

    /// <summary>What is being recorded right now, returned by both mutating capture tools.</summary>
    /// <remarks>
    /// Returned rather than left to a follow-up call because arming is invisible: a tool that answered
    /// only "done" would leave an agent unable to tell an armed connection from one whose id it had
    /// misspelled.
    /// </remarks>
    private async Task<LspCaptureStateResult> CaptureStateAsync()
    {
        var page = await CaptureQueries.ListAsync(ctx.Capture, new EnvelopeFilter(Limit: int.MaxValue));

        // The LOG's connections, not the record's. Deriving them from the envelopes present gives the
        // same answer right up until somebody clears, and then reports no connections at all while they
        // are alive and armed — measured, on the first real use of these tools.
        var connections = ctx.Capture.ConnectionIds
            .Order(StringComparer.Ordinal)
            .Select(id =>
            {
                var (envelopes, bodies, refused) = ctx.Capture.Losses(id);
                return new LspCaptureConnectionState(
                    id, ctx.Capture.IsArmed(id), page.Entries.Count(e => e.ConnectionId == id),
                    envelopes, bodies, refused);
            })
            .ToArray();

        return new LspCaptureStateResult(ctx.Capture.ArmsEveryConnection, connections);
    }
}

internal record LspExportResult(
    string MessagesPath,
    string ManifestPath,
    int Lines,
    int BodiesPresent,
    // Lines standing in for an envelope whose body was never kept or has been evicted. Counted rather
    // than omitted: a file that dropped them would read exactly like a shorter conversation.
    int BodiesAbsent,
    // Whole bodies that had to be compacted onto one line, so a reader knows those are not byte-exact.
    int BodiesReserialized,
    // Distinct values given a pseudonym. The honest way to size the disclosure: how many real names the
    // reader is NOT getting.
    long PseudonymsAssigned,
    string? Note);

internal record LspMessagesResult(
    LspMessageRow[] Messages,
    // How many matched in total, so a limited answer is visibly limited rather than quietly short.
    int Matched,
    bool Truncated,
    // Frames the capture could not keep up with. Never silent: a record that dropped some reads exactly
    // like one that had fewer to drop.
    long FramesDropped,
    // Why an empty answer is empty. A bare zero cannot be told apart from "nothing happened", "nothing
    // was configured" and "the tool is broken" — which is the ambiguity this whole capability exists to
    // remove, and the tool that removes it is the worst place to reintroduce it.
    string? Note,
    // What to pass as afterSequence to poll for what is new. Not the highest row: a request answered since comes
    // back on its own, older row, and passing that would return it again every time (#961).
    long? LatestSequence);

internal record LspMessageRow(
    long Sequence,
    string ConnectionId,
    string At,
    string Direction,
    string Kind,
    string? Method,
    string? Id,
    int SizeBytes,
    string? Outcome,
    double? ElapsedMs,
    // A short note for the entries that are not messages: an exit code, a line of standard error, the
    // capability a never-sent entry was refused for.
    string? Detail,
    bool HasBody,
    // A request and its answer are ONE row, because a timeline read in order must not show every
    // exchange twice. So the reply's own numbers hang off the request: how big it was, and — when its
    // body was kept — the sequence to pass to get_lsp_message to read it.
    int? AnswerSizeBytes = null,
    long? AnswerSequence = null,
    // Where its outcome was recorded, kept or not: why an older row came back to a poll (#961 review).
    long? CompletedSequence = null,
    // True on a row that is a failure without being a request's outcome, which failuresOnly lists (#945).
    bool? Fault = null);

internal record LspMessageBodyResult(
    long Sequence,
    string ConnectionId,
    string? Method,
    int TrueLength,
    string? Head,
    string? Tail,
    bool Truncated,
    // Why there is nothing, in the terms a reader needs. Null when there is something.
    string? Unavailable,
    // True when this is the reply half. The method is the request's, because a response carries none of
    // its own on the wire, and answerTo names which request it belongs to.
    bool IsAnswer = false,
    long? AnswerTo = null);

internal record LspCaptureStateResult(
    bool ArmsEveryConnection,
    LspCaptureConnectionState[] Connections);

internal record LspCaptureConnectionState(
    string ConnectionId,
    bool Armed,
    int Envelopes,
    long EnvelopesDropped,
    long BodiesEvicted,
    long BodiesRefused);

internal record LspCaptureClearedResult(
    int EnvelopesDiscarded,
    LspCaptureStateResult State);

/// <param name="Project">The project holding the document read, by its own name; null when no document was found.</param>
/// <param name="Document">The document's own name, as the IDE spells it rather than as the call spelled it.</param>
internal record FileContentResult(
    string? Content, bool HasUnsavedChanges, string? Error, string? Project = null, string? Document = null);

/// <inheritdoc cref="FileContentResult"/>
internal record FormControlsResult(
    string? Error, ControlInfo[] Controls, string? Project = null, string? Document = null,
    // Every row the Properties window shows for the one control asked for by name (#923), else null.
    PropertyRow[]? Properties = null,
    // What the reply alone would not say, such as that the name is a control array's.
    string? Note = null);

internal record ControlInfo(
    string Name,
    string Type,
    double Left,
    double Top,
    double Width,
    double Height,
    bool? Visible,
    bool? Enabled,
    string? Caption,
    string? Text,
    // The control this one sits inside, or null when it is on the form itself. Left/Top are measured from
    // this container's client origin, exactly as the .frm records them, so the space is self-describing.
    string? Container,
    // A control array element's Index, which the elements' shared Name alone does not tell apart; null when it is
    // no element. name(index) addresses it in get_form_controls, set_control_property and move_control (#1252 review).
    int? Index = null);

/// <param name="Scale">Image pixels per device-independent unit, the unit inspect_element's boundingRect is in.</param>
internal record SnapshotResult(
    string? Path, string? Error, string? ActiveDialog, int? Width = null, int? Height = null, double? Scale = null,
    // The inline copy's own size and pixels per device-independent unit, when one was asked for (#957).
    int? InlineWidth = null, int? InlineHeight = null, double? InlineScale = null,
    int? WindowOffsetX = null, int? WindowOffsetY = null, string? Note = null,
    string? Title = null,
    // What the image holds, so absent when there is none: a refusal said it held a client area it never captured.
    string? CaptureArea = "Window client area, without its title bar or frame; open popups are included unless the note says otherwise.");

internal record ProjectInfoResult(
    string? ProjectName,
    string? ProjectPath,
    string[] Forms,
    string[] Modules,
    // Carried files — a `RelatedDoc=` in the .vbp. Listed because they are openable and, since they are
    // the file types a configured language server exists to serve, they are exactly what needs driving
    // when verifying one.
    string[] RelatedDocuments,
    ProjectSummary[] Projects,
    string? Note);

/// <summary>One loaded project, as get_project_info lists it.</summary>
internal record ProjectSummary(
    string Name,
    string? Path,
    bool IsStartup,
    string[] Forms,
    string[] Modules,
    string[] RelatedDocuments);

internal record OpenEditorsResult(
    string[] OpenWindows,
    string? ActiveWindow,
    string? Note = null,
    DocumentTabInfo[]? Editors = null,
    DocumentTabInfo? ActiveEditor = null);

internal record DiagnosticsResult(DiagnosticItem[] Diagnostics, string[] Analysed, string? Note);

/// <remarks>
/// <c>Source</c> names the server that reported it, and matters because <c>DiagnosticLedger</c> merges
/// every source into one set: without it a caller cannot tell which of two servers watching a document
/// said this. <c>Code</c> is the rule that fired, and <c>Href</c> where that rule is documented.
/// <c>Uri</c> is the server's name for the document; <c>Project</c> and <c>Document</c> are the IDE's, and
/// are absent when that name answers to nothing loaded.
/// </remarks>
internal record DiagnosticItem(
    string Uri,
    string Message,
    string Severity,
    int Line,
    int Column,
    string? Code = null,
    string? Source = null,
    string? Href = null,
    string? Project = null,
    string? Document = null);

/// <param name="Note">
/// Something the caller should know about a write that DID happen — not an error. A silent adjustment is
/// how a form lost its identity and reached a commit; saying so costs one line.
/// </param>
/// <param name="Project">
/// The project holding the document the call named, by its own name. Null for a tool that names no document,
/// and for a call whose name found none; null is left out of the reply.
/// </param>
/// <param name="Document">The document's own name, as the IDE spells it rather than as the call spelled it.</param>
internal record MutateResult(
    bool Success, string? Error, string? Note = null, string? Project = null, string? Document = null,
    string? Printed = null);

/// <summary>
/// What <c>shutdown_ide</c> tore down on the way out. <see cref="Requested"/> is deliberately not
/// "succeeded": the reply has to be sent before the process exits, so no in-process result can honestly
/// claim it did. Poll <c>/health</c> for that.
/// </summary>
internal record ShutdownResult(bool Requested, bool ProjectStopped, int DialogsClosed, string Note);

/// <param name="Project">The project the file was added to, by its own name.</param>
/// <param name="Document">The new document's name.</param>
internal record AddFileResult(
    bool Success, string? Path, string? Error, string? Note = null, string? Project = null, string? Document = null);

/// <summary>A window's state, as <c>get_window_state</c> reports it. <c>Name</c> is what reaches it as 'window' again;
/// <c>Error</c> says why no window was read.</summary>
internal record WindowStateResult(string State, int X, int Y, int Width, int Height, string? Name = null, string? Error = null,
    double? Scale = null, WindowRect? Outer = null, WindowRect? Frame = null, WindowRect? Client = null, string? Units = null);

/// <summary>A rectangle on the screen in device-independent pixels, to a tenth (#2454).</summary>
internal record WindowRect(double X, double Y, double Width, double Height)
{
    public static WindowRect Of(Avalonia.PixelRect pixels, double scale) =>
        new(Tenth(pixels.X / scale), Tenth(pixels.Y / scale), Tenth(pixels.Width / scale), Tenth(pixels.Height / scale));

    public static WindowRect Of(Avalonia.PixelPoint origin, Avalonia.Size size, double scale) =>
        new(Tenth(origin.X / scale), Tenth(origin.Y / scale), Tenth(size.Width), Tenth(size.Height));

    private static double Tenth(double value) => Math.Round(value, 1);
}

/// <summary>What <c>set_window_state</c> did: the window's state afterwards, as <c>get_window_state</c> reports it.</summary>
internal record WindowStateChangeResult(bool Success, string? Error, WindowStateResult? Window, string? Note = null);

internal record ToolWindowInfo(string Name, bool Visible);

internal record ToolWindowsResult(ToolWindowInfo[] Tools);

internal record DocumentTabsResult(DocumentTabInfo[] Tabs);

internal record DocumentTabInfo(string Title, string Type, bool IsActive, string? Project = null, string? Document = null,
    bool? IsModified = null);

internal record UndoStateResult(
    string ActiveEditorKind,
    bool CanUndo,
    bool CanRedo,
    string? UndoDescription,
    string? RedoDescription,
    string? Project = null,
    string? Document = null);

/// <param name="Form">
/// Carried between the spawn and the save and blanked before returning -- internal plumbing, never
/// serialized to a caller. The two steps run on the UI thread and the save is awaited, so the form has to
/// survive the hop between them.
/// </param>
/// <param name="Project">The project holding the form, by its own name.</param>
/// <param name="Document">The form's own name, as the IDE spells it rather than as the call spelled it.</param>
internal record AddControlResult(bool Success, string? ControlName, string? Error,
    [property: System.Text.Json.Serialization.JsonIgnore] FormDefinition? Form = null, string? Note = null,
    string? Project = null, string? Document = null);

/// <param name="Project">The project holding the document, by name.</param>
/// <param name="Document">The document's own VB6 name, as the IDE spells it rather than as the call spelled it.</param>
/// <param name="Uri">The name a language server knows this document by.</param>
/// <param name="Note">The lines in a sentence with their numbering, as set_bookmarks gives them; none when there are none.</param>
internal record BookmarksResult(string? Project, string? Document, string? Uri, int[] Lines, string? Error, string? Note = null);

/// <param name="Project">The project holding the document, by name.</param>
/// <param name="Document">The document's own VB6 name, as the IDE spells it rather than as the call spelled it.</param>
/// <param name="Uri">The name a language server knows this document by.</param>
internal record BreakpointsResult(string? Project, string? Document, string? Uri, int[] Lines, string? Error);

/// <param name="Project">
/// The running project, by name, while paused: <c>Module</c> is a bare name, and a group may hold two documents
/// of one name, so the pair is what says which.
/// </param>
internal record DebugStateResult(
    bool Running, string State, string? StopReason, string? Module, int? Line, string? Project = null);

internal record LocalsResult(bool Success, string? Error, string? Context, LocalsRow[]? Locals, string? Project = null);

internal record LocalsRow(string Expression, string Value, string Type, bool HasChildren, LocalsRow[]? Children);

internal record EvaluateResult(bool Success, string? Error, string? Result);

/// <param name="Project">
/// The running project, by name. A run is one project, so every frame's module is in it.
/// </param>
internal record CallStackResult(bool Success, string? Error, CallStackFrameRow[] Frames, string? Project = null);

internal record CallStackFrameRow(string Proc, string Module, int Line);

internal record WatchesResult(bool Success, string? Error, WatchRow[] Watches, string? Project = null);

internal record WatchRow(string Expression, string WatchType, string Context, string Value, string Type, bool Ok);

internal record ToolboxItem(string Name, string? VBTypeName);

internal record ToolboxItemsResult(ToolboxItem[] Items);

internal record TemplateInfo(string Name, bool Supported, string Source);

internal record NewProjectTemplatesResult(TemplateInfo[] Templates);

internal record RuntimeErrorResult(bool Raised, string? Message, string? At, int Sequence, string? Module = null, int? Line = null);

/// <param name="Path">What the next dialog will return, or null when it will answer as cancelled or return several.</param>
/// <param name="Pending">How many answers are still armed, so a stale one is visible rather than latent.</param>
/// <param name="Paths">Every file the answer armed holds, in order: one for a path, several for an Open that allows
/// them (#2408). Null for a cancel.</param>
internal record FileDialogAnsweredResult(bool Success, string? Path, int Pending, string Note, string[]? Paths = null,
    string? Error = null);

/// <param name="Keys">All four lock keys after the call, whether or not it changed one.</param>
internal record LockKeysResult(bool Success, string? Error, LockKeyRow[] Keys, string Note);

/// <param name="On">The key's toggle as a running program reads it; null where each window follows its own presses.</param>
internal record LockKeyRow(HexIDE.Runtime.BuiltinControls.LockKey Key, bool? On, HexIDE.Runtime.BuiltinControls.LockKeySource Source);

// The note is written first: a client that cuts or previews a long reply keeps its start, and the note says how to
// narrow it (#1982 review).
internal record VisualTreeResult(string? Error, string? Window, UiNode? Root, UiNode[]? Matches = null,
    [property: System.Text.Json.Serialization.JsonPropertyOrder(-1)] string? Note = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    string? FocusedPath = null, string? FocusReason = null);

internal record InspectResult(string? Error, string? Window, UiNodeDetail? Element);
