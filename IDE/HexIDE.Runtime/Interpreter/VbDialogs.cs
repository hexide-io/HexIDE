using HexIDE.IDE;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// The dialogs a CommonDialog shows, where the application supplies its own (#1084). The IDE does, so that a run's dialogs
/// are the IDE's: its font dialog, and its pickers, which an automation client can answer with
/// <c>answer_next_file_dialog</c>. A form's window is made deep in the loader, with no way to hand it anything, which is
/// why this is a setting of the application rather than of the window.
/// </summary>
public static class HostDialogs
{
    public static Func<Task<bool>>? Print { get; set; }

    public static Func<int, Task<int?>>? Color { get; set; }

    /// <summary>Open and Save As, or null for the platform's picker.</summary>
    public static Func<VbFileDialogRequest, Task<VbFileDialogAnswer?>>? File { get; set; }

    /// <summary>The font dialog, or null where the application has none.</summary>
    public static Func<VbFontChoice, Task<VbFontChoice?>>? Font { get; set; }
}
