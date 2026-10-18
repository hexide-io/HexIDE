using System.Threading.Tasks;
using AvaloniaEdit.Document;
using Dock.Model.Mvvm.Controls;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using HexIDE.Utils;

namespace HexIDE.Tools;

/// <summary>
/// The Immediate window. Receives <c>Debug.Print</c> output (appended by MainViewViewModel) and, in Break mode,
/// evaluates a line through <see cref="IDebugController.EvaluateAsync"/> or the active external adapter.
/// External lines use DAP's REPL context in the selected frame and may have adapter-defined side effects.
/// </summary>
public partial class ImmediateToolViewModel : EditorToolBase
{
    private readonly IDebugController _debugController;
    private readonly ILocalizationService _localization;
    private readonly Debugging.ExternalDebugService? externalDebugger;

    public TextDocument Document { get; } = new();

    public ImmediateToolViewModel(ILocalizationService localization, IDebugController debugController, Debugging.ExternalDebugService? externalDebugger = null)
    {
        _localization = localization;
        _debugController = debugController;
        this.externalDebugger = externalDebugger;
        localization.BindTitle(this, "Str.Tool.Immediate.Title");
        CanPin = false;
        CanClose = true;
    }

    /// <summary>True while execution is paused — the only mode Immediate evaluation is available in (v1).</summary>
    public bool IsPaused => externalDebugger?.IsActive == true
        ? externalDebugger.CanEvaluate : _debugController.State == DebugState.Paused;

    /// <summary>The localized "available only in break mode" note the view prints when Enter is pressed while not paused.</summary>
    public string BreakModeOnlyNote => _localization.GetString("Str.Tool.Immediate.BreakModeOnly");

    /// <summary>Evaluate one Immediate line against the paused frame; returns the formatted result or a VB6-style
    /// error message (empty for a blank line, null when not in Break mode).</summary>
    public async Task<string?> EvaluateAsync(string line)
    {
        if (externalDebugger?.IsActive != true) return await _debugController.EvaluateAsync(line);
        if (string.IsNullOrWhiteSpace(line)) return "";
        if (!externalDebugger.CanEvaluate) return BreakModeOnlyNote;
        var result = await externalDebugger.EvaluateAsync(line);
        return result.Expired ? _localization.GetString("Str.Tool.Immediate.Expired") : result.Text;
    }
}
