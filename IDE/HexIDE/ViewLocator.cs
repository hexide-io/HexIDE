using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using HexIDE.Addins;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.Forms.Views;
using HexIDE.Forms.Views.Options;
using HexIDE.IDE;
using HexIDE.Tools;
using HexIDE.Tools.ObjectBrowser;
using HexIDE.Tools.TranslationEditor;
using HexIDE.VisualDesigner;
using HexIDE.VisualDesigner.Views;
using FormEditViewModel = HexIDE.VisualDesigner.FormEditViewModel;

namespace HexIDE;

public class ViewLocator : IDataTemplate
{
    private static Dictionary<Type, Func<Control>> templates = new();

    private static void Register<TViewModel, TView>() where TView : Control, new()
    {
        templates[typeof(TViewModel)] = () => new TView();
    }

    static ViewLocator()
    {
        Register<AddinToolWindowViewModel, AddinToolWindowView>();
        Register<CodeEditorViewModel, CodeEditorView>();
        Register<FormEditViewModel, FormEditView>();
        Register<MenuEditorViewModel, MenuEditorView>();
        Register<ToolBoxToolViewModel, ToolBoxToolView>();
        Register<ProjectToolViewModel, ProjectToolView>();
        Register<FormLayoutToolViewModel, FormLayoutToolView>();
        Register<PropertiesToolViewModel, PropertiesToolView>();
        Register<AddProcedureViewModel, AddProcedureView>();
        Register<LanguageRevertGateViewModel, LanguageRevertGateView>();
        Register<OptionsViewModel, OptionsView>();
        Register<EnvironmentGeneralPageViewModel, EnvironmentGeneralPageView>();
        Register<ThemePageViewModel, ThemePageView>();
        Register<LanguagePageViewModel, LanguagePageView>();
        Register<KeymapPageViewModel, KeymapPageView>();
        Register<ToolbarsPageViewModel, ToolbarsPageView>();
        Register<EditorGeneralPageViewModel, EditorGeneralPageView>();
        Register<EditorFormattingPageViewModel, EditorFormattingPageView>();
        Register<FormDesignerGridPageViewModel, FormDesignerGridPageView>();
        Register<AdvancedLspPageViewModel, AdvancedLspPageView>();
        Register<DeveloperPageViewModel, DeveloperPageView>();
        Register<AddinOptionsPageViewModel, AddinOptionsPageView>();
        Register<AddinConsentDialogViewModel, AddinConsentDialog>();
        Register<AddWatchDialogViewModel, AddWatchDialog>();
        Register<TrustChainViewModel, TrustChainView>();
        Register<RuntimeErrorViewModel, RuntimeErrorView>();
        Register<LocalsToolViewModel, LocalsToolView>();
        Register<WatchesToolViewModel, WatchesToolView>();
        Register<CallStackToolViewModel, CallStackToolView>();
        Register<ImmediateToolViewModel, ImmediateToolView>();
        Register<ColorPaletteToolViewModel, ColorPaletteToolView>();
        Register<ObjectBrowserToolViewModel, ObjectBrowserToolView>();
        Register<TranslationEditorViewModel, TranslationEditorView>();
        Register<NewProjectViewModel, NewProjectView>();
        Register<FindReplaceViewModel, FindReplaceView>();
        Register<SaveChangesViewModel, SaveChangesView>();
        Register<FileConflictViewModel, FileConflictView>();
        Register<ProjectPropertiesViewModel, ProjectPropertiesView>();
        Register<ReferencesViewModel, ReferencesView>();
        Register<ComponentsViewModel, ComponentsView>();
    }

    public Control? Build(object? param)
    {
        if (param != null &&
            templates.TryGetValue(param.GetType(), out var template))
            return template();
        return null;
    }

    public bool Match(object? data)
    {
        return data != null && templates.ContainsKey(data.GetType());
    }
}