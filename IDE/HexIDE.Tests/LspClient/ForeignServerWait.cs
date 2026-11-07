using System.Diagnostics;
using HexIDE.Conversations;

namespace HexIDE.Tests.LspClient;

internal static class ForeignServerWait
{
    public static async Task<T> ForDiagnostics<T>(
        Task<T> diagnostics, ConversationLog capture, string connectionId, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            await capture.DrainAsync();
            var entries = capture.Snapshot(connectionId);
            var exit = entries.FirstOrDefault(e =>
                e.Kind == ConversationEntryKind.Lifecycle && e.Fault
                && e.Detail is not null
                && e.Detail.StartsWith("process exited with code ", StringComparison.Ordinal));
            if (exit is not null)
            {
                // Disconnect can mark the request abandoned before the exit notice is read.
                var requests = entries.Where(e =>
                    e.Sequence < exit.Sequence && e.Direction == ConversationDirection.Sent
                    && e.Kind == ConversationEntryKind.Request
                    && e.Outcome is ConversationOutcome.None or ConversationOutcome.Abandoned)
                    .Select(e => $"during request '{e.Method}' (id {e.CorrelationId})");
                var context = string.Join(", ", requests);
                if (context.Length == 0) context = "no outstanding request recorded";
                throw new InvalidOperationException(
                    $"Foreign server '{connectionId}' {exit.Detail}; {context}. The conversation was: "
                    + string.Join(" | ", entries.Select(e =>
                        $"{e.Sequence} {e.Direction} {e.Kind} {e.Method} {e.Outcome} {e.Detail}".Trim())));
            }

            if (diagnostics.IsCompleted) return await diagnostics;
            var remaining = timeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException();
            await Task.WhenAny(diagnostics, Task.Delay(
                remaining < TimeSpan.FromMilliseconds(50) ? remaining : TimeSpan.FromMilliseconds(50),
                TestContext.Current.CancellationToken));
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        }
    }
}
