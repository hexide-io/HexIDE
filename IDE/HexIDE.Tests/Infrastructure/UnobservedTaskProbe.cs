using System.Reflection;
using Xunit.v3;

[assembly: HexIDE.Tests.Infrastructure.UnobservedTaskProbe]

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Set HEXIDE_UNOBSERVED_PROBE to a file to append unobserved task exceptions after each test.
/// Forces two collection/finalizer cycles; attribution can land one test late when work outlives its test.
/// Does nothing when the variable is unset. This diagnostic is opt-in, not a CI guard.
/// </summary>
public sealed class UnobservedTaskProbe : BeforeAfterTestAttribute
{
    private static readonly object Gate = new();
    private static bool subscribed;
    private static string? output;
    private static string displayName = "";

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        lock (Gate)
        {
            output = Environment.GetEnvironmentVariable("HEXIDE_UNOBSERVED_PROBE");
            displayName = test.TestDisplayName;
            if (string.IsNullOrEmpty(output) || subscribed) return;
            TaskScheduler.UnobservedTaskException += Record;
            subscribed = true;
        }
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (string.IsNullOrEmpty(output)) return;
        for (var i = 0; i < 2; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private static void Record(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        lock (Gate)
        {
            if (!string.IsNullOrEmpty(output))
                File.AppendAllText(output, $"=== after {displayName}{Environment.NewLine}{args.Exception}{Environment.NewLine}");
        }
    }
}
