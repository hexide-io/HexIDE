using System;
using HexIDE.Forms.ViewModels;
using HexIDE.Lsp;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;
using Serilog;

namespace HexIDE.IDE;

public class EditorService : IEditorService
{
    private readonly IDocumentDockService documentDockService;
    private readonly Func<CodeEditorViewModel> codeEditorViewModelFactory;
    private readonly Func<RelatedDocumentEditorViewModel> relatedDocumentEditorViewModelFactory;
    private readonly Func<FormEditViewModel> formEditViewModelFactory;
    private readonly Func<IProjectManager> projectManager;

    /// <param name="projectManager">
    /// Resolved lazily, and that is load-bearing rather than stylistic: <c>ProjectManager</c> takes an
    /// <see cref="IEditorService"/> of its own, so asking for it directly here closes a construction cycle
    /// Pure.DI resolves at compile time and cannot break. The <c>Func</c> is the back edge — the same
    /// device, and the same reason, as the note on <c>ILspWorkspace</c> in <c>DISetup</c>.
    /// </param>
    public EditorService(IDocumentDockService documentDockService,
        Func<CodeEditorViewModel> codeEditorViewModelFactory,
        Func<RelatedDocumentEditorViewModel> relatedDocumentEditorViewModelFactory,
        Func<FormEditViewModel> formEditViewModelFactory,
        Func<IProjectManager> projectManager)
    {
        this.documentDockService = documentDockService;
        this.codeEditorViewModelFactory = codeEditorViewModelFactory;
        this.relatedDocumentEditorViewModelFactory = relatedDocumentEditorViewModelFactory;
        this.formEditViewModelFactory = formEditViewModelFactory;
        this.projectManager = projectManager;
    }

    /// <inheritdoc />
    public bool NavigateTo(string uri, int line, int column)
    {
        if (string.IsNullOrWhiteSpace(uri)) return false;

        // Every loaded project, not just the startup one. A group's members live in different directories
        // and a definition can perfectly well be in a sibling project — see hexide-io/HexIDE#261.
        foreach (var project in projectManager().LoadedProjects)
        {
            foreach (var module in project.Modules)
            {
                if (!Names(uri, "module", module.Name, module.AbsolutePath)) continue;
                EditCode(module);
                PlaceCaret(vm => vm.ModuleDefinition == module, line, column);
                return true;
            }

            foreach (var form in project.Forms)
            {
                if (!Names(uri, "form", form.Name, form.AbsolutePath)) continue;
                EditCode(form);
                // EditCode(form) redirects a UserControl or PropertyPage to its module, so the editor that
                // opened may be keyed on the module rather than the form. Accept either.
                PlaceCaret(
                    vm => vm.FormDefinition == form || vm.ModuleDefinition?.FormPart == form, line, column);
                return true;
            }

            foreach (var carried in project.RelatedDocuments)
            {
                if (!Names(uri, null, carried.Name, carried.AbsolutePath)) continue;
                EditRelatedDocument(carried);
                // Carried files open in the plain-text editor, which is a different view model with no
                // caret to place. Opening it is the whole of what can be honoured here.
                return true;
            }
        }

        Log.Debug("EditorService: NavigateTo — nothing loaded answers to {Uri}", uri);
        return false;
    }

