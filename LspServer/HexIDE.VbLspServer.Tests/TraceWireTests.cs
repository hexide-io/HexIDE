// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// The server's own trace channel, asserted on the frames that come back rather than on our own calls
// returning. Everything here is driven through StreamJsonRpc over an in-memory pipe pair — the transport
// the real IDE client uses — and every assertion is about a $/logTrace frame's contents.

using System.IO.Pipelines;
using System.Text.Json;
using System.Threading.Channels;
using EmmyLua.LanguageServer.Framework.Server;
using StreamJsonRpc;

namespace HexIDE.VbLspServer.Tests;

public class TraceWireTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private const string Uri = "vb6://module/Test";
    private const string QuietUri = "vb6://module/Quiet";
    private const string LoudUri = "vb6://module/Loud";

    private const string CleanDoc = "Sub DoWork()\nEnd Sub\nSub Other()\nEnd Sub\n";
    private const string BrokenDoc = "Sub DoWork()\n    @@\nEnd Sub\n";

    // ── off ─────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task No_trace_is_emitted_when_initialize_omits_the_member()
    {
        // Absent means off, and off means nothing on the wire.
        await using var h = await StartAsync(trace: null);
        await AssertNothingWasTracedYetAsync(h);
    }

    [Fact]
    public async Task No_trace_is_emitted_when_initialize_asks_for_off()
    {
        await using var h = await StartAsync(trace: "off");
        await AssertNothingWasTracedYetAsync(h);
    }

    // ── messages ────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Messages_emits_one_summary_line_and_no_verbose_member()
    {
        await using var h = await StartAsync(trace: "messages");
        await OpenAndSettleAsync(h, Uri, CleanDoc);

        var frame = await h.NextTraceAsync();

        Message(frame).Should().NotBeNullOrWhiteSpace();
        frame.TryGetProperty("verbose", out _).Should()
            .BeFalse("the verbose member is sent only at the verbose level");
    }

    [Fact]
    public async Task The_summary_names_the_prediction_stage_the_cost_and_the_counts()
    {
        // The point of the whole channel: none of this is recoverable from a wire capture. The client saw
        // the diagnostics that came out; it cannot see which of the two prediction stages produced them.
        await using var h = await StartAsync(trace: "messages");
        await OpenAndSettleAsync(h, Uri, CleanDoc);

        Message(await h.NextTraceAsync()).Should()
            .MatchRegex($@"^{Uri}: SLL, [\d.]+ ms, 0 diagnostics, 2 symbols$");
    }

    [Fact]
    public async Task The_summary_names_the_LL_fallback_when_the_fast_path_bails()
    {
        // Same client, same request, wildly different cost — and the only observable difference is here.
        await using var h = await StartAsync(trace: "messages");
        await OpenAndSettleAsync(h, Uri, BrokenDoc);

        Message(await h.NextTraceAsync()).Should()
            .MatchRegex($@"^{Uri}: LL fallback, [\d.]+ ms, \d+ diagnostics?, \d+ symbols?$");
    }

    // ── verbose ─────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Verbose_emits_the_same_summary_line_plus_the_detail()
    {
        await using var h = await StartAsync(trace: "verbose");
        await OpenAndSettleAsync(h, Uri, CleanDoc);

        var frame = await h.NextTraceAsync();

        // The same shape the messages level produces — the level changes what accompanies the line, not
        // the line.
        Message(frame).Should().MatchRegex($@"^{Uri}: SLL, [\d.]+ ms, 0 diagnostics, 2 symbols$");

        frame.TryGetProperty("verbose", out var verbose).Should().BeTrue();
        var detail = verbose.GetString();
        detail.Should().Contain("prediction: SLL");
        detail.Should().Contain("budget", "why an analysis was abandoned is the question this answers");
        detail.Should().Contain("symbols: 2");
    }

    // ── $/setTrace ──────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task SetTrace_turns_tracing_on_for_a_running_server()
    {
        await using var h = await StartAsync(trace: null);
        await OpenAndSettleAsync(h, QuietUri, CleanDoc);
        h.DrainTrace().Should().BeEmpty();

        await SetTraceAsync(h, "verbose");
        await OpenAndSettleAsync(h, LoudUri, CleanDoc);

        var frame = await h.NextTraceAsync();
        Message(frame).Should().StartWith(LoudUri);
        frame.TryGetProperty("verbose", out _).Should().BeTrue("the level asked for was verbose");
    }

    [Fact]
    public async Task SetTrace_turns_tracing_off_for_a_running_server()
    {
        await using var h = await StartAsync(trace: "verbose");
        await OpenAndSettleAsync(h, Uri, CleanDoc);
        (await h.NextTraceAsync()).ValueKind.Should().Be(JsonValueKind.Object); // tracing was on

        await SetTraceAsync(h, "off");
        await AssertNothingWasTracedYetAsync(h);
    }

    [Fact]
    public async Task SetTrace_with_an_unrecognised_value_is_ignored_and_the_level_survives()
    {
        // Three distinct wrong answers are ruled out by this one test: silently going off (nothing would
        // arrive at all), silently downgrading to messages (the verbose member would be gone), and
        // accepting the nonsense as a level of its own.
        await using var h = await StartAsync(trace: "verbose");

        await SetTraceAsync(h, "chatty");

        var refusal = await h.NextTraceAsync();
        Message(refusal).Should().Contain("chatty").And.Contain("not recognised");
        Message(refusal).Should().Contain("level stays verbose");

        await OpenAndSettleAsync(h, Uri, CleanDoc);
        var frame = await h.NextTraceAsync();
        Message(frame).Should().StartWith(Uri);
        frame.TryGetProperty("verbose", out _).Should()
            .BeTrue("an unrecognised value is ignored, so the level is still verbose");
    }

    [Fact]
    public async Task SetTrace_with_no_usable_params_is_ignored_rather_than_fatal()
    {
        await using var h = await StartAsync(trace: "messages");

        await h.Rpc.NotifyWithParameterObjectAsync("$/setTrace", new { });

        Message(await h.NextTraceAsync()).Should().Contain("not recognised");

        // Still serving, and still at the level it was set to.
        await OpenAndSettleAsync(h, Uri, CleanDoc);
        Message(await h.NextTraceAsync()).Should().StartWith(Uri);
    }

    // ── decisions the wire cannot show ──────────────────────────────────────────────────────────
    [Fact]
    public async Task A_refused_ranged_change_says_so_on_the_trace_channel()
    {
        // On the wire the refusal is an empty diagnostics array — identical to a file with nothing wrong.
        // A developer wondering why their document went quiet has nowhere else to look.
        await using var h = await StartAsync(trace: "messages");
        await OpenAndSettleAsync(h, Uri, CleanDoc);
        await h.NextTraceAsync(); // the open's own analysis line

        await h.Rpc.NotifyWithParameterObjectAsync("textDocument/didChange", new
        {
            textDocument = new { uri = Uri, version = 2 },
            contentChanges = new[]
            {
                new
                {
                    range = new { start = new { line = 0, character = 4 }, end = new { line = 0, character = 6 } },
                    rangeLength = 2,
                    text = "X = 1",
                },
            },
        });
        await h.NextDiagnosticsAsync();

        Message(await h.NextTraceAsync()).Should()
            .Contain("ranged contentChange refused").And.Contain("evicted");
    }

    // ── harness ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Proves silence soundly. Draining the channel on its own is weak — a frame the server had already
    /// written could still be in flight. So this also turns tracing on afterwards and asserts that the
    /// FIRST frame ever to arrive is the one deliberately provoked: a channel preserves order, so anything
    /// emitted while the level was off would have had to arrive ahead of it.
    /// </summary>
    private static async Task AssertNothingWasTracedYetAsync(Harness h)
    {
        await OpenAndSettleAsync(h, QuietUri, CleanDoc);
        h.DrainTrace().Should().BeEmpty("the level is off");

        await SetTraceAsync(h, "messages");
        await OpenAndSettleAsync(h, LoudUri, CleanDoc);

        Message(await h.NextTraceAsync()).Should()
            .StartWith(LoudUri, "anything traced while the level was off would have arrived before this");
    }

    /// <summary>
    /// Opens a document and waits until everything it caused has been written. The request round-trip is
    /// the barrier: the server dispatches sequentially and writes in dispatch order, so its response
    /// cannot overtake a notification the open produced.
    /// </summary>
    private static async Task OpenAndSettleAsync(Harness h, string uri, string text)
    {
        await h.Rpc.NotifyWithParameterObjectAsync("textDocument/didOpen",
            new { textDocument = new { uri, languageId = "vb6", version = 1, text } });
        await h.NextDiagnosticsAsync();
        await h.Rpc.InvokeWithParameterObjectAsync<JsonElement>("textDocument/documentSymbol",
            new { textDocument = new { uri } }).WaitAsync(Timeout);
    }

    private static Task SetTraceAsync(Harness h, string value) =>
        h.Rpc.NotifyWithParameterObjectAsync("$/setTrace", new { value });

    private static string Message(JsonElement frame) => frame.GetProperty("message").GetString()!;

    private static async Task<Harness> StartAsync(string? trace)
    {
        var h = new Harness();
        object initParams = trace is null
            ? new { processId = (int?)null, rootUri = (string?)null, capabilities = new { } }
            : new { processId = (int?)null, rootUri = (string?)null, capabilities = new { }, trace };

        await h.Rpc.InvokeWithParameterObjectAsync<JsonElement>("initialize", initParams).WaitAsync(Timeout);
        await h.Rpc.NotifyWithParameterObjectAsync("initialized", new { });
        return h;
    }

    private sealed class Harness : IAsyncDisposable
    {
        public JsonRpc Rpc { get; }
        private readonly ClientTarget _client = new();
        private readonly LanguageServer _server;
        private readonly Task _loop;

        public Harness()
        {
            var c2s = new Pipe();
            var s2c = new Pipe();
            _server = LspServerHost.Create(c2s.Reader.AsStream(), s2c.Writer.AsStream());
            _loop = _server.Run();

            var handler = new HeaderDelimitedMessageHandler(
                c2s.Writer.AsStream(), s2c.Reader.AsStream(), new SystemTextJsonFormatter());
            Rpc = new JsonRpc(handler);
            Rpc.AddLocalRpcTarget(_client);
            Rpc.StartListening();
        }

        public Task<JsonElement> NextDiagnosticsAsync() =>
            _client.Diagnostics.Reader.ReadAsync().AsTask().WaitAsync(Timeout);

        public Task<JsonElement> NextTraceAsync() =>
            _client.Trace.Reader.ReadAsync().AsTask().WaitAsync(Timeout);

        public IReadOnlyList<JsonElement> DrainTrace()
        {
            var frames = new List<JsonElement>();
            while (_client.Trace.Reader.TryRead(out var frame)) frames.Add(frame);
            return frames;
        }

        public async ValueTask DisposeAsync()
        {
            Rpc.Dispose();
            _server.Exit();
            try { await _loop.WaitAsync(Timeout); } catch { /* best effort */ }
        }

        private sealed class ClientTarget
        {
            public Channel<JsonElement> Diagnostics { get; } = Channel.CreateUnbounded<JsonElement>();
            public Channel<JsonElement> Trace { get; } = Channel.CreateUnbounded<JsonElement>();

            [JsonRpcMethod("textDocument/publishDiagnostics", UseSingleObjectParameterDeserialization = true)]
            public void Publish(JsonElement diagnostics) => Diagnostics.Writer.TryWrite(diagnostics);

            [JsonRpcMethod("$/logTrace", UseSingleObjectParameterDeserialization = true)]
            public void LogTrace(JsonElement trace) => Trace.Writer.TryWrite(trace);
        }
    }
}
