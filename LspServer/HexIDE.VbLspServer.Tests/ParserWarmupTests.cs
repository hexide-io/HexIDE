// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

using System.IO.Pipelines;
using System.Text.Json;
using StreamJsonRpc;

namespace HexIDE.VbLspServer.Tests;

[Collection("Server timing")]
public class ParserWarmupTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Startup_runs_and_logs_the_real_LL_warmup()
    {
        var previous = Serilog.Log.Logger;
        var sink = new WarmupSink();
        using var logger = new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        Serilog.Log.Logger = logger;
        try
        {
            var code = await global::Program.RunServerAsync(async () =>
            {
                await sink.Finished.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
                return 0;
            });
            code.Should().Be(0);
            var message = await sink.Finished.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
            message.Properties["Prediction"].Should().BeOfType<Serilog.Events.ScalarValue>()
                .Which.Value.Should().Be(ParsePrediction.Ll);
            message.Properties.Should().ContainKey("ElapsedMs");
        }
        finally { Serilog.Log.Logger = previous; }
    }

    private sealed class WarmupSink : Serilog.Core.ILogEventSink
    {
        internal TaskCompletionSource<Serilog.Events.LogEvent> Finished { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Emit(Serilog.Events.LogEvent logEvent)
        {
            if (logEvent.MessageTemplate.Text == "Parser warm-up finished in {ElapsedMs} ms ({Prediction})")
                Finished.TrySetResult(logEvent);
        }
    }

    [Fact]
    public async Task Initialize_replies_while_background_LL_warmup_is_pending()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource<ParseReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var c2s = new Pipe();
        var s2c = new Pipe();
        var server = LspServerHost.Create(c2s.Reader.AsStream(), s2c.Writer.AsStream());
        var loop = global::Program.RunServerAsync(async () =>
        {
            await server.Run();
            return 0;
        }, () =>
        {
            started.SetResult();
            if (!release.Wait(Timeout))
                throw new TimeoutException("Test did not release the warm-up");
            var report = ParserWarmup.Run();
            finished.SetResult(report);
            return report;
        });
        using var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(
            c2s.Writer.AsStream(), s2c.Reader.AsStream(), new SystemTextJsonFormatter()));
        rpc.StartListening();
        try
        {
            await started.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
            var init = await rpc.InvokeWithParameterObjectAsync<JsonElement>("initialize",
                new { processId = (int?)null, rootUri = (string?)null, capabilities = new { } },
                TestContext.Current.CancellationToken).WaitAsync(Timeout, TestContext.Current.CancellationToken);
            init.GetProperty("serverInfo").GetProperty("name").GetString()
                .Should().Be("HexIDE VB6 Language Server");
            finished.Task.IsCompleted.Should().BeFalse();
            release.Set();
            var report = await finished.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
            report.Prediction.Should().Be(ParsePrediction.Ll);
            report.Outcome.Should().Be(ParseOutcome.Parsed);
        }
        finally
        {
            release.Set();
            server.Exit();
            await loop.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Warmup_failure_does_not_change_server_exit_code()
    {
        var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var code = await global::Program.RunServerAsync(async () =>
        {
            await attempted.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
            return 0;
        }, () =>
        {
            attempted.SetResult();
            throw new InvalidOperationException("warm-up failed");
        });
        code.Should().Be(0);
    }
}
