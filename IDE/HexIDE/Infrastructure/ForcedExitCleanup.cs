using System.Threading;

namespace HexIDE.Infrastructure;

internal static class ForcedExitCleanup
{
    internal static void Run(IDisposable loader, CancellationTokenSource cancellation, IDisposable context,
        int exitCode, Action<int> exit, Action closeLog)
    {
        try
        {
            loader.Dispose();
            cancellation.Cancel();
            context.Dispose();
        }
        catch (Exception cleanup)
        {
            Serilog.Log.Warning(cleanup, "Cleanup before exiting with {ExitCode} failed; exiting anyway", exitCode);
        }
        finally
        {
            exit(exitCode);
            closeLog();
        }
    }
}
