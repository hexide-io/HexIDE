using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

/// <summary>What a CommonDialog's <c>ShowOpen</c> or <c>ShowSave</c> asks the host for (#1084).</summary>
/// <param name="Save">Save As rather than Open.</param>
/// <param name="Title">The dialog's caption, or null for the platform's own.</param>
/// <param name="Filters">The <c>Filter</c> property's pairs, a description and its patterns (<c>*.txt;*.log</c>).</param>
/// <param name="FilterIndex">The pair shown first, from 1. 0 means the first.</param>
/// <param name="InitialDirectory">The folder it opens in, or empty for the platform's choice.</param>
/// <param name="FileName">The name it suggests, or empty.</param>
/// <param name="DefaultExtension">The extension a name typed without one is given, without its dot, or empty.</param>
/// <param name="OverwritePrompt">Ask before choosing a file that exists: <c>cdlOFNOverwritePrompt</c>.</param>
/// <param name="AllowMultiple">Allow several Open picks: <c>cdlOFNAllowMultiselect</c>.</param>
public sealed record VbFileDialogRequest(
    bool Save,
    string? Title,
    IReadOnlyList<(string Description, string Patterns)> Filters,
    int FilterIndex,
    string InitialDirectory,
    string FileName,
    string DefaultExtension,
    bool OverwritePrompt,
    bool AllowMultiple = false);

/// <summary>The first file, selected filter (from 1) where known, and any further files in selection order.</summary>
public sealed record VbFileDialogAnswer(string Path, int? FilterIndex, IReadOnlyList<string>? AdditionalPaths = null);

/// <summary>A font, as a CommonDialog's <c>ShowFont</c> hands it to the host and takes it back (#1084).</summary>
public sealed record VbFontChoice(string Name, float Size, bool Bold, bool Italic);

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