    /// <summary>
    /// Whether a URI names this document, by either of the two spellings the IDE uses.
    /// </summary>
    /// <remarks>
    /// Compared with <see cref="LspDocumentUri.AreSame"/> rather than <c>==</c>: a server is under no
    /// obligation to echo a URI back byte for byte, and a normalised drive letter is the measured case
    /// (hexide-io/HexIDE#236). A document with no file yet has no <c>file:</c> spelling at all, which is
    /// why the scheme URI is still checked first.
    /// </remarks>
    private static bool Names(string uri, string? kind, string name, string? absolutePath)
    {
        if (kind is not null && LspDocumentUri.AreSame(uri, $"vb6://{kind}/{name}")) return true;
        if (string.IsNullOrEmpty(absolutePath)) return false;

        // A path that cannot be turned into a URI is not a match, and is not an error either: it is a
        // project entry naming something this filesystem cannot express.
        try { return LspDocumentUri.AreSame(uri, LspDocumentUri.ForFile(absolutePath)); }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    /// <summary>
    /// Puts the caret on the editor that just opened, if it can be found.
    /// </summary>
    /// <remarks>
    /// Deliberately does not decide <see cref="NavigateTo"/>'s answer. That answer means "the URI named
    /// something this project holds", which is what a caller can do anything about — a caret that could
    /// not be placed is an internal disappointment, and reporting it as "nothing answers to that URI"
    /// would send the caller down a fallback path for a document that is now open in front of the user.
    /// </remarks>
    private void PlaceCaret(Func<CodeEditorViewModel, bool> matches, int line, int column)
    {
        foreach (var open in documentDockService.OpenDocuments)
        {
            if (open is not CodeEditorViewModel editor || !matches(editor)) continue;

            // Clamped rather than trusted. The position came from a server reading a buffer it may no
            // longer agree with us about, and an offset past the end throws inside AvaloniaEdit.
            if (line < 1 || line > editor.Document.LineCount) return;
            var docLine = editor.Document.GetLineByNumber(line);
            editor.CaretOffset = Math.Min(docLine.Offset + Math.Max(column - 1, 0), docLine.EndOffset);
            return;
        }
    }

    public void EditForm(FormDefinition? form)
    {
        if (form == null) return;
        Log.Debug("EditorService: EditForm({FormName})", form.Name);
        if (documentDockService.TryActivate<FormEditViewModel>(vm => vm.FormDefinition == form))
        {
            Log.Debug("EditorService: EditForm — existing editor activated");
            return;
        }
        Log.Debug("EditorService: EditForm — opening new form editor");
        documentDockService.OpenDocument(formEditViewModelFactory().Initialize(form));
    }

    /// <summary>
    /// Opens a file the project carries but does not compile, in the plain-text editor.
    ///
    /// <para>
    /// A separate door from <see cref="EditCode(ModuleDefinition)"/> on purpose: routing a README through
    /// the VB6 code editor would hand it to machinery that assumes a FormDefinition or ModuleDefinition,
    /// a VB6 language server and a faithfulness gate. None of those describe a text file.
    /// </para>
    /// </summary>
    public void EditRelatedDocument(RelatedDocumentDefinition? relatedDocument)
    {
        if (relatedDocument == null) return;

        Log.Debug("EditorService: EditRelatedDocument({Name})", relatedDocument.Name);
        if (documentDockService.TryActivate<RelatedDocumentEditorViewModel>(vm => vm.RelatedDocument == relatedDocument))
            return;

        documentDockService.OpenDocument(relatedDocumentEditorViewModelFactory().Initialize(relatedDocument));
    }

    public void EditCode(FormDefinition? form)
    {
        if (form == null) return;

        // A UserControl or PropertyPage reaches here from its designer's View Code, because the designer
        // holds module.FormPart. Route it to the MODULE door instead, so one file has one tab and one
        // buffer. Opening it here would build a form-only editor whose flush writes formPart.Code, while
        // SaveModule writes module.Code — and whichever the developer did not type into is the one that
        // reaches disk. (#152)
        //
        // Safe only because the module door now adopts the designer half; before that it was the poorer
        // initializer and this redirect would have emptied the object/event combos and broken
        // double-click-a-control-to-write-a-handler.
        foreach (var module in form.Owner.Modules)
        {
            if (module.FormPart != form) continue;
            Log.Debug("EditorService: EditCode(form={FormName}) — routing to its module", form.Name);
            EditCode(module);
            return;
        }

        Log.Debug("EditorService: EditCode(form={FormName})", form.Name);
        if (documentDockService.TryActivate<CodeEditorViewModel>(vm => vm.FormDefinition == form))
        {
            Log.Debug("EditorService: EditCode — existing editor activated");
            return;
        }
        Log.Debug("EditorService: EditCode — opening new code editor");
        documentDockService.OpenDocument(codeEditorViewModelFactory().Initialize(form));
    }

    public void EditCode(ModuleDefinition? module)
    {
        if (module == null) return;
        Log.Debug("EditorService: EditCode(module={ModuleName})", module.Name);
        if (documentDockService.TryActivate<CodeEditorViewModel>(vm => vm.ModuleDefinition == module))
        {
            Log.Debug("EditorService: EditCode — existing editor activated");
            return;
        }
        Log.Debug("EditorService: EditCode — opening new code editor");
        documentDockService.OpenDocument(codeEditorViewModelFactory().Initialize(module));
    }
}
