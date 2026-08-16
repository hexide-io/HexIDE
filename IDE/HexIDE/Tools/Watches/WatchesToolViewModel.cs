using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using HexIDE.Debugging;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools;

/// <summary>
/// The Watches window — VB6's Debug → Watches. A tree of user-defined watch expressions (Expression / Value / Type /
/// Context), each re-evaluated against the paused frame on every <see cref="IDebugController.Stopped"/> via
/// <see cref="IDebugController.EvaluateWatchAsync"/>, and blanked to "&lt;Out of context&gt;" on
/// <see cref="IDebugController.Continued"/>. Object/array/UDT watches expand exactly like Locals (shared
/// <see cref="LocalsVariableNode"/>). Watches themselves live in the session-scoped <see cref="WatchService"/>; this
/// window renders them and hosts Add/Edit/Delete. Reads live execution state only — no static analysis (in-bounds
/// under the CST-not-AST limit). P6a is display-only; the Break-When-True/Changed gate behaviour is P6b.
/// </summary>
public partial class WatchesToolViewModel : Tool
{
    private readonly ILocalizationService _localization;
    private readonly WatchService _watchService;
    private readonly IDebugController _debug;
    private readonly IWindowManager _windowManager;

    [Notify] private ObservableCollection<WatchRowViewModel> _rows = new();
    [Notify] private WatchRowViewModel? _selectedRow;

    public ICommand EditWatchCommand { get; }
    public ICommand DeleteWatchCommand { get; }

    /// <summary>The backing watch store — used by the MCP <c>add_watch</c>/<c>get_watches</c> tools and tests.</summary>
    public WatchService Service => _watchService;

    public WatchesToolViewModel(ILocalizationService localization, WatchService watchService,
        IDebugController debugController, IWindowManager windowManager)
    {
        _localization = localization;
        _watchService = watchService;
        _debug = debugController;
        _windowManager = windowManager;
        localization.BindTitle(this, "Str.Tool.Watches.Title");
        CanPin = false;
        CanClose = true;

        EditWatchCommand = new AsyncRelayCommand(EditSelected);
        DeleteWatchCommand = new RelayCommand(DeleteSelected);

        RebuildRows();
        // The controller raises Stopped/Continued on the interpreter's own (UI-thread) execution, so we are on the UI
        // thread and the walk is frozen — the async watch eval completes synchronously against the paused frame.
        watchService.Watches.CollectionChanged += (_, _) =>
        {
            RebuildRows();
            if (_debug.State == DebugState.Paused)
                EvaluateAll();
        };
        debugController.Stopped += _ => EvaluateAll();
        debugController.Continued += () => Blank();
    }

    private void RebuildRows()
        => Rows = new ObservableCollection<WatchRowViewModel>(
            _watchService.Watches.Select(w => new WatchRowViewModel(w, _localization)));

    // async void: fired from the synchronous Stopped event; awaits the (frozen-frame) evaluation on the UI thread.
    private async void EvaluateAll()
    {
        foreach (var row in Rows)
            row.Update(await _debug.EvaluateWatchAsync(row.Watch.Expression));
    }

    private void Blank()
    {
        foreach (var row in Rows)
            row.Update(null);
    }

    // The context a new watch records: the paused Module.Procedure, else the "(All Procedures)" placeholder.
    private string CurrentContext()
        => _debug.GetLocals()?.Context ?? _localization.GetString("Str.Dialog.AddWatch.AllContext");

    /// <summary>Open the Add Watch dialog (optionally pre-filled — QuickWatch passes the caret/selected expression).</summary>
    public Task OpenAddWatchDialog(string? presetExpression = null)
        => _windowManager.ShowDialog(new AddWatchDialogViewModel(
            _localization, _watchService, CurrentContext(), presetExpression, editing: null));

    /// <summary>Open the Edit Watch dialog for the selected row (VB6 Ctrl+W); no-op if nothing is selected.</summary>
    public Task EditSelected()
        => SelectedRow is { } row
            ? _windowManager.ShowDialog(new AddWatchDialogViewModel(
                _localization, _watchService, row.Watch.Context, presetExpression: null, editing: row.Watch))
            : Task.CompletedTask;

    /// <summary>Remove the selected watch from the store (the row disappears via CollectionChanged).</summary>
    public void DeleteSelected()
    {
        if (SelectedRow is { } row)
            _watchService.Remove(row.Watch);
    }
}
