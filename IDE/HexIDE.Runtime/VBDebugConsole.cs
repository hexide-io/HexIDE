using System;
using System.Collections.Generic;
using System.Text;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime;

/// <summary>
/// Global sink for VB6 <c>Debug.Print</c> output — the runtime's bridge to the IDE's Immediate window.
/// Mirrors the static-event pattern already used by <see cref="VBWindowContext.RunTimeError"/> /
/// <see cref="VBWindowContext.CompileError"/>: the runtime writes; the IDE subscribes and appends to the Immediate pane.
/// There is a single Immediate window (as in VB6), so a static, process-wide sink is the correct scope. Tests do not
/// use this — they capture <c>Debug.Print</c> per-instance via <see cref="IBasicStandardLibrary.DebugPrint"/>, so there
/// is no cross-test contamination.
/// </summary>
public static class VBDebugConsole
{
    private static readonly object Gate = new();
    private static readonly StringBuilder Pending = new();
    private static int column;

    /// <summary>The column the Immediate window's output has reached, which VB6 keeps from one run to the next (#1380).</summary>
    public static int Column
    {
        get { lock (Gate) return column; }
    }

    /// <summary>Raised with each piece of text as <c>Debug.Print</c> writes it, line breaks as CR LF (#1380). A statement
    /// that ends in a semicolon or a comma writes none, so its line stays open for the next.</summary>
    public static event Action<string>? Written;

    /// <summary>Raised once for each line <c>Debug.Print</c> completes, without its line break. A line left open waits for
    /// the statement that ends it.</summary>
    public static event Action<string>? Output;

    /// <summary>Text as <c>Debug.Print</c> writes it: raises <see cref="Written"/>, and <see cref="Output"/> for each line
    /// it completes.</summary>
    public static void Write(string text)
    {
        Written?.Invoke(text);
        List<string> lines = [];
        lock (Gate)
        {
            column = VbPrint.ColumnAfter(column, text);
            Pending.Append(text);
            var buffered = Pending.ToString();
            int start = 0, end;
            while ((end = buffered.IndexOf("\r\n", start, StringComparison.Ordinal)) >= 0)
            {
                lines.Add(buffered[start..end]);
                start = end + 2;
            }
            Pending.Remove(0, start);
        }
        foreach (var line in lines)
            Output?.Invoke(line);
    }

    /// <summary>A value alone on a line, as <see cref="Format"/> makes it. Not <c>Debug.Print</c>'s path, which goes through
    /// <see cref="Write"/> with Print's own spacing.</summary>
    public static void Emit(Vb6Value value) => Write(Format(value) + "\r\n");

    // The text CStr and '&' make of the value (VbDisplay): VB6's digits, E notation and date halves, in the current
    // culture. Not Print's own spacing (VbPrint). Public so the Immediate window's `?expr` result has a formatter; what
    // VB6's `?` writes is not measured.
    public static string Format(Vb6Value value) => VbDisplay.ToText(value);
}
