using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using Serilog;
using System;
using HexIDE.Tools;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

/// <summary>
/// Manages the modeless Find/Replace dialog lifecycle.
/// Reuses a single ViewModel instance so search state (text, options) persists.
/// </summary>
public class FindReplaceService : IFindReplaceService
{
    private readonly IWindowManager _windowManager;
    private readonly IDocumentDockService _documentDockService;
    private readonly ILocalizationService _localization;
    private readonly FindProjectDocuments _projects;
    private FindReplaceViewModel? _viewModel;
    private bool _isOpen;
    private readonly Func<ProjectToolViewModel> _explorer;
    private bool _subscribed;

    public FindReplaceService(IWindowManager windowManager, IDocumentDockService documentDockService,
        ILocalizationService localization, IProjectManager projects, IEditorService editors, Func<ProjectToolViewModel> explorer)
    {
        _windowManager = windowManager;
        _documentDockService = documentDockService;
        _localization = localization;
        _projects = new FindProjectDocuments(documentDockService, projects, editors, () => SelectedProject(explorer()));
        documentDockService.PropertyChanged += (_, _) => _viewModel?.RefreshScopes();
        projects.PropertyChanged += (_, _) => _viewModel?.RefreshScopes();
        _explorer = explorer;
    }

    private static ProjectDefinition? SelectedProject(ProjectToolViewModel explorer) => explorer.SelectedItem switch
    {
        ProjectViewModel project => project.Definition,
        FormViewModel form => form.Project.Definition,
        ModuleViewModel module => module.Project.Definition,
        RelatedDocViewModel related => related.Project.Definition,
        DirectoryViewModel directory => directory.Project?.Definition,
        _ => null
    };

    public void ShowFind()
    {
        Log.Debug("FindReplaceService: ShowFind");
        EnsureDialog();
        _viewModel!.ShowReplace = false;
    }

    public void ShowReplace()
    {
        Log.Debug("FindReplaceService: ShowReplace");
        EnsureDialog();
        _viewModel!.ShowReplace = true;
    }

    public void FindNext()
    {
        Log.Debug("FindReplaceService: FindNext (search='{SearchText}')", _viewModel?.SearchText ?? "(no dialog)");
        if (_viewModel is not null && _viewModel.SearchText.Length > 0)
            _viewModel.FindNextCommand.Execute(null);
    }

    private void EnsureDialog()
    {
        if (_isOpen && _viewModel is not null)
        {
            Log.Debug("FindReplaceService: Reusing existing dialog");
            return;
        }

        if (!_subscribed)
        {
            _explorer().PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProjectToolViewModel.SelectedItem)) _viewModel?.RefreshScopes();
            };
            _subscribed = true;
        }
        Log.Debug("FindReplaceService: Creating new dialog");
        _viewModel = new FindReplaceViewModel(_windowManager, _documentDockService, _localization, _projects);
        _isOpen = true;

        // ShowWindow is modeless — fire and forget, track close
        _ = ShowAndTrackAsync();
    }

    private async System.Threading.Tasks.Task ShowAndTrackAsync()
    {
        await _windowManager.ShowWindow(_viewModel!);
        Log.Debug("FindReplaceService: Dialog closed");
        _isOpen = false;
    }
}
