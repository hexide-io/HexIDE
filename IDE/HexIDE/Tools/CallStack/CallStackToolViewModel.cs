using System;
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Mvvm.Controls;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools;

/// <summary>
/// The Call Stack window — the chain of running procedure activations at a break (deepest/current first). Rebuilds
/// from <see cref="IDebugController.GetCallStack"/> on every <see cref="IDebugController.Stopped"/> and clears on
/// <see cref="IDebugController.Continued"/>. Depends only on the DAP-shaped controller seam.
/// </summary>
public partial class CallStackToolViewModel : Tool
{
    /// <summary>The call-stack rows, current frame first (marked with an arrow).</summary>
    [Notify] private IReadOnlyList<CallStackRow> _frames = Array.Empty<CallStackRow>();

    public CallStackToolViewModel(ILocalizationService localization, IDebugController debugController)
    {
        localization.BindTitle(this, "Str.Tool.CallStack.Title");
        CanPin = false;
        CanClose = true;

        // The controller raises Stopped/Continued on the interpreter's own (UI-thread) execution, so update
        // synchronously (same contract the Locals pane + current-line bridge rely on).
        debugController.Stopped += _ => Refresh(debugController);
        debugController.Continued += () => Frames = Array.Empty<CallStackRow>();
    }

    private void Refresh(IDebugController debugController)
    {
        var stack = debugController.GetCallStack();
        Frames = stack
            .Select((f, i) => new CallStackRow((i == 0 ? "→ " : "   ") + $"{f.Module}.{f.ProcName}", f.Line))
            .ToList();
    }
}

/// <summary>One Call Stack row: the frame ("Module.Procedure", current marked) and its 1-based line.</summary>
public sealed record CallStackRow(string Frame, int Line);
