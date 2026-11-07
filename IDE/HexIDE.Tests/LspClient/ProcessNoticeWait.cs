namespace HexIDE.Tests.LspClient;

internal static class ProcessNoticeWait
{
    // Child startup and Process callbacks share the runner's CPU under load (#892). A wait that runs out says what did
    // arrive, so a notice that never came can be told from one that came late (#2685).
    internal static async Task ForAsync(Task signal, string expected, CancellationToken cancellationToken,
        TimeSpan? timeout = null, Func<string>? received = null)
    {
        try
        {
            await signal.WaitAsync(timeout ?? TimeSpan.FromMinutes(2), cancellationToken);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"Timed out waiting for {expected}; the child process or its callbacks "
                + "may not have been scheduled. This does not establish a missing exit code."
                + (received is null ? "" : $" Received meanwhile: {received()}"), ex);
        }
    }

    /// <summary>The notices a test has collected, as a timeout message lists them.</summary>
    internal static string Describe(IReadOnlyList<HexIDE.Lsp.TransportNotice> seen, Lock gate)
    {
        lock (gate)
            return seen.Count == 0 ? "nothing" : string.Join("; ", seen.Select(n => $"{n.Kind} '{n.Text}'"));
    }
}
