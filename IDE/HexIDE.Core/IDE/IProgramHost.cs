using System.Threading.Tasks;

namespace HexIDE.IDE;

/// <summary>
/// What a run engine asks of the IDE that hosts a running program (#4066, step 2a of the run side of #3411's split): the
/// single-view form window, CommonDialog's dialogs, a program's MsgBox and InputBox, and the engine's two failure texts
/// (#4068 completes operation 1).
/// </summary>
/// <remarks>
/// An engine outside this repository inherits exactly this surface, so it stays to these operations: no other window, service
/// locator, event bus or localization lookup (the supervisor's condition on #3411's shape). A new need is a new operation
/// with its own case, not a passthrough.
/// </remarks>
public interface IProgramHost
{
    /// <summary>Make the host window for one form. The runtime receives only that form's own window.</summary>
    IHostedForm CreateForm(HostedFormRequest request);

    /// <summary>A CommonDialog's ShowOpen or ShowSave. Null when cancelled.</summary>
    Task<VbFileDialogAnswer?> FileDialog(VbFileDialogRequest request);

    /// <summary>A CommonDialog's ShowPrinter. False when cancelled.</summary>
    Task<bool> PrintDialog();

    /// <summary>A CommonDialog's ShowColor, from an initial colour. Null when cancelled.</summary>
    Task<int?> ColorDialog(int initial);

    /// <summary>A CommonDialog's ShowFont. Null when cancelled.</summary>
    Task<VbFontChoice?> FontDialog(VbFontChoice initial);

    /// <summary>A program's MsgBox, kept apart from the IDE's own prompts (#3513).</summary>
    Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxLayout layout);

    /// <summary>A program's InputBox, kept apart from the IDE's own prompts (#3513). Null when cancelled.</summary>
    Task<string?> InputBox(string prompt, string? title, string defaultText);

    /// <summary>The text for a startup form that failed to load, in the IDE's language.</summary>
    string StartupFormFailedToLoad(string formName, string description);

    /// <summary>The heading for a construct the engine doesn't implement yet, in the IDE's language.</summary>
    string NotImplementedYet { get; }
}
