using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
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
    private readonly ArchiveProjectService? archives;
    private bool synchronizingArchiveFolder;
    public bool HasArchiveSelection => SelectedItem is ArchiveTreeNode;
    [Notify] private bool showHiddenArchiveEntries;
    private void OnShowHiddenArchiveEntriesChanged() => RebuildRootItems();
    private Dictionary<ProjectDefinition, ProjectViewModel> projectToViewModel = new();

    public ObservableCollection<ProjectViewModel> LoadedProjects { get; } = new();
    public ObservableCollection<object> RootItems { get; } = new();

    [Notify] [AlsoNotify(nameof(HasArchiveSelection), nameof(SelectedForm), nameof(SelectedProject), nameof(SelectedFormOrFormPart), nameof(SelectedModule), nameof(ProjectPropertiesHeader), nameof(SaveFormHeader), nameof(SaveFormAsHeader), nameof(RemoveFormHeader), nameof(HasRemovableSelection))] private object? selectedItem;

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
    public bool HasRemovableSelection => SelectedForm != null || SelectedModule != null;
    public string RemoveFormHeader =>
        (SelectedForm?.Name ?? SelectedModule?.Name) is { } n ? string.Format(localization.GetString("Str.Tool.Project.RemoveFormFormat"), n) : "";

    public DelegateCommand ViewObjectCommand { get; }

    public DelegateCommand ViewCodeCommand { get; }

    public DelegateCommand FormPropertiesCommand { get; }

    public DelegateCommand SaveFormCommand { get; }

    public DelegateCommand SaveFormAsCommand { get; }

    public DelegateCommand AddFormCommand { get; }
    public DelegateCommand SynchronizeArchiveFolderCommand { get; }

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
        ILocalizationService localization, ArchiveProjectService? archives = null)
    {
        this.projectManager = projectManager;
        this.eventBus = eventBus;
        this.projectService = projectService;
        this.editorService = editorService;
        this.localization = localization;
        this.archives = archives;
        if (archives != null) archives.Changed += RebuildRootItems;
        localization.BindTitle(this, "Str.Tool.Project.Title");
        localization.LanguageChanged += () =>
        {
            RebuildRootItems();
            OnPropertyChanged(nameof(ProjectPropertiesHeader));
            OnPropertyChanged(nameof(SaveFormHeader));
            OnPropertyChanged(nameof(SaveFormAsHeader));
            OnPropertyChanged(nameof(RemoveFormHeader));
        };
        CanPin = false;
        CanClose = true;
        SynchronizeArchiveFolderCommand = new DelegateCommand(() => SynchronizeArchiveFolder().ListenErrors(),
            () => archives != null && HasArchiveSelection && !synchronizingArchiveFolder);

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
            else if (SelectedItem is ArchiveTreeNode node) archives?.OpenDocument(node.Entry);
        }, () => SelectedForm != null || SelectedModule != null || HasArchiveSelection);
        FormPropertiesCommand = new DelegateCommand(() =>
        {
            ViewObjectCommand.Execute(null);
            ((ICommand)ApplicationCommands.OpenPropertiesCommand).Execute(null);
        }, () => SelectedForm != null);
        SaveFormCommand = new DelegateCommand(() =>
        {
            if (SelectedForm != null)
                projectService.SaveForm(SelectedForm.FormDefinition, false).ListenErrors();
        }, () => SelectedForm != null);
        SaveFormAsCommand = new DelegateCommand(() =>
        {
            if (SelectedForm != null)
                projectService.SaveForm(SelectedForm.FormDefinition, true).ListenErrors();
        }, () => SelectedForm != null);
        AddFormCommand = new DelegateCommand(() => AddFormAsync(GetSelectedProject()!.Definition).ListenErrors(),
            () => GetSelectedProject() != null);

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
        var previous = RootItems.OfType<ArchiveTreeNode>().SelectMany(WalkArchive).ToDictionary(n => n.Entry.Identity);
        var selected = (SelectedItem as ArchiveTreeNode)?.Entry.Identity;
        RootItems.Clear();
        if (projectManager.CurrentGroup != null)
            RootItems.Add(new ProjectGroupViewModel(projectManager.CurrentGroup, LoadedProjects));
        else
            foreach (var p in LoadedProjects)
                RootItems.Add(p);
        if (archives != null)
            foreach (var project in archives.Projects)
            {
                var root = new ArchiveTreeNode(project.Root, ShowHiddenArchiveEntries,
                    string.Format(localization.GetString("Str.Archive.ProjectFormat"), project.Name));
                foreach (var node in WalkArchive(root))
                    if (previous.TryGetValue(node.Entry.Identity, out var old)) node.IsExpanded = old.IsExpanded;
                RootItems.Add(root);
            }
        if (selected != null)
            SelectedItem = RootItems.OfType<ArchiveTreeNode>().SelectMany(WalkArchive)
                .FirstOrDefault(n => n.Entry.Identity.Equals(selected));
    }

    private static IEnumerable<ArchiveTreeNode> WalkArchive(ArchiveTreeNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in WalkArchive(child)) yield return descendant;
    }

    private void OnSelectedItemChanged()
    {
        eventBus.Publish(new ModulePropertiesSelectedEvent(SelectedModule?.ModuleDefinition));
        archives?.SelectTreeEntry(HasArchiveSelection);
        ViewObjectCommand.RaiseCanExecutedChanged();
        ViewCodeCommand.RaiseCanExecutedChanged();
        FormPropertiesCommand.RaiseCanExecutedChanged();
        AddFormCommand.RaiseCanExecutedChanged();
        SynchronizeArchiveFolderCommand.RaiseCanExecutedChanged();
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
        if (selectedItem is ArchiveTreeNode archive && archive.Entry is HexIDE.TwinBasic.ArchiveProjectDocument)
        {
            archives?.OpenDocument(archive.Entry);
        }
        else if (SelectedForm != null)
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
        else if (SelectedProject != null)
        {
            // Takes the double-click away from expand/collapse, which is what VB6 did here and what the
            // fallthrough below still does for every other container. Deliberate: the chevron is what
            // expansion is for, and fidelity does not extend to spending the primary gesture on a
            // second way to do it. A project group keeps the old behaviour until it has something of
            // its own to open into (.vbg is not read yet).
            editorService.EditProject(SelectedProject.Definition);
        }
        else if (selectedItem is IProjectTreeElement projectTreeElement)
        {
            projectTreeElement.IsExpanded = !projectTreeElement.IsExpanded;
        }
    }

    public void InspectArchive()
    {
        if (selectedItem is ArchiveTreeNode node) archives?.OpenDocument(node.Entry);
    }

    public void ReloadArchive()
    {
        if (selectedItem is ArchiveTreeNode node) archives?.Reload(node.Entry.Identity.ContainerPath).ListenErrors();
    }

    public void CloseArchive()
    {
        if (selectedItem is ArchiveTreeNode node) archives?.Close(node.Entry.Identity.ContainerPath).ListenErrors();
    }

    /// <summary>
    /// Add Form, as Project > Add Form does it: the next free name, the file written, the designer opened.
    /// </summary>
    /// <remarks>
    /// This was a second Add Form of its own, which put a form with no file into the model and opened nothing (#468).
    /// The name is the lowest unused across every kind, and passes over one whose file is already in the folder (#738).
    /// </remarks>
    private async Task AddFormAsync(ProjectDefinition project)
    {
        var form = await projectService.AddNewForm(project, projectService.NextFreeFormName(project));
        editorService.EditForm(form);
    }

    public async Task SynchronizeArchiveFolder()
    {
        if (archives == null || synchronizingArchiveFolder || selectedItem is not ArchiveTreeNode node) return;
        synchronizingArchiveFolder = true;
        SynchronizeArchiveFolderCommand.RaiseCanExecutedChanged();
        try { await archives.SynchronizeFolder(node.Entry.Identity.ContainerPath); }
        finally
        {
            synchronizingArchiveFolder = false;
            SynchronizeArchiveFolderCommand.RaiseCanExecutedChanged();
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

    public void DeleteForm() => RemoveSelectedDocument().ListenErrors();

    public async Task RemoveSelectedDocument()
    {
        var selection = SelectedItem;
        var document = SelectedForm is { } form ? DocumentIdentity.For(form.FormDefinition)
            : SelectedModule is { } module ? DocumentIdentity.For(module.ModuleDefinition) : null;
        if (document == null) return;

        await projectService.RemoveDocument(document);
        if (SelectedItem == selection && !DocumentLookup.DocumentsOf(document.Project).Contains(document))
            SelectedItem = null;
    }
}
