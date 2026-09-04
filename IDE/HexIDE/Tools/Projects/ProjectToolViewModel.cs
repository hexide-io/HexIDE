using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Utils;
using Dock.Model.Mvvm.Controls;
using PropertyChanged.SourceGenerator;
using R3;

namespace HexIDE.Tools;

public partial class ProjectToolViewModel : Tool
{
    private readonly IProjectManager projectManager;
    private readonly IEventBus eventBus;
    private readonly IProjectService projectService;
    private readonly IEditorService editorService;
    private readonly ILocalizationService localization;
    private Dictionary<ProjectDefinition, ProjectViewModel> projectToViewModel = new();

    public ObservableCollection<ProjectViewModel> LoadedProjects { get; } = new();
    public ObservableCollection<object> RootItems { get; } = new();

    [Notify] [AlsoNotify(nameof(SelectedForm), nameof(SelectedProject), nameof(SelectedFormOrFormPart), nameof(SelectedModule), nameof(ProjectPropertiesHeader), nameof(SaveFormHeader), nameof(SaveFormAsHeader), nameof(RemoveFormHeader))] private object? selectedItem;

    public FormViewModel? SelectedForm
    {
        get => selectedItem as FormViewModel;
        set => SelectedItem = value;
    }

    public ModuleViewModel? SelectedModule
    {
        get => selectedItem as ModuleViewModel;
        set => SelectedItem = value;
    }

    public ProjectViewModel? SelectedProject
    {
        get => selectedItem as ProjectViewModel;
        set => SelectedItem = value;
    }

    public bool SelectedFormOrFormPart =>
        SelectedForm != null || SelectedModule?.ModuleDefinition.FormPart != null;

    // Name-aware, localized context-menu headers (formerly AXAML StringFormat). Re-raised when the
    // selection changes (via selectedItem's AlsoNotify) and on LanguageChanged (see constructor).
    public string ProjectPropertiesHeader =>
        SelectedProject?.Name is { } n ? string.Format(localization.GetString("Str.Tool.Project.PropertiesFormat"), n) : "";
    public string SaveFormHeader =>
        SelectedForm?.Name is { } n ? string.Format(localization.GetString("Str.Tool.Project.SaveFormFormat"), n) : "";
    public string SaveFormAsHeader =>
        SelectedForm?.Name is { } n ? string.Format(localization.GetString("Str.Tool.Project.SaveFormAsFormat"), n) : "";
    public string RemoveFormHeader =>
        SelectedForm?.Name is { } n ? string.Format(localization.GetString("Str.Tool.Project.RemoveFormFormat"), n) : "";

    public DelegateCommand ViewObjectCommand { get; }

    public DelegateCommand ViewCodeCommand { get; }

    public DelegateCommand FormPropertiesCommand { get; }

    public DelegateCommand AddFormCommand { get; }

    private ProjectViewModel? GetSelectedProject()
    {
        if (SelectedProject != null)
            return SelectedProject;
        if (SelectedForm != null)
            return SelectedForm.Project;
        if (SelectedModule != null)
            return SelectedModule.Project;
        if (selectedItem is DirectoryViewModel directory)
            return directory.Project;
        return null;
    }

