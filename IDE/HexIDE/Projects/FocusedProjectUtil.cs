using System;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Components;
using HexIDE.VisualDesigner;
using PropertyChanged.SourceGenerator;
using R3;

namespace HexIDE.Projects;

public partial class FocusedProjectUtil : IFocusedProjectUtil
{
    private readonly IDocumentDockService documentDockService;
    private readonly IProjectManager projectManager;
    private readonly ArchiveProjectService? archives;
    [Notify] private ProjectDefinition? focusedProject;
    [Notify] private ProjectDefinition? focusedOrStartupProject;
    [Notify] private FormDefinition? focusedForm;
    [Notify] private string focusedComponentPosition = "0, 0";
    [Notify] private string focusedComponentSize = "0, 0";

    private System.IDisposable? activeWindowDisposable;

    public FocusedProjectUtil(IDocumentDockService documentDockService,
        IProjectManager projectManager, ArchiveProjectService? archives = null)
    {
        this.documentDockService = documentDockService;
        this.projectManager = projectManager;
        this.archives = archives;
        if (archives != null) archives.FocusChanged += Update;
        documentDockService.ObservePropertyChanged(x => x.ActiveDocument)
            .Subscribe(_ => Update());
        projectManager.ObservePropertyChanged(x => x.StartupProject)
            .Subscribe(_ => Update());
    }

    private void Update()
    {
        if (archives?.IsActive == true)
        {
            FocusedProject = null;
            FocusedForm = null;
        }
        else if (documentDockService.ActiveDocument is FormEditViewModel formEdit)
        {
            FocusedForm = formEdit.FormDefinition;
            FocusedProject = FocusedForm?.Owner;
        }
        else if (documentDockService.ActiveDocument is CodeEditorViewModel codeEdit)
        {
            FocusedForm = codeEdit.FormDefinition;
            FocusedProject = codeEdit.ModuleDefinition?.Owner ?? FocusedForm?.Owner;
        }
        else
        {
            FocusedProject = null;
            FocusedForm = null;
        }
        FocusedOrStartupProject = archives?.IsActive == true || documentDockService.ActiveDocument is ArchiveDocumentViewModel
            ? null : FocusedProject ?? projectManager.StartupProject;


        if (archives?.IsActive != true && documentDockService.ActiveDocument is FormEditViewModel formEdit2)
        {
            UpdateControlPositionBinding(formEdit2);
        }
        else
        {
            UpdateControlPositionBinding(null);
        }
    }

    private void UpdateControlPositionBinding(FormEditViewModel? formEditor)
    {
        FocusedComponentPosition = "0, 0";
        FocusedComponentSize = "0, 0";
        activeWindowDisposable?.Dispose();
        activeWindowDisposable = null;

        if (formEditor == null)
            return;

        // Use the Properties window's units and precision, including the frame round a form (#1641).
        var left = formEditor.ObservePropertyChanged(x => x.SelectedComponent)
            .Where(x => x != null)
            .SelectMany(x => x!.ObservePropertyChanged(y => y.RelativeLeft)
                .Select(pixels => Shown(formEditor, x!.Instance, VBProperties.LeftProperty, pixels)));
        var top = formEditor.ObservePropertyChanged(x => x.SelectedComponent)
            .Where(x => x != null)
            .SelectMany(x => x!.ObservePropertyChanged(y => y.RelativeTop)
                .Select(pixels => Shown(formEditor, x!.Instance, VBProperties.TopProperty, pixels)));
        var width = formEditor.ObservePropertyChanged(x => x.SelectedComponent)
            .Where(x => x != null)
            .SelectMany(x => x!.ObservePropertyChanged(y => y.Width)
                .Select(pixels => Shown(formEditor, x!.Instance, VBProperties.WidthProperty, pixels)));
        var height = formEditor.ObservePropertyChanged(x => x.SelectedComponent)
            .Where(x => x != null)
            .SelectMany(x => x!.ObservePropertyChanged(y => y.Height)
                .Select(pixels => Shown(formEditor, x!.Instance, VBProperties.HeightProperty, pixels)));

        activeWindowDisposable = new CompositeDisposable(left.CombineLatest(top, (a, b) => $"{a}, {b}")
            .Subscribe(position => FocusedComponentPosition = position), width.CombineLatest(height, (a, b) => $"{a} x {b}")
            .Subscribe(size => FocusedComponentSize = size));
    }

    // As the Properties window shows it, or the model's pixels where the positions service can't say (#5758).
    private static double Shown(FormEditViewModel editor, ComponentInstance instance, PropertyClass property, double pixels) =>
        editor.FormDefinition is { } form && DesignerClassCalls.ShownPosition(form, instance, property, pixels) is { } shown
            ? shown.Value
            : pixels;
}
