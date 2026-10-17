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
public sealed record VbFileDialogRequest(
    bool Save,
    string? Title,
    IReadOnlyList<(string Description, string Patterns)> Filters,
    int FilterIndex,
    string InitialDirectory,
    string FileName,
    string DefaultExtension,
    bool OverwritePrompt);

/// <summary>The file a dialog chose, and the filter it was chosen under, from 1, where the host can tell.</summary>
public sealed record VbFileDialogAnswer(string Path, int? FilterIndex);

/// <summary>A font, as a CommonDialog's <c>ShowFont</c> hands it to the host and takes it back (#1084).</summary>
public sealed record VbFontChoice(string Name, float Size, bool Bold, bool Italic);