    public ProjectToolViewModel(IProjectManager projectManager,
        IEventBus eventBus,
        IProjectService projectService,
        IEditorService editorService,
        ILocalizationService localization)
    {
        this.projectManager = projectManager;
        this.eventBus = eventBus;
        this.projectService = projectService;
        this.editorService = editorService;
        this.localization = localization;
        localization.BindTitle(this, "Str.Tool.Project.Title");
        localization.LanguageChanged += () =>
        {
            OnPropertyChanged(nameof(ProjectPropertiesHeader));
            OnPropertyChanged(nameof(SaveFormHeader));
            OnPropertyChanged(nameof(SaveFormAsHeader));
            OnPropertyChanged(nameof(RemoveFormHeader));
        };
        CanPin = false;
        CanClose = true;

        ViewObjectCommand = new DelegateCommand(() =>
        {
            if (SelectedForm != null)
                editorService.EditForm(SelectedForm.FormDefinition);
            else if (SelectedModule != null)
                editorService.EditForm(SelectedModule.ModuleDefinition.FormPart);
        }, () => SelectedForm != null || SelectedModule?.ModuleDefinition.FormPart != null);
        ViewCodeCommand = new DelegateCommand(() =>
        {
            if (SelectedForm != null)
                editorService.EditCode(SelectedForm.FormDefinition);
            else if (SelectedModule != null)
                editorService.EditCode(SelectedModule.ModuleDefinition);
        }, () => SelectedForm != null || SelectedModule != null);
        FormPropertiesCommand = new DelegateCommand(() =>
        {
            ViewObjectCommand.Execute(null);
            ((ICommand)ApplicationCommands.OpenPropertiesCommand).Execute(null);
        }, () => SelectedForm != null);
        AddFormCommand = new DelegateCommand(() =>
        {
            var project = GetSelectedProject();
            project!.Definition.AddForm(new FormDefinition(project!.Definition, FormComponentClass.Instance, $"Form{project.Definition.Forms.Count + 1}"));
        }, () => GetSelectedProject() != null);

        projectManager.ProjectLoaded += OnProjectLoaded;
        projectManager.ProjectUnloaded += OnProjectUnloaded;
        foreach (var projDef in this.projectManager.LoadedProjects)
            OnProjectLoaded(projDef);

        projectManager.ObservePropertyChanged(x => x.StartupProject)
            .Subscribe(startupProj =>
            {
                foreach (var vm in LoadedProjects)
                    vm.IsStartupProject = vm.Definition == startupProj;
            });

        projectManager.ObservePropertyChanged(x => x.CurrentGroup)
            .Subscribe(_ => RebuildRootItems());
    }

    private void RebuildRootItems()
    {
        RootItems.Clear();
        if (projectManager.CurrentGroup != null)
            RootItems.Add(new ProjectGroupViewModel(projectManager.CurrentGroup, LoadedProjects));
        else
            foreach (var p in LoadedProjects)
                RootItems.Add(p);
    }

    private void OnSelectedItemChanged()
    {
        ViewObjectCommand.RaiseCanExecutedChanged();
        ViewCodeCommand.RaiseCanExecutedChanged();
        FormPropertiesCommand.RaiseCanExecutedChanged();
        AddFormCommand.RaiseCanExecutedChanged();
    }

    private void OnProjectLoaded(ProjectDefinition obj)
    {
        var vm = new ProjectViewModel(this, obj);
        projectToViewModel[obj] = vm;
        LoadedProjects.Add(vm);
        if (projectManager.CurrentGroup == null)
            RootItems.Add(vm);
    }

    private void OnProjectUnloaded(ProjectDefinition obj)
    {
        var vm = projectToViewModel[obj];
        LoadedProjects.Remove(vm);
        RootItems.Remove(vm);
        vm.Dispose();
        projectToViewModel.Remove(obj);
    }

    public void OpenSelected()
    {
        if (SelectedForm != null)
        {
            editorService.EditForm(SelectedForm.FormDefinition);
        }
        else if (SelectedModule != null)
        {
            if (SelectedModule.ModuleDefinition.FormPart != null)
                editorService.EditForm(SelectedModule.ModuleDefinition.FormPart);
            else
                editorService.EditCode(SelectedModule.ModuleDefinition);
        }
        else if (selectedItem is RelatedDocViewModel relatedDoc)
        {
            editorService.EditRelatedDocument(relatedDoc.Definition);
        }
        else if (selectedItem is IProjectTreeElement projectTreeElement)
        {
            projectTreeElement.IsExpanded = !projectTreeElement.IsExpanded;
        }
    }

    public void SetAsStartUp()
    {
        if (SelectedProject == null)
            return;

        projectManager.StartupProject = SelectedProject.Definition;
    }

    public void EditProjectProperties()
    {
        if (SelectedProject == null)
            return;

        projectService.EditProjectProperties(SelectedProject.Definition).ListenErrors();
    }

    public void DeleteForm()
    {
        if (SelectedForm == null)
            return;

        // TODO: this logic doesn't belong to a VM, maybe some service?
        eventBus.Publish(new FormUnloadedEvent(SelectedForm.FormDefinition));
        SelectedForm.Project.Definition.DeleteForm(SelectedForm.FormDefinition);
    }
}