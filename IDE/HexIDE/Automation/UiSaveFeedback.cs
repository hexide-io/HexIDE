#if DEBUG
using System.Windows.Input;
using HexIDE.IDE;

namespace HexIDE.Automation;

/// <summary>Answer feedback for routed saves, including document saves that wait for formatting.</summary>
public static class UiSaveFeedback
{
    private static readonly System.Threading.AsyncLocal<DocumentSaveWatch?> Current = new();

    public static DocumentSaveWatch BeginDocumentSave() => new();

    internal static void Started(Task task) => Current.Value?.Tasks.Add(task);

    /// <summary>Captures only saves started by this action's execution context, including its pointer release.</summary>
    public sealed class DocumentSaveWatch : IDisposable
    {
        private readonly DocumentSaveWatch? previous = Current.Value;
        private readonly System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
        private readonly ScriptedFileDialogs.CancelWatch completed = ScriptedFileDialogs.WatchForUiProjectSave();
        private readonly ScriptedFileDialogs.CancelWatch running = ScriptedFileDialogs.WatchForUiProjectSave("the document save is still running");
        internal readonly System.Collections.Generic.List<Task> Tasks = [];

        internal DocumentSaveWatch() => Current.Value = this;

        public async Task<string?> AfterAsync(string? note, System.Threading.CancellationToken ct = default,
            TimeSpan? budget = null)
        {
            if (Tasks.Count == 0) return note;
            var pending = Task.WhenAll(Tasks);
            var remaining = (budget ?? TimeSpan.FromSeconds(2)) - elapsed.Elapsed;
            try
            {
                if (!pending.IsCompleted && remaining <= TimeSpan.Zero) throw new TimeoutException();
                await pending.WaitAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, ct);
            }
            catch (TimeoutException) when (!pending.IsCompleted)
            {
                return running.After(Lead(note) + "Document save is still running; the wait budget expired.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested && !pending.IsCompleted)
            {
                return running.After(Lead(note) + "Document save is still running; the wait was cancelled.");
            }
            catch (OperationCanceledException)
            {
                return completed.After(Lead(note) + "Document save completed: cancelled."
                    + (completed.AnswerTaken ? " An armed file-dialog answer was taken." : ""));
            }
            catch (Exception)
            {
                // The original task still reaches ListenErrors; report the failed save rather than losing its reply.
                return completed.After(Lead(note) + "Document save failed; see the IDE log.");
            }
            return completed.After(Lead(note) + "Document save completed."
                + (completed.AnswerTaken ? " An armed file-dialog answer was taken." : ""));
        }

        // The tool's own detail need not end a sentence ("the caret is at offset 485"), so close it first.
        private static string Lead(string? note) => (note?.TrimEnd() ?? "") switch
        {
            "" => "",
            var text when text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?') => text + " ",
            var text => text + ". ",
        };

        public void Dispose() => Current.Value = previous;
    }

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
