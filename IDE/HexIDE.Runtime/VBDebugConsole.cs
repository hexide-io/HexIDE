using System;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime;

/// <summary>
/// Global sink for VB6 <c>Debug.Print</c> output — the runtime's bridge to the IDE's Immediate window.
/// Mirrors the static-event pattern already used by <see cref="VBWindowContext.RunTimeError"/> /
/// <see cref="VBWindowContext.CompileError"/>: the runtime raises <see cref="Output"/>; the IDE subscribes and
/// appends to the Immediate pane. There is a single Immediate window (as in VB6), so a static, process-wide
/// sink is the correct scope. Tests do not use this — they capture <c>Debug.Print</c> per-instance via
/// <see cref="IBasicStandardLibrary.DebugPrint"/>, so there is no cross-test contamination.
/// </summary>
public static class VBDebugConsole
{
    /// <summary>Raised once per <c>Debug.Print</c> call. The argument is the already-formatted display line.</summary>
    public static event Action<string>? Output;

    /// <summary>Format a value the way <c>Debug.Print</c> displays it and raise <see cref="Output"/>.</summary>
    public static void Emit(Vb6Value value) => Output?.Invoke(Format(value));

    // Matches how the interpreter stringifies for '&' concatenation (the boxed CLR value's ToString):
    // strings print verbatim, Boolean as True/False, numbers via invariant ToString. Public so the Immediate
    // window's `?expr` result formats identically to Debug.Print (same buffer, same conventions).
    public static string Format(Vb6Value value) => value.Value?.ToString() ?? "";
}
