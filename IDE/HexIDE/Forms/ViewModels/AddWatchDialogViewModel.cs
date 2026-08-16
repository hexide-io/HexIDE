using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexIDE.Debugging;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Forms.ViewModels;

/// <summary>
/// VB6's Add Watch / Edit Watch dialog (Debug → Add Watch / Ctrl+W). Captures an Expression, shows the Context it
/// was added in, and a Watch Type — Watch Expression / Break When Value Is True / Break When Value Has Changed. OK
/// adds a new <see cref="WatchExpression"/> (or rewrites the one being edited, in place) in the
/// <see cref="WatchService"/>; Cancel/Escape discards. In P6a all three types are storable/displayable; the two
/// break types act at the gate in P6b.
/// </summary>
public partial class AddWatchDialogViewModel : ObservableObject, IDialog
{
    private readonly ILocalizationService _localization;
    private readonly WatchService _watchService;
    private readonly WatchExpression? _editing;

    public bool CanResize => false;
    public event Action<bool>? CloseRequested;

    public AddWatchDialogViewModel(ILocalizationService localization, WatchService watchService, string context,
        string? presetExpression = null, WatchExpression? editing = null)
    {
        _localization = localization;
        _watchService = watchService;
        Context = context;
        _editing = editing;
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

    public string Context { get; }

    [ObservableProperty] private string _expression = string.Empty;
    [ObservableProperty] private bool _isExpression = true;
    [ObservableProperty] private bool _isBreakWhenTrue;
    [ObservableProperty] private bool _isBreakWhenChanged;

    private WatchType SelectedType =>
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
                _watchService.Add(new WatchExpression(expr, SelectedType, Context));
            }
            else
            {
                // Edit in place, then fire a Replace at the same index so the row rebuilds WITHOUT reordering the list
                // (WatchExpression isn't observable + the row caches its text, so a plain mutation wouldn't refresh).
                _editing.Expression = expr;
                _editing.Type = SelectedType;
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
