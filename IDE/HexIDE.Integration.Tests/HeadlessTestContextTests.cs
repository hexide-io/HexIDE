using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using Xunit.Sdk;
using Xunit.v3;

namespace HexIDE.Integration.Tests;

// Each headless test runs on the context it was dispatched from, not on whatever the dispatcher's thread was left holding
// (#2486). Avalonia.Headless 12.0.2 started each test on the thread's leftover, so a job that left an empty context made
// every later Avalonia test throw in xunit's setup before it started. MTP does not count those errors, so a run lost
// hundreds of tests and still passed. 12.0.4 captures the caller's context (AvaloniaUI/Avalonia#21357).
//
// The class runs in a fixed order: the first test leaves the thread with no context, and the second must still start. On a
// headless package that does not capture the context, the second and every Avalonia test after it are lost, and CI's check
// of xunit's report names them.
[TestCaseOrderer(typeof(InThisOrder))]
public class HeadlessTestContextTests
{
    public sealed class InThisOrder : ITestCaseOrderer
    {
        private static readonly string[] Order =
        [
            nameof(A_test_can_leave_the_headless_thread_with_no_context),
            nameof(The_test_after_it_still_starts_with_its_own_context),
        ];

        public IReadOnlyCollection<TTestCase> OrderTestCases<TTestCase>(IReadOnlyCollection<TTestCase> testCases)
            where TTestCase : notnull, ITestCase =>
            testCases.OrderBy(testCase => Array.IndexOf(Order, testCase.TestMethodName)).ToArray();
    }

    [AvaloniaFact]
    public void A_test_can_leave_the_headless_thread_with_no_context()
    {
        // Avalonia 12.0.4 ran a dispatcher job by restoring the context it was posted from and left that context behind,
        // and the headless session runs whatever jobs are left after a test in its teardown. A job posted then from a
        // thread with no context left the thread with none. 12.0.5 puts the thread's context back after a job, but any
        // code that restores a context there leaves it the same way, so this does that deterministically: a job that never
        // runs is aborted in that teardown, and its handler restores an empty context.
        var empty = EmptyContext();
        var parked = Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Inactive);
        parked.Aborted += (_, _) => ExecutionContext.Restore(empty);
    }

    [AvaloniaFact]
    public void The_test_after_it_still_starts_with_its_own_context()
    {
        TestContext.Current.Test.Should().NotBeNull();
        TestContext.Current.Test!.TestCase.TestMethodName.Should().Be(nameof(The_test_after_it_still_starts_with_its_own_context));
    }

    // A context captured on a thread-pool thread started without one, as a callback the runtime raises unasked is.
    private static ExecutionContext EmptyContext()
    {
        ExecutionContext? empty = null;
        using var captured = new ManualResetEventSlim();
        ThreadPool.UnsafeQueueUserWorkItem(_ =>
        {
            empty = ExecutionContext.Capture();
            captured.Set();
        }, null);
        captured.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        return empty!;
    }
}
