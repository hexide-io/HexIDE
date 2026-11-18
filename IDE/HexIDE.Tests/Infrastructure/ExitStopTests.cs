using System.Diagnostics;
using System.IO;
using Avalonia.Controls.ApplicationLifetimes;
using HexIDE.Infrastructure;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace HexIDE.Tests.Infrastructure;

public class ExitStopTests
{
    [AvaloniaFact]
    public async Task First_request_is_cancelled_and_retry_follows_shutdown_and_exit_on_stdio()
    {
        var file = Path.GetTempFileName();
        await using var client = CreateClient(file);
        try
        {
            await client.StartAsync(TestContext.Current.CancellationToken);
            client.IsRunning.Should().BeTrue();
            var lifetime = new Lifetime();
            var order = new List<string>();
            string[] receivedAtRetry = [];
            lifetime.BeforeRequest = () => receivedAtRetry = ReadMethods(file);
            var exit = new ExitStop(lifetime.Desktop, () => order.Add("watcher"), async () =>
            {
                order.Add("stop");
                await client.StopAsync();
                order.Add("stopped");
            }, () => order.Add("logging"), ExitStop.StopTimeout);
            lifetime.Desktop.ShutdownRequested += exit.OnShutdownRequested;

            lifetime.Request().Should().BeFalse("the lifetime reads Cancel as soon as the handler returns");
            lifetime.Request().Should().BeFalse("another request during stop must also wait");
            await lifetime.Exited.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

            receivedAtRetry.Should().ContainInOrder("shutdown", "exit");
            order.Should().Equal("watcher", "stop", "stopped", "logging");
            lifetime.Retries.Should().Be(1);
            lifetime.Request().Should().BeTrue("requests after stopping pass through");
        }
        finally { File.Delete(file); }
    }

    [AvaloniaFact]
    public async Task Unanswered_shutdown_cannot_hold_the_lifetime_past_the_exit_bound()
    {
        var file = Path.GetTempFileName();
        await using var client = CreateClient(file, unanswered: true);
        Task? stopping = null;
        try
        {
            await client.StartAsync(TestContext.Current.CancellationToken);
            client.IsRunning.Should().BeTrue();
            var lifetime = new Lifetime();
            var loggingClosed = false;
            using var logs = new ExitLogs();
            // Below the client's own 2 s shutdown deadline: only ExitStop's bound can release this exit.
            var bound = TimeSpan.FromMilliseconds(500);
            var exit = new ExitStop(lifetime.Desktop, () => { },
                () => stopping = client.StopAsync(), () => loggingClosed = true, bound);
            lifetime.Desktop.ShutdownRequested += exit.OnShutdownRequested;
            var watch = Stopwatch.StartNew();

            lifetime.Request().Should().BeFalse();
            await lifetime.Exited.Task.WaitAsync(bound + TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

            watch.Elapsed.Should().BeLessThan(bound + TimeSpan.FromSeconds(1));
            stopping.Should().NotBeNull();
            stopping!.IsCompleted.Should().BeFalse("the lifetime must stop waiting even though the client is still stopping");
            loggingClosed.Should().BeTrue();
            logs.Events.Should().ContainSingle(e => e.Level == LogEventLevel.Error && e.Exception is TimeoutException);
            ReadMethods(file).Should().Contain("shutdown");
        }
        finally
        {
            if (stopping is not null) await stopping.WaitAsync(TimeSpan.FromSeconds(15));
            File.Delete(file);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completed_or_failed_stop_retries_after_the_first_handler_returns(bool fail)
    {
        var lifetime = new Lifetime();
        var order = new List<string>();
        using var logs = new ExitLogs();
        var exit = new ExitStop(lifetime.Desktop, () => order.Add("watcher"), () =>
        {
            order.Add("stop");
            if (fail) throw new IOException("stop failed");
            return Task.CompletedTask;
        }, () => order.Add("logging"), ExitStop.StopTimeout);
        lifetime.Desktop.ShutdownRequested += exit.OnShutdownRequested;

        lifetime.Request().Should().BeFalse();
        lifetime.Retries.Should().Be(0, "a synchronous stop must not recursively shut down inside the first request");
        await lifetime.Exited.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        order.Should().Equal("watcher", "stop", "logging");
        lifetime.Retries.Should().Be(1);
        logs.Events.Should().HaveCount(fail ? 1 : 0);
        if (fail) logs.Events[0].Exception.Should().BeOfType<IOException>();
    }

    [AvaloniaFact]
    public async Task An_earlier_veto_leaves_services_running()
    {
        var lifetime = new Lifetime();
        var calls = new List<string>();
        var veto = true;
        lifetime.Desktop.ShutdownRequested += (_, e) => e.Cancel = veto;
        var exit = new ExitStop(lifetime.Desktop, () => calls.Add("watcher"),
            () => { calls.Add("stop"); return Task.CompletedTask; }, () => calls.Add("logging"), ExitStop.StopTimeout);
        lifetime.Desktop.ShutdownRequested += exit.OnShutdownRequested;

        lifetime.Request().Should().BeFalse();
        await Task.Yield();
        calls.Should().BeEmpty();
        veto = false;
        lifetime.Request().Should().BeFalse();
        await lifetime.Exited.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        calls.Should().Equal("watcher", "stop", "logging");
    }

    internal static string[] ReadMethods(string file)
    {
        // The server may still be appending a notification when an active-connection assertion reads.
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    internal static VBLspClient CreateClient(string file, bool unanswered = false)
    {
        var host = Path.Join(AppContext.BaseDirectory, "LspTestHost", "HexIDE.Lsp.TestHost.dll");
        var transport = new StdioProcessLspTransport(new LspServerInfo("dotnet", "", "")
        {
            ArgumentList = [host, "recording-server", file, unanswered ? "unanswered" : "answer"],
        }, NullLogger<StdioProcessLspTransport>.Instance);
        return new VBLspClient(transport, NullLogger<VBLspClient>.Instance, "exit-test");
    }

    private sealed class Lifetime
    {
        internal IClassicDesktopStyleApplicationLifetime Desktop { get; } = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        internal TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action? BeforeRequest { get; set; }
        internal int Retries { get; private set; }

        internal Lifetime()
        {
            Desktop.TryShutdown().Returns(_ =>
            {
                Retries++;
                BeforeRequest?.Invoke();
                var accepted = Request();
                if (accepted) Exited.TrySetResult();
                return accepted;
            });
        }

        internal bool Request()
        {
            var e = new ShutdownRequestedEventArgs();
            Desktop.ShutdownRequested += Raise.Event<EventHandler<ShutdownRequestedEventArgs>>(Desktop, e);
            return !e.Cancel;
        }
    }

    private sealed class ExitLogs : ILogEventSink, IDisposable
    {
        private readonly Serilog.ILogger previous = Log.Logger;
        private readonly Logger logger;
        private bool disposed;
        internal List<LogEvent> Events { get; } = [];
        internal ExitLogs()
        {
            logger = new LoggerConfiguration().WriteTo.Sink(this).CreateLogger();
            Log.Logger = logger;
        }
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Log.Logger = previous;
            logger.Dispose();
        }
    }
}
