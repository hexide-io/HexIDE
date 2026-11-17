using Avalonia.Headless;

namespace HexIDE.Tests;

/// <summary>
/// Runs a test's body on the headless session's thread, from a test that can't be an <c>[AvaloniaFact]</c>, such as a
/// <c>[ForeignServerFact]</c> (#4130). The only way a test here should call <see cref="HeadlessUnitTestSession.Dispatch"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Dispatch</c> completes its task on the session's own thread, so a test that awaits it directly resumes there, and
/// xunit's runner carries on there with it. The next <c>[AvaloniaFact]</c> then waits on that thread for work only that
/// thread can run, and the whole run hangs with no failure, only CI's step timeout. The result comes back through a
/// source whose continuations always run elsewhere.
/// </para>
/// <para><c>AvaloniaTestGuardTests</c> fails the build when a test calls <c>Dispatch</c> itself.</para>
/// </remarks>
internal static class HeadlessDispatch
{
    internal static Task RunAsync(Func<Task> body, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatched = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(TestApp).Assembly)
            .Dispatch<bool>(async () =>
            {
                try
                {
                    await body();
                    done.TrySetResult();
                }
                catch (Exception ex)
                {
                    done.TrySetException(ex);
                }
                return true;
            }, cancellationToken);
        // Dispatch can also end without running the body, cancelled or faulted, and the source hears of that too.
        _ = dispatched.ContinueWith(task =>
        {
            if (task.IsFaulted)
                done.TrySetException(task.Exception!.InnerExceptions);
            else if (task.IsCanceled)
                done.TrySetCanceled(cancellationToken);
        }, CancellationToken.None, TaskContinuationOptions.NotOnRanToCompletion, TaskScheduler.Default);
        return done.Task;
    }
}
