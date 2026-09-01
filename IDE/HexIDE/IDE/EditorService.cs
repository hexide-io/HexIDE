using System;
using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;
using Serilog;

namespace HexIDE.IDE;

public class EditorService : IEditorService
{
    private readonly IDocumentDockService documentDockService;
    private readonly Func<CodeEditorViewModel> codeEditorViewModelFactory;
    private readonly Func<FormEditViewModel> formEditViewModelFactory;

    public EditorService(IDocumentDockService documentDockService,
        Func<CodeEditorViewModel> codeEditorViewModelFactory,
        Func<FormEditViewModel> formEditViewModelFactory)
    {
        this.documentDockService = documentDockService;
        this.codeEditorViewModelFactory = codeEditorViewModelFactory;
        this.formEditViewModelFactory = formEditViewModelFactory;
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
