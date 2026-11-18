using System.Collections.ObjectModel;
using System.Collections.Generic;
using System;
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
using HexIDE.Dap;

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
    private readonly ExternalDebugService? externalDebugger;
    public WatchDialogContextSource? DialogContextSource { get; init; }
    private bool showingExternal;
    private long generation;
    private bool refreshing;
    private (DapStop? Stop, int? Frame, long Version, bool Ready) displayed;
    public WatchService ExternalService { get; } = new();
    private WatchService ActiveService => showingExternal ? ExternalService : _watchService;

    [Notify] private ObservableCollection<WatchRowViewModel> _rows = new();
    [Notify] [AlsoNotify(nameof(SelectedRow))] private object? _selectedItem;
    public WatchRowViewModel? SelectedRow => SelectedItem as WatchRowViewModel;

    public ICommand EditWatchCommand { get; }
    public ICommand DeleteWatchCommand { get; }

    /// <summary>The backing watch store — used by the MCP <c>add_watch</c>/<c>get_watches</c> tools and tests.</summary>
    public WatchService Service => _watchService;

    public WatchesToolViewModel(ILocalizationService localization, WatchService watchService,
        IDebugController debugController, IWindowManager windowManager, ExternalDebugService? externalDebugger = null)
    {
        _localization = localization;
        _watchService = watchService;
        _debug = debugController;
        _windowManager = windowManager;
        this.externalDebugger = externalDebugger;
        showingExternal = externalDebugger?.IsActive == true;
        localization.BindTitle(this, "Str.Tool.Watches.Title");
        CanPin = false;
        CanClose = true;

        EditWatchCommand = new AsyncRelayCommand(EditSelected);
        DeleteWatchCommand = new RelayCommand(DeleteSelected);

        RebuildRows();
        localization.LanguageChanged += () =>
        {
            foreach (var row in Rows)
            {
                row.RefreshContext();
                row.RefreshLocalization();
            }
        };
        // The controller raises Stopped/Continued on the interpreter's own (UI-thread) execution, so we are on the UI
        // thread and the walk is frozen — the async watch eval completes synchronously against the paused frame.
        watchService.Watches.CollectionChanged += (_, _) =>
        {
            if (showingExternal) return;
            RebuildRows();
            if (_debug.State == DebugState.Paused)
                EvaluateAll();
        };
        debugController.Stopped += _ => EvaluateAll();
        debugController.Continued += () => Blank();
        ExternalService.Watches.CollectionChanged += (_, _) =>
        {
            if (!showingExternal) return;
            RebuildRows();
            RefreshExternal(force: true);
        };
        if (externalDebugger is not null) externalDebugger.Changed += () => RefreshExternal();
    }

    // Stable ordinal ordering matches the measured VB6 rows (#4543); the store keeps added order.
    public IEnumerable<WatchRowViewModel> BuildInterpreterRows()
        => _watchService.Watches.Select(w => new WatchRowViewModel(w, _localization))
            .OrderBy(row => row.Context, StringComparer.Ordinal)
            .ThenBy(row => row.Expression, StringComparer.Ordinal);

    private void RebuildRows()
    {
        SelectedItem = null;
        Rows = new ObservableCollection<WatchRowViewModel>(
            showingExternal ? ExternalService.Watches.Select(w => new WatchRowViewModel(w, _localization))
                : BuildInterpreterRows());
        foreach (var row in Rows)
            row.Update(null, inactive: !showingExternal && !_debug.IsSessionActive);
    }

    private void RefreshExternal(bool force = false)
    {
        var active = externalDebugger?.IsActive == true;
        if (active != showingExternal) { showingExternal = active; RebuildRows(); force = true; }
        var next = (externalDebugger?.Stop, externalDebugger?.SelectedFrame?.GetProperty("id").GetInt32(),
            externalDebugger?.WatchVersion ?? 0, active && externalDebugger!.CanEvaluate);
        if (!force && next == displayed) return;
        displayed = next;
        ++generation;
        Blank();
        if (showingExternal) foreach (var row in Rows) row.SetExternalContext(CurrentContext());
        if (displayed.Ready && !refreshing) _ = RefreshExternalAsync();
    }

    private async Task RefreshExternalAsync()
    {
        refreshing = true;
        try
        {
            while (showingExternal && displayed is { Ready: true, Stop: { } stop, Frame: { } frame })
            {
                var version = generation;
                foreach (var row in Rows.ToArray())
                {
                    if (version != generation) break;
                    try
                    {
                        var value = await externalDebugger!.EvaluateWatchAsync(stop, frame, row.Expression);
                        if (version != generation) break;
                        row.UpdateExternal(value, stop, externalDebugger);
                    }
                    catch (Exception exception)
                    {
                        if (version != generation) break;
                        row.UpdateError(exception.Message);
                    }
                }
                if (version == generation) return;
            }
        }
        finally { refreshing = false; }
    }

    // async void: fired from the synchronous Stopped event; awaits the (frozen-frame) evaluation on the UI thread.
    private async void EvaluateAll()
    {
        if (showingExternal) return;
        foreach (var row in Rows)
            row.Update(await _debug.EvaluateScopedWatchAsync(row.Watch.Expression, row.Watch.Context));
    }

    private void Blank()
    {
        foreach (var row in Rows)
            row.Update(null, inactive: !showingExternal && _debug.State == DebugState.Stopped);
    }

    // The paused frame context; the interpreter dialog source supplies the active code window defaults.
    private string CurrentContext()
        => (showingExternal ? externalDebugger?.SelectedFrame?.GetProperty("name").GetString() : _debug.GetLocals()?.WatchContext)
            ?? WatchExpression.AllProceduresContext;

    /// <summary>Open the Add Watch dialog (optionally pre-filled — QuickWatch passes the caret/selected expression).</summary>
    public async Task OpenAddWatchDialog(string? presetExpression = null)
    {
        var context = CurrentContext();
        WatchContextModule[] modules = [];
        if (!showingExternal && DialogContextSource is { } source)
            (modules, context) = await source.ReadAsync(context);
        await _windowManager.ShowDialog(new AddWatchDialogViewModel(
            _localization, ActiveService, context, presetExpression, expressionOnly: showingExternal, modules: modules));
    }

    /// <summary>Open the Edit Watch dialog for the selected row.</summary>
    public async Task EditSelected()
    {
        if (SelectedRow is not { } row) return;
        var modules = !showingExternal && DialogContextSource is { } source
            ? (await source.ReadAsync(CurrentContext())).Modules : [];
        await _windowManager.ShowDialog(new AddWatchDialogViewModel(
            _localization, ActiveService, row.Watch.Context, editing: row.Watch, expressionOnly: showingExternal, modules: modules));
    }

    /// <summary>Remove the selected watch from the store (the row disappears via CollectionChanged).</summary>
    public void DeleteSelected()
    {
        if (SelectedRow is { } row)
            ActiveService.Remove(row.Watch);
    }
}
