using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using AvaloniaEdit.Utils;
using HexIDE.Addins;
using HexIDE.Projects;
using HexIDE.Utils;
using HexIDE.IDE;
using HexIDE.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Forms.ViewModels;

public partial class NewProjectViewModel : ObservableObject, IDialog
{
    private readonly IRecentProjectsService _recentProjectsService;
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localization;

    public string Title => _localization.GetString("Str.NewProject.Msg.Title");
    public bool CanResize => false;
    public event Action<bool>? CloseRequested;

    [Notify] private ProjectTemplateViewModel? selectedTemplate;
    [Notify] private int selectedTabIndex;
    [Notify] private RecentProjectEntryViewModel? selectedRecentProject;
    [Notify] private bool dontShowOnStartup;

    /// <summary>
    /// When the dialog closes with OK, this is set to a .vbp file path
    /// if the user chose "Existing" or "Recent" tab. Null means "New" tab.
    /// </summary>
    public string? ResultFilePath { get; private set; }

    public ObservableCollection<ProjectTemplateViewModel> Templates { get; } = new();
    public ObservableCollection<RecentProjectEntryViewModel> RecentProjects { get; } = new();
    public FileBrowserViewModel FileBrowser { get; } = new();

    public DelegateCommand OkNew { get; }
    public DelegateCommand OkExisting { get; }
    public DelegateCommand OkRecent { get; }
    public DelegateCommand Cancel { get; }

    public NewProjectViewModel(IWindowManager windowManager, IRecentProjectsService recentProjectsService, ISettingsService settingsService, IPersonalityService personalityService, ILocalizationService localization, AddinProjectTemplateService? addinProjectTemplateService = null)
    {
        _recentProjectsService = recentProjectsService;
        _settingsService = settingsService;
        _localization = localization;
        DontShowOnStartup = !settingsService.PromptForProjectOnStartup;
        // New tab
        OkNew = new DelegateCommand(() =>
        {
            if (SelectedTemplate?.Supported == false)
            {
                windowManager.MessageBox(string.Format(_localization.GetString("Str.NewProject.Msg.VersionDoesNotSupport"), SelectedTemplate.Name), "HexIDE", MessageBoxButtons.Ok, MessageBoxIcon.Information);
                return;
            }
            ResultFilePath = null;
            CloseRequested?.Invoke(true);
        }, () => SelectedTemplate != null);

        // Existing tab
        OkExisting = new DelegateCommand(() =>
        {
            var path = FileBrowser.ResolveSelectedFile();
            if (path == null)
            {
                windowManager.MessageBox(_localization.GetString("Str.NewProject.Msg.FileNotFound"), _localization.GetString("Str.NewProject.Msg.OpenProjectTitle"), MessageBoxButtons.Ok, MessageBoxIcon.Information);
                return;
            }
            ResultFilePath = path;
            CloseRequested?.Invoke(true);
        }, () => !string.IsNullOrWhiteSpace(FileBrowser.FileName));

        // Recent tab
        OkRecent = new DelegateCommand(() =>
        {
            if (SelectedRecentProject == null)
                return;
            if (!File.Exists(SelectedRecentProject.FullPath))
            {
                windowManager.MessageBox(string.Format(_localization.GetString("Str.NewProject.Msg.ProjectNoLongerExists"), SelectedRecentProject.FullPath), _localization.GetString("Str.NewProject.Msg.OpenProjectTitle"), MessageBoxButtons.Ok, MessageBoxIcon.Information);
                recentProjectsService.Remove(SelectedRecentProject.FullPath);
                RecentProjects.Remove(SelectedRecentProject);
                return;
            }
            ResultFilePath = SelectedRecentProject.FullPath;
            CloseRequested?.Invoke(true);
        }, () => SelectedRecentProject != null);

        Cancel = new DelegateCommand(() => CloseRequested?.Invoke(false), () => true);

        // Populate templates
        Templates.AddRange(personalityService.AvailableProjectTypes.Select(template => new ProjectTemplateViewModel(template)));
        if (addinProjectTemplateService is not null)
            Templates.AddRange(addinProjectTemplateService.Templates.Select(template => new ProjectTemplateViewModel(template)));
        SelectedTemplate = Templates.FirstOrDefault();

        // Populate recent projects
        foreach (var path in recentProjectsService.GetRecent())
            RecentProjects.Add(new RecentProjectEntryViewModel(path));

        // Wire file name changes to OkExisting can-execute
        FileBrowser.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FileBrowser.FileName))
                OkExisting.RaiseCanExecutedChanged();
        };
    }

    private void OnSelectedTemplateChanged()
    {
        OkNew.RaiseCanExecutedChanged();
    }

    private void OnSelectedRecentProjectChanged()
    {
        OkRecent.RaiseCanExecutedChanged();
    }

    private void OnDontShowOnStartupChanged()
    {
        _settingsService.PromptForProjectOnStartup = !DontShowOnStartup;
        _settingsService.Save();
    }
}
