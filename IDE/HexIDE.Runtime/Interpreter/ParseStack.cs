using System.Runtime.ExceptionServices;
using System.Threading;

namespace HexIDE.Runtime.Interpreter;

// Parsing and declaration collection are synchronous; execution stays on its caller's thread (#3210).
internal static class ParseStack
{
    internal const int StackSize = 64 * 1024 * 1024;
    // #3210 measured ~2.8 KB per If (five rule frames), ~3.6 KB per parenthesis.
    // Budget the smaller per-rule cost with a 3x margin; the physical probe covers heavier rules.
    internal const int MaxRuleDepth = StackSize / (3 * 560);
    [ThreadStatic] private static bool sized;

    internal static T Run<T>(Func<T> work)
    {
        if (sized)
            return work();
        T result = default!;
        ExceptionDispatchInfo? error = null;
        var worker = new Thread(() =>
        {
            sized = true;
            try { result = work(); }
            catch (Exception e) { error = ExceptionDispatchInfo.Capture(e); }
        }, StackSize) { IsBackground = true, Name = "HexIDE runtime parse" };
        worker.Start();
        worker.Join();
        error?.Throw();
        return result;
    }

    internal static int RuleDepth => sized ? MaxRuleDepth : ParseDepthGuard.DefaultMaxDepth;
}
