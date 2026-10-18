using System.Text.Json;
using HexIDE.Dap;
using Peer = HexIDE.Tests.DebugAdapter.DapConnectionTests.Peer;

namespace HexIDE.Tests.DebugAdapter;

public class DapSessionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static JsonElement Args => JsonSerializer.SerializeToElement(new { module = "Module1", entryPoint = "Main" });

    private static async Task<DapSessionUpdate> Until(DapSession session, Func<DapSessionUpdate, bool> predicate)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(Patience);
        while (true)
        {
            var update = await session.Updates.ReadAsync(deadline.Token);
            if (predicate(update)) return update;
        }
    }

    private static async Task<DapStop> Stop(Peer peer, DapSession session)
    {
        await peer.Send("""{"seq":11,"type":"event","event":"stopped","body":{"threadId":1,"reason":"breakpoint"}}""");
        return (await Until(session, u => u.Event?.Event == "stopped")).Snapshot.Stop!;
    }

    private static async Task Launch(Peer peer, DapSession session, bool configuration = true)
    {
        var pending = session.LaunchAsync("external", Args, [], Token);
        var initialize = await peer.Receive();
        initialize.GetProperty("command").GetString().Should().Be("initialize");
        initialize.GetProperty("arguments").GetProperty("linesStartAt1").GetBoolean().Should().BeTrue();
        await peer.Reply(initialize, configuration ? """{"supportsConfigurationDoneRequest":true}""" : "{}");
        var launch = await peer.Receive();
        launch.GetProperty("command").GetString().Should().Be("launch");
        await peer.Send("""{"seq":2,"type":"event","event":"initialized"}""");
        if (configuration)
        {
            var done = await peer.Receive();
            done.GetProperty("command").GetString().Should().Be("configurationDone");
            await peer.Reply(done);
        }
        await peer.Reply(launch);
        await pending.WaitAsync(Patience, Token);
    }

    [Fact]
    public async Task Configures_before_awaiting_launch_and_preserves_breakpoint_verification()
    {
        await using var peer = new Peer();
        await using var session = new DapSession(peer.Client);
        var pending = session.LaunchAsync("external", Args, [new("/workspace/Module1.bas", [4])], Token);
        await peer.Reply(await peer.Receive(), """{"supportsConfigurationDoneRequest":true,"exceptionBreakpointFilters":[{"filter":"unhandled","default":true},{"filter":"all","default":false}],"future":42}""");
        var launch = await peer.Receive();
        await peer.Send("""{"seq":2,"type":"event","event":"initialized"}""");
        var bp = await peer.Receive();
        bp.GetProperty("command").GetString().Should().Be("setBreakpoints");
        bp.GetProperty("arguments").GetProperty("source").GetProperty("path").GetString().Should().Be("/workspace/Module1.bas");
        bp.GetProperty("arguments").GetProperty("breakpoints")[0].GetProperty("line").GetInt32().Should().Be(4);
        await peer.Reply(bp, """{"breakpoints":[{"verified":false,"message":"No executable statement"}]}""");
        var errors = await peer.Receive();
        errors.GetProperty("command").GetString().Should().Be("setExceptionBreakpoints");
        errors.GetProperty("arguments").GetProperty("filters").GetRawText().Should().Be("[\"unhandled\"]");
        var configured = peer.Reply(errors);
        await configured;
        var done = await peer.Receive();
        done.GetProperty("command").GetString().Should().Be("configurationDone");
        await peer.Reply(done);
        await peer.Reply(launch);
        var results = await pending.WaitAsync(Patience, Token);
        results[0].Body!.Value.GetProperty("breakpoints")[0].GetProperty("verified").GetBoolean().Should().BeFalse();
        session.Capabilities.GetProperty("future").GetInt32().Should().Be(42);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task Evaluation_preserves_repl_syntax_and_frame_without_optional_capability(int frameId)
    {
        await using var peer = new Peer();
        await using var session = new DapSession(peer.Client);
        await Launch(peer, session, false);
        var stop = await Stop(peer, session);
        var pending = session.EvaluateAsync(stop, frameId, "? callerValue + 1", Token);
        var request = await peer.Receive();
        request.GetProperty("command").GetString().Should().Be("evaluate");
        var arguments = request.GetProperty("arguments");
        arguments.GetProperty("expression").GetString().Should().Be("? callerValue + 1");
        arguments.GetProperty("context").GetString().Should().Be("repl");
        arguments.GetProperty("frameId").GetInt32().Should().Be(frameId);
        await peer.Reply(request, """{"result":"43","variablesReference":123,"future":true}""");
        var reply = await pending;
        reply.Body!.Value.GetProperty("variablesReference").GetInt32().Should().Be(123);
        reply.Body.Value.GetProperty("future").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Evaluation_rejects_reply_from_expired_stop_and_preserves_adapter_refusal()
    {
        await using var peer = new Peer();
        await using var session = new DapSession(peer.Client);
        await Launch(peer, session);
        var stop = await Stop(peer, session);
        var refused = session.EvaluateAsync(stop, 0, "bad", Token);
        await peer.Reply(await peer.Receive(), success: false);
        await ((Func<Task>)(() => refused)).Should().ThrowAsync<DapRequestException>();
        var pending = session.EvaluateAsync(stop, 0, "slow", Token);
        var request = await peer.Receive();
        await Stop(peer, session);
        await peer.Reply(request, """{"result":"old","variablesReference":0}""");
        await ((Func<Task>)(() => pending)).Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => session.EvaluateAsync(stop, 0, "stale", Token))).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Unsupported_configuration_done_is_omitted()
    {
        await using var peer = new Peer();
        await using var session = new DapSession(peer.Client);
        await Launch(peer, session, false);
        session.Snapshot.State.Should().Be(DapSessionState.Running);
        var disconnect = session.DisconnectAsync(Token);
        var request = await peer.Receive();
        request.GetProperty("command").GetString().Should().Be("disconnect");
        await peer.Reply(request);
        await disconnect.WaitAsync(Patience, Token);
    }

    [Theory]
    [InlineData("continue")]
    [InlineData("next")]
    [InlineData("stepIn")]
    [InlineData("stepOut")]
    public async Task Fast_stop_before_resume_response_wins(string command)
    {
        await using var peer = new Peer();
        await using var session = new DapSession(peer.Client);
        await Launch(peer, session);
        var first = await Stop(peer, session);
        var resume = session.ResumeAsync(first, command, 1, Token);
        var request = await peer.Receive();
        request.GetProperty("command").GetString().Should().Be(command);
        session.IsCurrent(first).Should().BeFalse();
        var second = await Stop(peer, session);
        await peer.Reply(request);
        await resume.WaitAsync(Patience, Token);
        session.Snapshot.Stop.Should().Be(second);
        session.Snapshot.State.Should().Be(DapSessionState.Stopped);
    }

    [Fact]
    public async Task Delayed_inspection_from_previous_stop_is_rejected_even_when_frame_ids_are_reused()
    {
        await using var peer = new Peer();
        await using var session = new DapSession(peer.Client);
        await Launch(peer, session);
        var first = await Stop(peer, session);
        var inspection = session.VariablesAsync(first, 1, Token);
        var variables = await peer.Receive();
        var resume = session.ResumeAsync(first, "continue", 1, Token);
        var request = await peer.Receive();
        await peer.Reply(request);
        await resume;
        await Stop(peer, session);
        await peer.Reply(variables, """{"variables":[{"name":"old","value":"42","variablesReference":0}]}""");
        await ((Func<Task>)(() => inspection)).Should().ThrowExactlyAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Refused_resume_restores_a_fresh_stop_and_old_handles_stay_expired()
    {
        await using var peer = new Peer();
        await using var session = new DapSession(peer.Client);
        await Launch(peer, session);
        var stop = await Stop(peer, session);
        var resume = session.ResumeAsync(stop, "continue", 1, Token);
        await peer.Reply(await peer.Receive(), success: false);
        await ((Func<Task>)(() => resume)).Should().ThrowExactlyAsync<DapRequestException>();
        session.Snapshot.State.Should().Be(DapSessionState.Stopped);
        session.IsCurrent(stop).Should().BeFalse();
        session.Snapshot.Stop!.Generation.Should().BeGreaterThan(stop.Generation);
    }

    [Fact]
    public async Task Launch_refusal_without_initialized_fails_promptly_and_closes_connection()
    {
        await using var peer = new Peer();
        await using var session = new DapSession(peer.Client);
        var launch = session.LaunchAsync("external", Args, [], Token);
        await peer.Reply(await peer.Receive());
        await peer.Reply(await peer.Receive(), success: false);
        await ((Func<Task>)(() => launch.WaitAsync(Patience, Token))).Should().ThrowExactlyAsync<DapRequestException>();
        session.Snapshot.State.Should().Be(DapSessionState.Faulted);
        peer.Client.Completion.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Missing_initialized_is_bounded_even_after_successful_launch_response()
    {
        await using var peer = new Peer();
        await using var session = new DapSession(peer.Client, TimeSpan.FromMilliseconds(500));
        var launch = session.LaunchAsync("external", Args, [], Token);
        await peer.Reply(await peer.Receive());
        await peer.Reply(await peer.Receive());
        await ((Func<Task>)(() => launch.WaitAsync(Patience, Token))).Should().ThrowAsync<OperationCanceledException>();
        session.Snapshot.State.Should().Be(DapSessionState.Faulted);
        peer.Client.Completion.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Output_is_preserved_and_queue_overflow_faults_instead_of_losing_state()
    {
        await using var peer = new Peer();
        await using var session = new DapSession(peer.Client, updateCapacity: 1);
        await peer.Send("""{"seq":1,"type":"event","event":"output","body":{"category":"stdout","output":"hello\n"}}""");
        var output = await Until(session, u => u.Event?.Event == "output");
        output.Event!.Body!.Value.GetProperty("output").GetString().Should().Be("hello\n");
        await peer.Send("""{"seq":2,"type":"event","event":"thread"}""");
        await peer.Send("""{"seq":3,"type":"event","event":"thread"}""");
        await session.Completion.WaitAsync(Patience, Token);
        session.Snapshot.State.Should().Be(DapSessionState.Faulted);
        session.Snapshot.Failure.Should().BeOfType<IOException>();
    }

    [Fact]
    public async Task Stop_from_another_session_cannot_read_locals()
    {
        await using var peer = new Peer();
        await using var session = new DapSession(peer.Client);
        var stop = await Stop(peer, session);
        await ((Func<Task>)(() => session.ScopesAsync(stop with { SessionId = Guid.NewGuid() }, 1, Token)))
            .Should().ThrowExactlyAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Initialized_before_initialize_response_is_retained_and_stop_ids_are_preserved()
    {
        await using var peer = new Peer();
        await using var session = new DapSession(peer.Client);
        var launch = session.LaunchAsync("external", Args, [], Token);
        var initialize = await peer.Receive();
        await peer.Send("""{"seq":1,"type":"event","event":"initialized"}""");
        await peer.Reply(initialize);
        await peer.Reply(await peer.Receive());
        await launch.WaitAsync(Patience, Token);
        var stop = await Stop(peer, session);
        var stack = session.StackTraceAsync(stop, 7, Token);
        var request = await peer.Receive();
        request.GetProperty("arguments").GetProperty("threadId").GetInt32().Should().Be(7);
        await peer.Reply(request, """{"stackFrames":[{"id":0,"name":"Main","source":{"path":"/src/a.bas","sourceReference":4},"line":6}]}""");
        var frame = (await stack).Body!.Value.GetProperty("stackFrames")[0];
        frame.GetProperty("id").GetInt32().Should().Be(0);
        frame.GetProperty("source").GetProperty("sourceReference").GetInt32().Should().Be(4);
        var scopes = session.ScopesAsync(stop, 0, Token);
        request = await peer.Receive();
        request.GetProperty("arguments").GetProperty("frameId").GetInt32().Should().Be(0);
        await peer.Reply(request, """{"scopes":[{"name":"Locals","variablesReference":42}]}""");
        (await scopes).Body!.Value.GetProperty("scopes")[0].GetProperty("variablesReference").GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task Cancelled_resume_is_uncertain_and_ends_the_owned_connection()
    {
        await using var peer = new Peer();
        await using var session = new DapSession(peer.Client);
        await Launch(peer, session);
        var stop = await Stop(peer, session);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var resume = session.ResumeAsync(stop, "continue", 1, cancel.Token);
        await peer.Receive();
        await cancel.CancelAsync();
        await ((Func<Task>)(() => resume.WaitAsync(Patience, Token))).Should().ThrowAsync<OperationCanceledException>();
        session.Snapshot.State.Should().Be(DapSessionState.Faulted);
        peer.Client.Completion.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Cancelled_startup_releases_launch_wait_and_connection()
    {
        await using var peer = new Peer();
        await using var session = new DapSession(peer.Client);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var launch = session.LaunchAsync("external", Args, [], cancel.Token);
        await peer.Reply(await peer.Receive());
        await peer.Receive();
        await cancel.CancelAsync();
        await ((Func<Task>)(() => launch.WaitAsync(Patience, Token))).Should().ThrowAsync<OperationCanceledException>();
        peer.Client.Completion.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Unexpected_eof_faults_and_terminated_session_cannot_be_resurrected()
    {
        await using var peer = new Peer();
        await using var session = new DapSession(peer.Client);
        await peer.Output.DisposeAsync();
        await session.Completion.WaitAsync(Patience, Token);
        session.Snapshot.State.Should().Be(DapSessionState.Faulted);

        await using var secondPeer = new Peer();
        await using var second = new DapSession(secondPeer.Client);
        await secondPeer.Send("""{"seq":1,"type":"event","event":"terminated"}""");
        await Until(second, u => u.Event?.Event == "terminated");
        await secondPeer.Send("""{"seq":2,"type":"event","event":"stopped","body":{"threadId":1}}""");
        (await Until(second, u => u.Event?.Event == "stopped")).Snapshot.State.Should().Be(DapSessionState.Ended);
        await secondPeer.Output.DisposeAsync();
        await second.Completion.WaitAsync(Patience, Token);
        second.Snapshot.Failure.Should().BeNull();
    }

    [Fact]
    public async Task Pause_waits_for_event_and_refused_disconnect_still_releases_ownership()
    {
        await using var peer = new Peer();
        await using var session = new DapSession(peer.Client);
        await Launch(peer, session);
        var pause = session.PauseAsync(1, Token);
        var request = await peer.Receive();
        request.GetProperty("command").GetString().Should().Be("pause");
        await peer.Reply(request);
        await pause.WaitAsync(Patience, Token);
        session.Snapshot.State.Should().Be(DapSessionState.Running);
        var stop = await Stop(peer, session);
        var disconnect = session.DisconnectAsync(Token);
        request = await peer.Receive();
        request.GetProperty("arguments").GetProperty("terminateDebuggee").GetBoolean().Should().BeTrue();
        await peer.Reply(request, success: false);
        await ((Func<Task>)(() => disconnect.WaitAsync(Patience, Token))).Should().ThrowExactlyAsync<DapRequestException>();
        session.IsCurrent(stop).Should().BeFalse();
        peer.Client.Completion.IsCompleted.Should().BeTrue();
        await session.DisposeAsync();
    }
}
