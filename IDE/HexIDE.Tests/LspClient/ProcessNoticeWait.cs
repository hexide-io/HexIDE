namespace HexIDE.Tests.LspClient;

internal static class ProcessNoticeWait
{
    // Child startup and Process callbacks share the runner's CPU under load (#892).
    internal static async Task ForAsync(Task signal, string expected, CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        try
        {
            await signal.WaitAsync(timeout ?? TimeSpan.FromMinutes(2), cancellationToken);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"Timed out waiting for {expected}; the child process or its callbacks "
                + "may not have been scheduled. This does not establish a missing exit code.", ex);
        }
    }
}
