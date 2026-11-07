using HexIDE.Runtime.Interpreter;
using Antlr4.Runtime;
using System.Threading;

namespace HexIDE.Runtime.Tests;

public class SizedParsingTests(ITestOutputHelper output) : BaseVBTestFixture
{
    // VB6 SP6 p-code measurements: #3210 and #3202. These tests parse, without executing the deep body.
    [Theory]
    [InlineData(false, 7643)]
    [InlineData(true, 7227)]
    public void ReferenceDepth_BuildsModule(bool blocks, int depth)
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        process.Refresh();
        var before = process.WorkingSet64;
        var privateBefore = process.PrivateMemorySize64;
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, Source(blocks, depth));
        vb.PrePass.Procedures.Should().ContainKey("Main");
        process.Refresh();
        output.WriteLine($"blocks={blocks}, depth={depth}; working set {before} -> {process.WorkingSet64}; private bytes {privateBefore} -> {process.PrivateMemorySize64}");
    }

    [Theory]
    [InlineData(false, 7643)]
    [InlineData(true, 7227)]
    public void ReferenceDepth_SyntaxCheckerAccepts(bool blocks, int depth)
    {
        new SyntaxChecker().Run(Source(blocks, depth));
    }

    [Fact]
    public void SyntaxChecker_ErrorTraversesSizedWorker()
    {
        var caller = Thread.CurrentThread;
        var checker = new RecordingSyntaxChecker();
        var act = () => checker.Run("Sub Main(\nEnd Sub\n");
        act.Should().Throw<VBCompileErrorException>();
        checker.ParseThread.Should().NotBeSameAs(caller);
        checker.WasPoolThread.Should().BeFalse();
        checker.ThreadName.Should().Be("HexIDE runtime parse");
    }

    [Fact]
    public void PhysicalStackProbe_RefusesBeforeParserRecurses()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { UseStack(0); }
            catch (Exception e) { error = e; }
        }, 1024 * 1024);
        thread.Start();
        thread.Join();
        error.Should().BeOfType<VBCompileErrorException>().Which.Message.Should().Contain("parser stack space");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void UseStack(int depth)
    {
        if (System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
            UseStack(depth + 1);
        else
            new ParseDepthGuard(int.MaxValue).EnterEveryRule(new ParserRuleContext());
        GC.KeepAlive(depth); // Keep work after the recursive call so this cannot become a tail call.
    }

    private sealed class RecordingSyntaxChecker : SyntaxChecker, IAntlrErrorListener<IToken>
    {
        internal Thread? ParseThread { get; private set; }
        internal bool WasPoolThread { get; private set; }
        internal string? ThreadName { get; private set; }
        public new void SyntaxError(System.IO.TextWriter output, IRecognizer recognizer, IToken symbol,
            int line, int column, string message, RecognitionException error)
        {
            ParseThread = Thread.CurrentThread;
            WasPoolThread = ParseThread.IsThreadPoolThread;
            ThreadName = ParseThread.Name;
            base.SyntaxError(output, recognizer, symbol, line, column, message, error);
        }
    }

    internal static string Source(bool blocks, int depth) => "Sub Main()\nDim v\n" + (blocks
        ? string.Concat(Enumerable.Repeat("If True Then\n", depth)) + "v = 1\n"
            + string.Concat(Enumerable.Repeat("End If\n", depth))
        : "v = " + ReferenceParentheses(depth) + "\n") + "End Sub\n";

    // #3202's measured expression uses 7 and 1,000 characters per line (15 continuations at 7,643 pairs).
    private static string ReferenceParentheses(int depth) => string.Join(" _\n",
        (new string('(', depth) + "7" + new string(')', depth)).Chunk(1000).Select(chars => new string(chars)));
}
