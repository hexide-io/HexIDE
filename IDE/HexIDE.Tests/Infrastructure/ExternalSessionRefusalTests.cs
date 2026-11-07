using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// A tool that reads the interpreter's run checks for an external debug session before it does
/// (hexide-io/HexIDE#2966).
/// </summary>
/// <remarks>
/// An external session is not the interpreter's run: the debug controller has no stop, and
/// <c>ProjectRunnerService.RunningProject</c> is null. A tool that reads either first answers as though nothing were
/// running, straight after <c>get_debug_state</c> has called the external session paused. <c>set_next_statement</c>
/// did, with "No project is running". A tool that watches a running HexIDE form through
/// <c>UiAutomationDriver.WatchProgram</c> reads the controller's stop only to say the form paused, so that read is
/// left out. Read from <c>HexIdeTools.cs</c> through <see cref="ToolSource"/>, because no test project references
/// <c>HexIDE.Desktop</c>.
/// </remarks>
public class ExternalSessionRefusalTests
{
    private static readonly Regex InterpreterRead = new(@"\bctx\.DebugController\.|\bProjectRunnerService\.RunningProject\b");
    private static readonly Regex FormWatch = new(@"UiAutomationDriver\.WatchProgram\([^;]*;");

    [Fact]
    public void Every_tool_that_reads_the_interpreters_run_checks_for_an_external_session_first()
    {
        var readers = ToolSource.Tools
            .Select(tool => (tool.Name, Body: FormWatch.Replace(tool.Body, "")))
            .Select(tool => (tool.Name, tool.Body, Read: InterpreterRead.Match(tool.Body)))
            .Where(tool => tool.Read.Success)
            .ToList();

        readers.Select(tool => tool.Name).Should().Contain(
            ["set_next_statement", "get_debug_state", "get_locals", "get_call_stack", "evaluate"],
            "each of these reads the run in its own body, so a pattern that misses them checks nothing");
        foreach (var (name, body, read) in readers)
            body.IndexOf("ExternalDebugger", StringComparison.Ordinal).Should().BeInRange(0, read.Index,
                "{0} reads the interpreter's run, which an external session leaves empty, so it must check "
                + "ctx.RootViewModel.ExternalDebugger before that read", name);
    }
}
