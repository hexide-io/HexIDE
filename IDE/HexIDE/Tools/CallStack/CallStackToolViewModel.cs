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
    private readonly Debugging.ExternalDebugService? externalDebugger;
    private bool synchronizingSelection;
    [Notify] private CallStackRow? selectedFrame;
    /// <summary>The call-stack rows, current frame first (marked with an arrow).</summary>
    [Notify] private IReadOnlyList<CallStackRow> _frames = Array.Empty<CallStackRow>();

    public CallStackToolViewModel(ILocalizationService localization, IDebugController debugController, Debugging.ExternalDebugService? externalDebugger = null)
    {
        this.externalDebugger = externalDebugger;
        localization.BindTitle(this, "Str.Tool.CallStack.Title");
        CanPin = false;
        CanClose = true;

        // The controller raises Stopped/Continued on the interpreter's own (UI-thread) execution, so update
        // synchronously (same contract the Locals pane + current-line bridge rely on).
        debugController.Stopped += _ => Refresh(debugController);
        debugController.Continued += () => Frames = Array.Empty<CallStackRow>();
        if (externalDebugger is not null)
        {
            IReadOnlyList<System.Text.Json.JsonElement>? displayedFrames = null;
            externalDebugger.Changed += () =>
            {
                synchronizingSelection = true;
                try
                {
                    if (externalDebugger.Frames.Count == 0 || !ReferenceEquals(displayedFrames, externalDebugger.Frames))
                    {
                        displayedFrames = externalDebugger.Frames;
                        Frames = displayedFrames.Select((frame, index) => new CallStackRow(
                            (index == 0 ? "→ " : "   ") + frame.GetProperty("name").GetString(), frame.GetProperty("line").GetInt32(),
                            externalDebugger.Stop, frame.GetProperty("id").GetInt32())).ToArray();
                    }
                    var id = externalDebugger.SelectedFrame?.GetProperty("id").GetInt32();
                    SelectedFrame = id is null ? null : Frames.FirstOrDefault(row => row.FrameId == id);
                }
                finally { synchronizingSelection = false; }
            };
        }
    }

    private void OnSelectedFrameChanged()
    {
        if (synchronizingSelection || externalDebugger is null || SelectedFrame is not { Stop: { } stop, FrameId: { } id })
            return;
        if (externalDebugger.CanSelectFrame(stop, id))
        {
            _ = externalDebugger.SelectFrameAsync(stop, id);
            return;
        }
        // A row of a stop the debugger has moved on from, which it declines: the list goes back to the frame the
        // debugger is in, so it shows what the debugger uses, and a select of the row reads back as not taken (#1182).
        synchronizingSelection = true;
        try
        {
            var current = externalDebugger.SelectedFrame?.GetProperty("id").GetInt32();
            SelectedFrame = current is null ? null : Frames.FirstOrDefault(row => row.FrameId == current && row.Stop == externalDebugger.Stop);
        }
        finally { synchronizingSelection = false; }
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
public sealed record CallStackRow(string Frame, int Line, HexIDE.Dap.DapStop? Stop = null, int? FrameId = null)
{
    // The name a row is shown and addressed by in dump_visual_tree: "Main, line 6". The record's own text carried the
    // debug session's id and stop generation, so a row's path went stale at every stop (#1182).
    public override string ToString() => $"{Frame.TrimStart('→', ' ')}, line {Line}";

    // Each row is its own frame: a recursive call shows the same procedure and line twice, and a list told the two were
    // equal selected the first for the second (#1182 review).
    public bool Equals(CallStackRow? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}
