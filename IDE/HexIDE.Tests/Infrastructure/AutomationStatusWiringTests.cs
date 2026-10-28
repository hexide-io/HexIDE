namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// The automation server tells the status bar's section what to show (#2451), read from the server's own source, as no
/// test here runs the dispatcher the section is updated on. What the section does with it is
/// <c>AutomationStatusSectionTests</c>'s.
/// </summary>
public class AutomationStatusWiringTests
{
    private static string Server(string file) =>
        File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Desktop", "Server", file)).ReplaceLineEndings("\n");

    [Fact]
    public void Listening_is_reported_once_the_port_is_bound_not_before()
    {
        var server = Server("IdeServer.cs");
        var started = server.IndexOf("await app.StartAsync(ct);", StringComparison.Ordinal);
        var listening = server.IndexOf("AutomationServerStatus.Current.Listening(port, session, ToolFailures.WithoutProfile(",
            StringComparison.Ordinal);

        started.Should().BeGreaterThan(-1);
        listening.Should().BeGreaterThan(started, "a port another IDE holds fails the start, and nothing listens");
        // The build directory reaches the hover tool's reply, so it is masked as the session refusal masks it.
    }

    [Fact]
    public void Calls_are_counted_after_the_session_guard_and_before_failure_reporting()
    {
        // After the guard, so a call from another session is counted as refused, not as the last call this IDE ran; before
        // failure reporting, so a call refused for its arguments still counts.
        var filters = Server("ToolFilters.cs");
        var guard = filters.IndexOf("SessionGuard.Register(builder, session)", StringComparison.Ordinal);
        var activity = filters.IndexOf("AddCallToolFilter(ActivityFilter.Filter)", StringComparison.Ordinal);
        var failures = filters.IndexOf("AddCallToolFilter(ToolFailures.Filter)", StringComparison.Ordinal);

        guard.Should().BeGreaterThan(-1);
        activity.Should().BeGreaterThan(guard);
        failures.Should().BeGreaterThan(activity);
        Server("ActivityFilter.cs").Should().Contain("status.CallStarted(").And.Contain("finally")
            .And.Contain("status.CallEnded();");
    }

    [Fact]
    public void A_call_refused_for_its_session_is_counted_as_refused()
    {
        var guard = Server("SessionGuard.cs");
        var refusal = guard.IndexOf("if (Refusal(session, caller) is { } refusal)", StringComparison.Ordinal);
        var counted = guard.IndexOf("AutomationServerStatus.Current.Refused(caller);", StringComparison.Ordinal);

        counted.Should().BeGreaterThan(refusal);
        counted.Should().BeLessThan(guard.IndexOf("return new CallToolResult", refusal, StringComparison.Ordinal));
    }
}
