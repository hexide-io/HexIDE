using System.Threading.Tasks;
using AvaloniaEdit.Document;
using Dock.Model.Mvvm.Controls;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using HexIDE.Utils;

namespace HexIDE.Tools;

/// <summary>
/// The Immediate window. Receives <c>Debug.Print</c> output (appended by MainViewViewModel) and, in Break mode,
/// EVALUATES an expression typed after <c>?</c>/<c>Print</c> (or a bare expression) against the paused frame via
/// <see cref="IDebugController.EvaluateAsync"/> — read-only (variables, operators, intrinsics; user-procedure
/// calls + assignment are rejected). Depends only on the DAP-shaped controller seam, never on interpreter types.
/// </summary>
public partial class ImmediateToolViewModel : EditorToolBase
{
    private readonly IDebugController _debugController;
    private readonly ILocalizationService _localization;

    public TextDocument Document { get; } = new();

    public ImmediateToolViewModel(ILocalizationService localization, IDebugController debugController)
    {
        _localization = localization;
        _debugController = debugController;
        localization.BindTitle(this, "Str.Tool.Immediate.Title");
        CanPin = false;
        CanClose = true;
    }

    /// <summary>True while execution is paused — the only mode Immediate evaluation is available in (v1).</summary>
    public bool IsPaused => _debugController.State == DebugState.Paused;

    /// <summary>The localized "available only in break mode" note the view prints when Enter is pressed while not paused.</summary>
    public string BreakModeOnlyNote => _localization.GetString("Str.Tool.Immediate.BreakModeOnly");

    /// <summary>Evaluate one Immediate line against the paused frame; returns the formatted result or a VB6-style
    /// error message (empty for a blank line, null when not in Break mode).</summary>
    public Task<string?> EvaluateAsync(string line) => _debugController.EvaluateAsync(line);
}
