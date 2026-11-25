using HexIDE.IDE;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// The dialogs a CommonDialog shows, where the application supplies its own (#1084). The IDE does, so that a run's dialogs
/// are the IDE's: its font dialog, and its pickers, which an automation client can answer with
/// <c>answer_next_file_dialog</c>. A form's window is made deep in the loader, with no way to hand it anything, which is
/// why this is a setting of the application rather than of the window.
/// </summary>
/// <remarks>
/// <b>Only the run engine the IDE registered sets these (#5616),</b> so they're set inside the runtime alone: no add-in can
/// point a running program's dialogs anywhere. The engine puts back whatever it set once it's withdrawn, so nothing it
/// installed outlives it, and a made EXE, where no engine sets them, keeps the runtime's own.
/// </remarks>
public static class HostDialogs
{
    public static Func<Task<bool>>? Print { get; internal set; }

    public static Func<int, Task<int?>>? Color { get; internal set; }

    /// <summary>Open and Save As, or null for the platform's picker.</summary>
    public static Func<VbFileDialogRequest, Task<VbFileDialogAnswer?>>? File { get; internal set; }

    /// <summary>The font dialog, or null where the application has none.</summary>
    public static Func<VbFontChoice, Task<VbFontChoice?>>? Font { get; internal set; }

    /// <summary>
    /// Where a form's MsgBox and InputBox windows are made, or null for windows of the runtime's own (#5616). The IDE makes
    /// them, so it can tell a program's boxes from its own prompts by what it made, without asking the provider.
    /// </summary>
    public static HexIDE.IDE.IProgramDialogWindows? BoxWindows { get; internal set; }
}
