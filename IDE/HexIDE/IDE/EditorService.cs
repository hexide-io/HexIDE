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
