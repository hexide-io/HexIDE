using System.Runtime.CompilerServices;

namespace HexIDE.IDE;

/// <summary>
/// The IDE's own record of the windows it made for a running program's MsgBox and InputBox (#3513, #5616), by their content.
/// The IDE tells a program's boxes from its own prompts by this record, never by asking the engine: a box is the program's
/// only because the IDE made its window through <see cref="IProgramDialogWindows"/>.
/// </summary>
internal static class ProgramBoxes
{
    private static readonly ConditionalWeakTable<object, Kind> Made = new();

    private sealed record Kind(ProgramBox Box);

    /// <summary>Records <paramref name="content"/> as a program's box of <paramref name="box"/>'s kind.</summary>
    internal static void Record(object content, ProgramBox box) => Made.AddOrUpdate(content, new Kind(box));

    /// <summary>Whether <paramref name="content"/> is a box the IDE made a window for, for a running program.</summary>
    internal static bool IsProgramBox(object? content) => content is not null && Made.TryGetValue(content, out _);

    /// <summary>Whether <paramref name="content"/> is a running program's MsgBox.</summary>
    internal static bool IsMessageBox(object? content) =>
        content is not null && Made.TryGetValue(content, out var kind) && kind.Box == ProgramBox.Message;
}
