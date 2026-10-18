using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using HexIDE.Conversations;
using HexIDE.Dap;
using HexIDE.Redaction;
using Peer = HexIDE.Tests.DebugAdapter.DapConnectionTests.Peer;

namespace HexIDE.Tests.DebugAdapter;

public class DapCaptureTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private const string Id = "dap:fixture";
    private static string Body(ConversationLog log, long sequence) => Encoding.UTF8.GetString(log.Body(Id, sequence)!.Head);

    [Fact]
    public async Task Pairs_reverse_requests_by_direction_and_keeps_exact_response_bytes()
    {
        await using var log = new ConversationLog();
        await using var peer = new Peer(capture: new(log, Id));
        var pending = peer.Request("initialize");
        await peer.Receive();
        await peer.Send("""{"seq":1,"type":"request","command":"runInTerminal"}""");
        await peer.Receive();
        const string response = """{ "seq":2, "type":"response", "request_seq":1, "command":"initialize", "success":false, "body":{"error":{"id":42}}, "future":"雪" }""";
        await peer.Send(response);
        Func<Task> wait = () => pending.WaitAsync(Patience, TestToken);
        await wait.Should().ThrowAsync<DapRequestException>();
        await log.DrainAsync();
        var requests = log.Snapshot(Id).Where(e => e.Kind == ConversationEntryKind.Request).ToArray();
        requests.Should().HaveCount(2);
        var sent = requests.Single(e => e.Direction == ConversationDirection.Sent);
        var received = requests.Single(e => e.Direction == ConversationDirection.Received);
        sent.CorrelationId.Should().Be("1");
        received.CorrelationId.Should().Be("1");
        sent.Outcome.Should().Be(ConversationOutcome.Failed);
        received.Outcome.Should().Be(ConversationOutcome.Failed);
        Body(log, sent.AnswerSequence!.Value).Should().Be(response);
        sent.AnswerSizeBytes.Should().Be(Encoding.UTF8.GetByteCount(response));
        Body(log, received.AnswerSequence!.Value).Should().Contain("runInTerminal");
    }

    [Fact]
    public async Task Events_obey_opening_retention_arming_and_frame_limits()
    {
        await using var log = new ConversationLog(new CaptureLimits(FrameBytes: 1024));
        await using var peer = new Peer(capture: new(log, Id));
        for (var i = 0; i < ConversationLog.OpeningFrames + 1; i++)
        {
            await peer.Send($$$"""{"seq":{{{i}}},"type":"event","event":"output","body":{"output":"雪"}}""");
            await peer.Client.Events.ReadAsync(TestToken);
        }
        await log.DrainAsync();
        var events = log.Snapshot(Id).Where(e => e.Kind == ConversationEntryKind.Notification).ToArray();
        events.Should().HaveCount(ConversationLog.OpeningFrames + 1);
        log.Body(Id, events[0].Sequence).Should().NotBeNull();
        log.Body(Id, events[^1].Sequence).Should().BeNull();
        events.Select(e => e.Method).Should().OnlyContain(name => name == "output");
        log.Arm(Id, true);
        await peer.Send(JsonSerializer.Serialize(new { seq = 99, type = "event", @event = "output", body = new { output = new string('x', 4096) } }));
        await peer.Client.Events.ReadAsync(TestToken);
        await log.DrainAsync();
        var last = log.Snapshot(Id).Last(e => e.Kind == ConversationEntryKind.Notification);
        log.Body(Id, last.Sequence)!.IsTruncated.Should().BeTrue();
        log.Body(Id, last.Sequence)!.RetainedBytes.Should().Be(1024);
    }

    [Fact]
    public async Task Cancelled_requests_stay_cancelled_when_a_late_response_arrives()
    {
        await using var log = new ConversationLog();
        await using var peer = new Peer(capture: new(log, Id));
        using var cancel = new CancellationTokenSource();
        var pending = peer.Request("threads", cancellationToken: cancel.Token);
        var request = await peer.Receive();
        cancel.Cancel();
        Func<Task> wait = () => pending.WaitAsync(Patience, TestToken);
        await wait.Should().ThrowAsync<OperationCanceledException>();
        await peer.Reply(request);
        await peer.Send("""{"seq":10,"type":"event","event":"stopped"}""");
        await peer.Client.Events.ReadAsync(TestToken);
        await log.DrainAsync();
        log.Snapshot(Id).Single(e => e.Kind == ConversationEntryKind.Request).Outcome.Should().Be(ConversationOutcome.Cancelled);
        log.Snapshot(Id).Should().Contain(e => e.Kind == ConversationEntryKind.Response);
    }

    [Fact]
    public async Task Invalid_JSON_is_retained_and_pending_work_is_marked_abandoned()
    {
        await using var log = new ConversationLog();
        await using var peer = new Peer(capture: new(log, Id));
        var pending = peer.Request("threads");
        await peer.Receive();
        await peer.Send("invalid JSON");
        await peer.Client.Completion.WaitAsync(Patience, TestToken);
        Func<Task> wait = () => pending;
        await wait.Should().ThrowAsync<IOException>();
        await log.DrainAsync();
        var invalid = log.Snapshot(Id).Single(e => e.Kind == ConversationEntryKind.Note);
        Body(log, invalid.Sequence).Should().Be("invalid JSON");
        log.Snapshot(Id).Single(e => e.Kind == ConversationEntryKind.Request).Outcome.Should().Be(ConversationOutcome.Abandoned);
        log.Snapshot(Id).Should().Contain(e => e.Kind == ConversationEntryKind.Lifecycle && e.Detail!.Contains("InvalidDataException"));
    }

    [Fact]
    public async Task A_mismatched_response_is_evidence_of_failure_not_a_successful_exchange()
    {
        await using var log = new ConversationLog();
        await using var peer = new Peer(capture: new(log, Id));
        var pending = peer.Request("threads");
        await peer.Receive();
        const string invalid = """{"seq":2,"type":"response","request_seq":1,"command":"launch","success":true}""";
        await peer.Send(invalid);
        Func<Task> wait = () => pending.WaitAsync(Patience, TestToken);
        await wait.Should().ThrowAsync<IOException>();
        await log.DrainAsync();
        log.Snapshot(Id).Single(e => e.Kind == ConversationEntryKind.Request).Outcome.Should().Be(ConversationOutcome.Abandoned);
        var note = log.Snapshot(Id).Single(e => e.Kind == ConversationEntryKind.Note);
        Body(log, note.Sequence).Should().Be(invalid);
    }

    [Fact]
    public async Task A_blocked_capture_pump_does_not_block_protocol_traffic()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var log = new ConversationLog(gate.Task, queueDepth: 1);
        try
        {
            await using var peer = new Peer(capture: new(log, Id));
            var pending = peer.Request("threads");
            await peer.Reply(await peer.Receive());
            (await pending.WaitAsync(Patience, TestToken)).Success.Should().BeTrue();
            log.QueueDropped.Should().BeGreaterThan(0);
        }
        finally { gate.TrySetResult(); }
    }

    [Fact]
    public async Task Disposing_the_log_does_not_break_an_active_connection()
    {
        var log = new ConversationLog();
        await using var peer = new Peer(capture: new(log, Id));
        await log.DisposeAsync();
        var pending = peer.Request("threads");
        await peer.Reply(await peer.Receive());
        (await pending.WaitAsync(Patience, TestToken)).Success.Should().BeTrue();
    }

    private sealed class FailingOutput : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromException(new IOException("fixture write failure"));
    }

    [Fact]
    public async Task Failed_writes_are_attempts_with_a_failure_note_not_successful_exchanges()
    {
        await using var log = new ConversationLog();
        var pipe = new Pipe();
        await using var connection = new DapConnection(pipe.Reader.AsStream(), new FailingOutput(), capture: new(log, Id));
        Func<Task> request = () => connection.RequestAsync("launch", cancellationToken: TestToken);
        await request.Should().ThrowAsync<IOException>();
        await connection.Completion.WaitAsync(Patience, TestToken);
        await log.DrainAsync();
        var entry = log.Snapshot(Id).Single(e => e.Kind == ConversationEntryKind.Request);
        entry.Outcome.Should().Be(ConversationOutcome.Abandoned);
        entry.Detail.Should().Contain("write attempt");
        log.Snapshot(Id).Should().Contain(e => e.Detail != null && e.Detail.Contains("Writing a DAP frame failed"));
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task Process_capture_includes_start_stderr_and_exit_code()
    {
        await using var log = new ConversationLog();
        await using var process = DapProcess.Start(new("dotnet")
        {
            Arguments = [typeof(Dap.TestAdapter.Program).Assembly.Location, "exit"], Capture = new(log, Id),
        });
        await process.Completion.WaitAsync(Patience, TestToken);
        await log.DrainAsync();
        var entries = log.Snapshot(Id);
        entries.Should().Contain(e => e.Detail != null && e.Detail.Contains("process started"));
        string.Concat(entries.Where(e => e.Kind == ConversationEntryKind.StandardError).Select(e => e.Detail))
            .Should().Be("adapter startup failed");
        entries.Last().Detail.Should().Contain("exit code 37");
    }

    [Theory]
    [InlineData("/work/private-project/Module1.bas")]
    [InlineData("C:\\work\\private-project\\Module1.bas")]
    [InlineData("relative/雪\"module.bas")]
    public async Task Export_pseudonymises_source_paths_without_changing_the_raw_capture(string path)
    {
        await using var log = new ConversationLog();
        await using var peer = new Peer(capture: new(log, Id));
        var raw = JsonSerializer.Serialize(new { seq = 1, type = "event", @event = "loadedSource", body = new { source = new { path } } });
        await peer.Send(raw);
        await peer.Client.Events.ReadAsync(TestToken);
        await log.DrainAsync();
        var entry = log.Snapshot(Id).Single(e => e.Kind == ConversationEntryKind.Notification);
        Body(log, entry.Sequence).Should().Be(raw);
        var redactor = new ConversationRedactor(new Pseudonymiser());
        var export = await ConversationExporter.ExportAsync(log, redactor, Id);
        export.Messages.Should().Contain(JsonSerializer.Serialize(redactor.LocalPath(path)));
        export.Messages.Should().NotContain(JsonSerializer.Serialize(path));
        var unredacted = await ConversationExporter.ExportAsync(log, new(new Pseudonymiser(), pseudonymise: false), Id);
        unredacted.Messages.Should().Contain(raw);
    }

    [Fact]
    public void URI_paths_are_not_pseudonymised_twice_and_ordinary_messages_are_unchanged()
    {
        var redactor = new ConversationRedactor(new Pseudonymiser());
        const string uri = "file:///private/project/module.bas";
        var body = JsonSerializer.Serialize(new { path = uri });
        using var output = JsonDocument.Parse(redactor.Body(body));
        output.RootElement.GetProperty("path").GetString().Should().Be(redactor.Uri(uri));
        const string ordinary = """{ "seq":1, "type":"event", "event":"stopped", "body":{"reason":"breakpoint"} }""";
        redactor.Body(ordinary).Should().Be(ordinary);
    }
}
