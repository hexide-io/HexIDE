using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexIDE.Debugging;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Forms.ViewModels;

public sealed record WatchContextModule(string Name, IReadOnlyList<string> Procedures);

/// <summary>
/// VB6's Add Watch / Edit Watch dialog (Debug → Add Watch / Ctrl+W). Captures an Expression, its module and
/// procedure, and a Watch Type — Watch Expression / Break When Value Is True / Break When Value Has Changed. OK
/// adds a new <see cref="WatchExpression"/> (or rewrites the one being edited, in place) in the
/// <see cref="WatchService"/>; Cancel/Escape discards. In P6a all three types are storable/displayable; the two
/// break types act at the gate in P6b.
/// </summary>
public partial class AddWatchDialogViewModel : ObservableObject, IDialog
{
    private readonly ILocalizationService _localization;
    private readonly WatchService _watchService;
    private readonly WatchExpression? _editing;
    private readonly string _context;

    public bool CanResize => false;
    public event Action<bool>? CloseRequested;

    public AddWatchDialogViewModel(ILocalizationService localization, WatchService watchService, string context,
        string? presetExpression = null, WatchExpression? editing = null, bool expressionOnly = false, IReadOnlyList<WatchContextModule>? modules = null)
    {
        _localization = localization;
        _watchService = watchService;
        _context = context;
        Modules = (modules ?? []).ToList();
        var moduleName = context == WatchExpression.AllProceduresContext ? null : context.Split('.')[0];
        var procedureName = context.Contains('.') ? context[(context.IndexOf('.') + 1)..] : null;
        // The stored context stays choosable when the lists can't name it, as when a module's symbols are unavailable:
        // otherwise OK on an unchanged Edit would store the module alone, dropping the watch's procedure.
        var index = moduleName is null ? -1 : Modules.FindIndex(m => m.Name == moduleName);
        if (moduleName is not null && index < 0)
            Modules.Add(new WatchContextModule(moduleName, procedureName is null ? [] : [procedureName]));
        else if (index >= 0 && procedureName is not null && !Modules[index].Procedures.Contains(procedureName))
            Modules[index] = Modules[index] with { Procedures = [.. Modules[index].Procedures, procedureName] };
        _selectedModule = Modules.FirstOrDefault(m => m.Name == moduleName);
        RefillProcedures();
        _selectedProcedure = procedureName ?? AllProcedures;
        AllowsContextSelection = !expressionOnly;
        var weak = new WeakReference<AddWatchDialogViewModel>(this);
        localization.LanguageChanged += () =>
        {
            if (weak.TryGetTarget(out var dialog))
            {
                dialog.OnPropertyChanged(nameof(Context));
                dialog.RefillProcedures();
            }
        };
        _editing = editing;
        AllowsBreakConditions = !expressionOnly;
        if (editing is not null)
        {
            _expression = editing.Expression;
            _isExpression = editing.Type == WatchType.Expression;
            _isBreakWhenTrue = editing.Type == WatchType.BreakWhenTrue;
            _isBreakWhenChanged = editing.Type == WatchType.BreakWhenChanged;
        }
        else if (!string.IsNullOrEmpty(presetExpression))
        {
            _expression = presetExpression!;
        }
    }

    public string Title => _localization.GetString(
        _editing is null ? "Str.Dialog.AddWatch.Title" : "Str.Dialog.AddWatch.EditTitle");

    public string Context => _context == WatchExpression.AllProceduresContext
        ? _localization.GetString("Str.Dialog.AddWatch.AllContext") : _context;
    public bool AllowsBreakConditions { get; }
    public bool AllowsContextSelection { get; }
    public List<WatchContextModule> Modules { get; }
    public System.Collections.ObjectModel.ObservableCollection<string> Procedures { get; } = new();
    private string AllProcedures => _localization.GetString("Str.Dialog.AddWatch.AllContext");
    [ObservableProperty] private WatchContextModule? _selectedModule;
    [ObservableProperty] private string? _selectedProcedure;

    partial void OnSelectedModuleChanged(WatchContextModule? value) => RefillProcedures();

    private void RefillProcedures()
    {
        var all = SelectedProcedure is null || Procedures.IndexOf(SelectedProcedure) == 0;
        var previous = SelectedProcedure;
        Procedures.Clear();
        Procedures.Add(AllProcedures);
        if (SelectedModule is { } module)
            foreach (var procedure in module.Procedures) Procedures.Add(procedure);
        SelectedProcedure = !all && previous is not null && Procedures.Contains(previous) ? previous : AllProcedures;
    }

    private string StoredContext => !AllowsContextSelection || SelectedModule is null ? _context
        : SelectedProcedure is null || Procedures.IndexOf(SelectedProcedure) <= 0 ? SelectedModule.Name
        : $"{SelectedModule.Name}.{SelectedProcedure}";

    [ObservableProperty] private string _expression = string.Empty;
    [ObservableProperty] private bool _isExpression = true;
    [ObservableProperty] private bool _isBreakWhenTrue;
    [ObservableProperty] private bool _isBreakWhenChanged;

    private WatchType SelectedType => !AllowsBreakConditions ? WatchType.Expression :
        IsBreakWhenTrue ? WatchType.BreakWhenTrue :
        IsBreakWhenChanged ? WatchType.BreakWhenChanged :
        WatchType.Expression;

    [RelayCommand]
    private void Ok()
    {
        var expr = Expression?.Trim() ?? string.Empty;
        if (expr.Length > 0)
        {
            if (_editing is null)
            {
                _watchService.Add(new WatchExpression(expr, SelectedType, StoredContext));
            }
            else
            {
                // Edit in place, then fire a Replace at the same index so the row rebuilds WITHOUT reordering the list
                // (WatchExpression isn't observable + the row caches its text, so a plain mutation wouldn't refresh).
                _editing.Expression = expr;
                _editing.Type = SelectedType;
                _editing.Context = StoredContext;
                int i = _watchService.Watches.IndexOf(_editing);
                if (i >= 0)
                    _watchService.Watches[i] = _editing;
            }
        }
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
