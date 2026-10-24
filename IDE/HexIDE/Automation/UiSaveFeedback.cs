#if DEBUG
using System.Windows.Input;
using HexIDE.IDE;

namespace HexIDE.Automation;

/// <summary>
/// Pending-answer feedback only for the project commands measured to reach their picker synchronously (#1335).
/// SaveCommand/SaveAsCommand belong to the focused editor, whose formatting can defer the picker. Their routed
/// handlers discard the save Task, so a pending answer there cannot yet mean that no picker will open.
/// </summary>
public static class UiSaveFeedback
{
    public static ScriptedFileDialogs.CancelWatch? Watch(ICommand? command) =>
        ReferenceEquals(command, ApplicationCommands.SaveProjectCommand)
        || ReferenceEquals(command, ApplicationCommands.SaveProjectAsCommand)
            ? ScriptedFileDialogs.WatchForUiProjectSave() : null;

    /// <summary>The menu tool's Execute and reply, shared with the headless timing/regression tests.</summary>
    public static string Execute(ICommand command, object? parameter, string note)
    {
        var watch = Watch(command);
        command.Execute(parameter);
        return watch?.After(note) ?? note;
    }

    internal static InteractOutcome After(ScriptedFileDialogs.CancelWatch? watch, InteractOutcome outcome) =>
        watch is { } saved && outcome.Success
            ? outcome with { Detail = saved.After(outcome.Detail) }
            : outcome;
}
#endif
