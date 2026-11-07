using System.Globalization;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using HexIDE.Dap;

namespace HexIDE.Tests.DebugAdapter;

/// <summary>The other end speaks literal DAP bytes, not our serializer or framing implementation.</summary>
public class DapConnectionTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    internal sealed class Peer : IAsyncDisposable
    {
        private readonly Pipe _toClient = new();
        private readonly Pipe _fromClient = new();
        public Stream Input { get; }
        public Stream Output { get; }
        public DapConnection Client { get; }

        public Peer(DapConnectionOptions? options = null, bool slowClientWrites = false, DapCapture? capture = null)
        {
            Input = _fromClient.Reader.AsStream();
            Output = _toClient.Writer.AsStream();
            Stream outgoing = _fromClient.Writer.AsStream();
            Client = new(_toClient.Reader.AsStream(), slowClientWrites ? new YieldingOutput(outgoing) : outgoing, options, capture);
        }

        public Task<DapResponse> Request(string command, JsonElement? arguments = null, CancellationToken? cancellationToken = null, TimeSpan? requestTimeout = null)
            => Client.RequestAsync(command, arguments, cancellationToken ?? TestContext.Current.CancellationToken, requestTimeout);

        public async Task Send(string json, bool fragmented = false)
        {
            var payload = Encoding.UTF8.GetBytes(json);
            var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
            var bytes = header.Concat(payload).ToArray();
            if (fragmented)
            {
                foreach (var b in bytes) await Output.WriteAsync(new[] { b }, TestContext.Current.CancellationToken);
            }
            else await Output.WriteAsync(bytes, TestContext.Current.CancellationToken);
            await Output.FlushAsync(TestContext.Current.CancellationToken);
        }

        public async Task<JsonElement> Receive()
        {
            using var timeout = new CancellationTokenSource(Patience);
            var header = new StringBuilder();
            var one = new byte[1];
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                await Input.ReadExactlyAsync(one, timeout.Token);
                header.Append((char)one[0]);
                (header.Length < 8192).Should().BeTrue();
            }
            var text = header.ToString();
            text.Should().StartWith("Content-Length: ");
            var length = int.Parse(text[16..].Trim(), CultureInfo.InvariantCulture);
            var bytes = new byte[length];
            await Input.ReadExactlyAsync(bytes, timeout.Token);
            return Json(Encoding.UTF8.GetString(bytes));
        }

        public Task Reply(JsonElement request, string body = "{}", bool success = true) => Send(
            $$"""{"seq":100,"type":"response","request_seq":{{request.GetProperty("seq").GetInt32()}},"command":{{request.GetProperty("command").GetRawText()}},"success":{{(success ? "true" : "false")}},"body":{{body}}} """);

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            Input.Dispose();
            Output.Dispose();
        }
    }

    [Fact]
    public async Task Initialize_is_DAP_not_JSON_RPC_and_preserves_unknown_capabilities()
    {
        await using var peer = new Peer();
        var pending = peer.Request("initialize", Json("""{"clientID":"hexide","linesStartAt1":true,"columnsStartAt1":true,"pathFormat":"path"}"""));
        var request = await peer.Receive();
        request.GetProperty("seq").GetInt32().Should().Be(1);
        request.GetProperty("type").GetString().Should().Be("request");
        request.GetProperty("command").GetString().Should().Be("initialize");
        (request.TryGetProperty("jsonrpc", out _)).Should().BeFalse();
        (request.TryGetProperty("id", out _)).Should().BeFalse();
        (request.TryGetProperty("params", out _)).Should().BeFalse();
        (request.GetProperty("arguments").GetProperty("linesStartAt1").GetBoolean()).Should().BeTrue();

        await peer.Send("""{"seq":1,"type":"response","request_seq":1,"command":"initialize","success":true,"body":{"supportsConfigurationDoneRequest":true,"futureCapability":{"mode":"new"}}}""", fragmented: true);
        await peer.Send("""{"seq":2,"type":"event","event":"initialized"}""");
        var reply = await pending.WaitAsync(Patience, TestContext.Current.CancellationToken);
        reply.Body!.Value.GetProperty("futureCapability").GetProperty("mode").GetString().Should().Be("new");
        (await peer.Client.Events.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(Patience, TestContext.Current.CancellationToken)).Event.Should().Be("initialized");
        (peer.Client.Completion.IsCompleted).Should().BeFalse();
    }

    [Fact]
    public async Task Utf8_lengths_and_fragmented_adjacent_events_preserve_values_and_order()
    {
        await using var peer = new Peer();
        var pending = peer.Request("evaluate", Json("""{"expression":"雪🙂é"}"""));
        var request = await peer.Receive();
        request.GetProperty("arguments").GetProperty("expression").GetString().Should().Be("雪🙂é");
        await peer.Send("""{"seq":1,"type":"event","event":"output","body":{"output":"雪🙂é"}}""", true);
        await peer.Send("""{"seq":2,"type":"event","event":"stopped","body":{"threadId":1}}""", true);
        await peer.Reply(request);
        await pending.WaitAsync(Patience, TestContext.Current.CancellationToken);
        var output = await peer.Client.Events.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(Patience, TestContext.Current.CancellationToken);
        var stopped = await peer.Client.Events.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(Patience, TestContext.Current.CancellationToken);
        output.Event.Should().Be("output");
        stopped.Event.Should().Be("stopped");
        output.Body!.Value.GetProperty("output").GetString().Should().Be("雪🙂é");
    }

    [Fact]
    public async Task Concurrent_requests_are_correlated_by_request_seq_not_response_order()
    {
        await using var peer = new Peer();
        var first = peer.Request("threads");
        var second = peer.Request("stackTrace", Json("""{"threadId":1}"""));
        var a = await peer.Receive();
        var b = await peer.Receive();
        (a.GetProperty("seq").GetInt32() < b.GetProperty("seq").GetInt32()).Should().BeTrue();
        await peer.Reply(b, """{"answer":"second"}""");
        (await second.WaitAsync(Patience, TestContext.Current.CancellationToken)).Body!.Value.GetProperty("answer").GetString().Should().Be("second");
        (first.IsCompleted).Should().BeFalse();
        await peer.Reply(a, """{"answer":"first"}""");
        (await first.WaitAsync(Patience, TestContext.Current.CancellationToken)).Body!.Value.GetProperty("answer").GetString().Should().Be("first");
    }

    [Fact]
    public async Task A_refusal_retains_the_structured_error_and_does_not_close_the_connection()
    {
        await using var peer = new Peer();
        var pending = peer.Request("launch");
        await peer.Receive();
        await peer.Send("""{"seq":1,"type":"response","request_seq":1,"command":"launch","success":false,"message":"Module is missing","body":{"error":{"id":42,"format":"Cannot launch {module}","variables":{"module":"Missing"}}}}""");
        var error = (await ((Func<Task>)(() => pending.WaitAsync(Patience, TestContext.Current.CancellationToken))).Should().ThrowExactlyAsync<DapRequestException>()).Which;
        error.Message.Should().Be("Module is missing");
        error.Response.Body!.Value.GetProperty("error").GetProperty("id").GetInt32().Should().Be(42);
        var next = peer.Request("threads");
        await peer.Reply(await peer.Receive());
        await next.WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Unknown_reverse_requests_receive_a_DAP_failure_response()
    {
        await using var peer = new Peer();
        await peer.Send("""{"seq":7,"type":"request","command":"runInTerminal","arguments":{}}""");
        var response = await peer.Receive();
        response.GetProperty("type").GetString().Should().Be("response");
        response.GetProperty("request_seq").GetInt32().Should().Be(7);
        response.GetProperty("command").GetString().Should().Be("runInTerminal");
        (response.GetProperty("success").GetBoolean()).Should().BeFalse();
        var next = peer.Request("threads");
        var request = await peer.Receive();
        request.GetProperty("seq").GetInt32().Should().Be(response.GetProperty("seq").GetInt32() + 1);
        await peer.Reply(request);
        await next.WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("Content-Length: -1\r\n\r\n")]
    [InlineData("Content-Length: 0\r\n\r\n")]
    [InlineData("Content-Length: 9999999999999999999\r\n\r\n")]
    [InlineData("Content-Length: 1\r\nContent-Length: 1\r\n\r\n{")]
    [InlineData("Other: 2\r\n\r\n{}")]
    [InlineData("Content-Length: 3\r\n\r\nno!")]
    [InlineData("Content-Length: 2\r\n\r\n[]")]
    [InlineData("BadHeader\r\n\r\n")]
    public async Task Invalid_frames_close_the_connection_and_fail_pending_requests(string wire)
    {
        await using var peer = new Peer();
        var first = peer.Request("threads");
        var second = peer.Request("scopes");
        await peer.Receive();
        await peer.Receive();
        await peer.Output.WriteAsync(Encoding.UTF8.GetBytes(wire), TestContext.Current.CancellationToken);
        (await peer.Client.Completion.WaitAsync(Patience, TestContext.Current.CancellationToken)).Should().BeOfType<InvalidDataException>();
        await ((Func<Task>)(() => first.WaitAsync(Patience, TestContext.Current.CancellationToken))).Should().ThrowExactlyAsync<IOException>();
        await ((Func<Task>)(() => second.WaitAsync(Patience, TestContext.Current.CancellationToken))).Should().ThrowExactlyAsync<IOException>();
    }

    [Theory]
    [InlineData("{\"seq\":1,\"type\":\"event\"}")]
    [InlineData("{\"seq\":1,\"type\":\"unknown\"}")]
    [InlineData("{\"seq\":1,\"type\":\"response\",\"request_seq\":1,\"command\":\"threads\"}")]
    [InlineData("{\"seq\":1,\"type\":\"response\",\"request_seq\":1,\"command\":\"wrong\",\"success\":true}")]
    public async Task Invalid_envelopes_do_not_leave_a_request_waiting(string json)
    {
        await using var peer = new Peer();
        var pending = peer.Request("threads");
        await peer.Receive();
        await peer.Send(json);
        (await peer.Client.Completion.WaitAsync(Patience, TestContext.Current.CancellationToken)).Should().BeOfType<InvalidDataException>();
        await ((Func<Task>)(() => pending.WaitAsync(Patience, TestContext.Current.CancellationToken))).Should().ThrowExactlyAsync<IOException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Content-Length: ")]
    [InlineData("Content-Length: 12\r\n\r\n{")]
    public async Task Eof_at_any_frame_boundary_fails_outstanding_work(string partial)
    {
        await using var peer = new Peer();
        var pending = peer.Request("threads");
        await peer.Receive();
        await peer.Output.WriteAsync(Encoding.ASCII.GetBytes(partial), TestContext.Current.CancellationToken);
        peer.Output.Dispose();
        (await peer.Client.Completion.WaitAsync(Patience, TestContext.Current.CancellationToken)).Should().BeOfType<EndOfStreamException>();
        await ((Func<Task>)(() => pending.WaitAsync(Patience, TestContext.Current.CancellationToken))).Should().ThrowExactlyAsync<IOException>();
    }

    [Fact]
    public async Task Local_cancellation_ignores_its_late_response_and_the_next_request_still_works()
    {
        await using var peer = new Peer();
        using var cancel = new CancellationTokenSource();
        var abandoned = peer.Request("evaluate", cancellationToken: cancel.Token);
        var old = await peer.Receive();
        cancel.Cancel();
        await ((Func<Task>)(() => abandoned.WaitAsync(Patience, TestContext.Current.CancellationToken))).Should().ThrowAsync<OperationCanceledException>();
        var next = peer.Request("evaluate");
        var current = await peer.Receive();
        await peer.Reply(old, """{"result":"old"}""");
        await peer.Reply(current, """{"result":"current"}""");
        (await next.WaitAsync(Patience, TestContext.Current.CancellationToken)).Body!.Value.GetProperty("result").GetString().Should().Be("current");
    }

    [Fact]
    public async Task A_request_timeout_is_bounded_and_does_not_retry()
    {
        await using var peer = new Peer(new() { RequestTimeout = TimeSpan.FromSeconds(1) });
        var pending = peer.Request("threads");
        await peer.Receive();
        await ((Func<Task>)(() => pending.WaitAsync(Patience, TestContext.Current.CancellationToken))).Should().ThrowExactlyAsync<TimeoutException>();
        (peer.Client.Completion.IsCompleted).Should().BeFalse();
        var next = peer.Request("threads", requestTimeout: Patience);
        var request = await peer.Receive();
        request.GetProperty("seq").GetInt32().Should().Be(2);
        await peer.Reply(request);
        await next.WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_request_can_override_the_connection_timeout()
    {
        await using var peer = new Peer();
        var timeout = TimeSpan.FromSeconds(1);
        var pending = peer.Request("threads", requestTimeout: timeout);
        await peer.Receive();
        var error = (await ((Func<Task>)(() => pending.WaitAsync(Patience, TestContext.Current.CancellationToken)))
            .Should().ThrowExactlyAsync<TimeoutException>()).Which;
        error.Message.Should().Be($"Debug adapter did not complete 'threads' within {timeout}.");
    }

    [Fact]
    public async Task A_request_override_can_outlast_the_connection_timeout()
    {
        await using var peer = new Peer(new() { RequestTimeout = TimeSpan.FromSeconds(1) });
        var pending = peer.Request("threads", requestTimeout: Patience);
        var request = await peer.Receive();
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await peer.Reply(request);
        await pending.WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(4294967295)]
    public async Task Invalid_request_timeout_overrides_are_refused_before_sending(long milliseconds)
    {
        await using var peer = new Peer();
        await ((Func<Task>)(() => peer.Request("threads", requestTimeout: TimeSpan.FromMilliseconds(milliseconds))))
            .Should().ThrowExactlyAsync<ArgumentOutOfRangeException>();
        var next = peer.Request("threads");
        var request = await peer.Receive();
        request.GetProperty("seq").GetInt32().Should().Be(1);
        await peer.Reply(request);
        await next.WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Disposal_is_idempotent_unblocks_the_reader_and_fails_pending_and_future_requests()
    {
        await using var peer = new Peer();
        var pending = peer.Request("threads");
        await peer.Receive();
        await peer.Client.DisposeAsync().AsTask().WaitAsync(Patience, TestContext.Current.CancellationToken);
        await peer.Client.DisposeAsync();
        (await peer.Client.Completion).Should().BeNull();
        await ((Func<Task>)(() => pending.WaitAsync(Patience, TestContext.Current.CancellationToken))).Should().ThrowExactlyAsync<IOException>();
        await ((Func<Task>)(() => peer.Request("threads"))).Should().ThrowExactlyAsync<IOException>();
    }

    [Fact]
    public async Task Pending_request_limit_refuses_work_without_corrupting_the_connection()
    {
        await using var peer = new Peer(new() { MaximumPendingRequests = 1 });
        var pending = peer.Request("threads");
        var request = await peer.Receive();
        await ((Func<Task>)(() => peer.Request("threads"))).Should().ThrowExactlyAsync<InvalidOperationException>();
        await peer.Reply(request);
        await pending.WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Oversized_frame_is_refused_before_waiting_for_its_body()
    {
        await using var peer = new Peer(new() { MaximumMessageBytes = 100 });
        await peer.Output.WriteAsync("Content-Length: 101\r\n\r\n"u8.ToArray(), TestContext.Current.CancellationToken);
        (await peer.Client.Completion.WaitAsync(Patience, TestContext.Current.CancellationToken)).Should().BeOfType<InvalidDataException>();
    }

    [Fact]
    public async Task An_unterminated_header_has_a_limit()
    {
        await using var peer = new Peer(new() { MaximumHeaderBytes = 32 });
        await peer.Output.WriteAsync(Enumerable.Repeat((byte)'a', 33).ToArray(), TestContext.Current.CancellationToken);
        (await peer.Client.Completion.WaitAsync(Patience, TestContext.Current.CancellationToken)).Should().BeOfType<InvalidDataException>();
    }

    [Fact]
    public async Task Event_backlog_closes_instead_of_silently_dropping_state()
    {
        await using var peer = new Peer(new() { EventCapacity = 1 });
        await peer.Send("""{"seq":1,"type":"event","event":"stopped"}""");
        await peer.Send("""{"seq":2,"type":"event","event":"terminated"}""");
        var failure = (await peer.Client.Completion.WaitAsync(Patience, TestContext.Current.CancellationToken)).Should().BeOfType<IOException>().Which;
        failure.Message.Should().Contain("event queue");
    }

    private sealed class BlockingOutput(bool failOnDisposal = false) : Stream
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Writes { get; private set; }
        public bool Disposed { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        {
            Writes++;
            Entered.TrySetResult();
            if (failOnDisposal)
            {
                // Model a process pipe whose in-flight write resumes after its handle is disposed.
                await _disposed.Task;
                throw new ObjectDisposedException("SafeFileHandle");
            }
            // A stream may already have written part of the header when cancellation is observed.
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; _disposed.TrySetResult(); base.Dispose(disposing); }
    }

    [Fact]
    public async Task Disposal_during_an_in_flight_write_reports_a_connection_failure()
    {
        var input = new Pipe();
        var output = new BlockingOutput(failOnDisposal: true);
        await using var client = new DapConnection(input.Reader.AsStream(), output);
        var pending = client.RequestAsync("launch", cancellationToken: TestContext.Current.CancellationToken);
        await output.Entered.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        var queued = client.RequestAsync("threads", cancellationToken: TestContext.Current.CancellationToken);
        await client.DisposeAsync().AsTask().WaitAsync(Patience, TestContext.Current.CancellationToken);
        await ((Func<Task>)(() => pending.WaitAsync(Patience, TestContext.Current.CancellationToken))).Should().ThrowExactlyAsync<IOException>();
        await ((Func<Task>)(() => queued.WaitAsync(Patience, TestContext.Current.CancellationToken))).Should().ThrowExactlyAsync<IOException>();
        (await client.Completion).Should().BeNull();
        output.Disposed.Should().BeTrue();
        output.Writes.Should().Be(1);
        await input.Writer.CompleteAsync();
    }

    [Fact]
    public async Task Cancellation_during_a_write_closes_the_connection_instead_of_reusing_a_partial_frame()
    {
        var input = new Pipe();
        var output = new BlockingOutput();
        await using var client = new DapConnection(input.Reader.AsStream(), output);
        using var cancel = new CancellationTokenSource();
        var pending = client.RequestAsync("launch", cancellationToken: cancel.Token);
        await output.Entered.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        var queued = client.RequestAsync("threads", cancellationToken: TestContext.Current.CancellationToken);
        cancel.Cancel();
        await ((Func<Task>)(() => pending.WaitAsync(Patience, TestContext.Current.CancellationToken))).Should().ThrowAsync<OperationCanceledException>();
        await ((Func<Task>)(() => queued.WaitAsync(Patience, TestContext.Current.CancellationToken))).Should().ThrowExactlyAsync<IOException>();
        (await client.Completion.WaitAsync(Patience, TestContext.Current.CancellationToken)).Should().BeOfType<IOException>();
        (output.Disposed).Should().BeTrue();
        output.Writes.Should().Be(1);
        await input.Writer.CompleteAsync();
    }

    [Fact]
    public async Task Cancellation_while_waiting_for_the_writer_does_not_close_the_connection_or_write_a_frame()
    {
        var input = new Pipe();
        var output = new BlockingOutput();
        await using var client = new DapConnection(input.Reader.AsStream(), output);
        using var firstCancel = new CancellationTokenSource();
        var first = client.RequestAsync("launch", cancellationToken: firstCancel.Token);
        await output.Entered.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        using var queuedCancel = new CancellationTokenSource();
        var queued = client.RequestAsync("threads", cancellationToken: queuedCancel.Token);
        queuedCancel.Cancel();
        await ((Func<Task>)(() => queued.WaitAsync(Patience, TestContext.Current.CancellationToken))).Should().ThrowAsync<OperationCanceledException>();
        (client.Completion.IsCompleted).Should().BeFalse();
        output.Writes.Should().Be(1);
        firstCancel.Cancel();
        await ((Func<Task>)(() => first.WaitAsync(Patience, TestContext.Current.CancellationToken))).Should().ThrowAsync<OperationCanceledException>();
        await input.Writer.CompleteAsync();
    }

    [Fact]
    public async Task Oversized_outbound_arguments_are_refused_before_sending_any_bytes()
    {
        await using var peer = new Peer(new() { MaximumMessageBytes = 100 });
        await ((Func<Task>)(() => peer.Request("evaluate",
            Json("{\"expression\":\"" + new string('x', 200) + "\"}")))).Should().ThrowExactlyAsync<ArgumentException>();
        var next = peer.Request("threads");
        var request = await peer.Receive();
        request.GetProperty("command").GetString().Should().Be("threads");
        await peer.Reply(request);
        await next.WaitAsync(Patience, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Header_extensions_are_accepted_and_content_length_is_case_insensitive()
    {
        await using var peer = new Peer();
        const string body = "{\"seq\":1,\"type\":\"event\",\"event\":\"initialized\"}";
        await peer.Output.WriteAsync(Encoding.ASCII.GetBytes($"X-Adapter: future\r\ncontent-length: {body.Length}\r\n\r\n{body}"), TestContext.Current.CancellationToken);
        (await peer.Client.Events.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(Patience, TestContext.Current.CancellationToken)).Event.Should().Be("initialized");
    }

    private sealed class YieldingOutput(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        {
            foreach (var i in Enumerable.Range(0, buffer.Length))
            {
                await Task.Yield();
                await inner.WriteAsync(buffer.Slice(i, 1), token);
            }
        }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    [Fact]
    public async Task Concurrent_writers_cannot_interleave_headers_or_bodies_even_when_each_byte_yields()
    {
        await using var peer = new Peer(slowClientWrites: true);
        var calls = Enumerable.Range(0, 20).Select(i => peer.Request("evaluate", Json($$"""{"expression":"value{{i}}"}"""))).ToArray();
        var requests = new List<JsonElement>();
        for (var i = 0; i < calls.Length; ++i)
        {
            var request = await peer.Receive();
            request.GetProperty("seq").GetInt32().Should().Be(i + 1);
            requests.Add(request);
        }
        foreach (var request in requests.AsEnumerable().Reverse())
            await peer.Reply(request, request.GetProperty("arguments").GetRawText());
        var replies = await Task.WhenAll(calls).WaitAsync(Patience, TestContext.Current.CancellationToken);
        for (var i = 0; i < replies.Length; ++i)
            replies[i].Body!.Value.GetProperty("expression").GetString().Should().Be($"value{i}");
    }
}
