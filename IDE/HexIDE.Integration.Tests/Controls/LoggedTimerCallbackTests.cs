using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using HexIDE.Utils;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace HexIDE.Integration.Tests.Controls;

public class LoggedTimerCallbackTests
{
    [AvaloniaTheory]
    [InlineData("Tick")]
    [InlineData("Run")]
    [InlineData("RunOnce")]
    public async Task CallbackFailure_IsLoggedWithPurposeAndException(string form)
    {
        var previous = Log.Logger;
        var sink = new Capture();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        Log.Logger = logger;
        var error = new InvalidOperationException("timer probe");
        var called = false;
        Action callback = () => LoggedTimerCallback.Run("timer probe purpose", () =>
        {
            called = true;
            throw error;
        });
        using var timer = Start(form, callback);
        try
        {
            await PumpUntil(() => called);
            sink.Events.Should().ContainSingle().Which.Should().Match<LogEvent>(e =>
                e.Level == LogEventLevel.Error && ReferenceEquals(e.Exception, error) &&
                e.RenderMessage().Contains("timer probe purpose"));
        }
        finally
        {
            Log.Logger = previous;
        }
    }

    [AvaloniaTheory]
    [InlineData("Tick")]
    [InlineData("Run")]
    public async Task UnwrappedFailure_DoesNotReachDispatcherHandler(string form)
    {
        var called = false;
        var unhandled = false;
        void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            unhandled = true;
            e.Handled = true;
        }
        Dispatcher.UIThread.UnhandledException += OnUnhandled;
        using var timer = Start(form, () =>
        {
            called = true;
            throw new InvalidOperationException("unwrapped timer probe");
        });
        try
        {
            await PumpUntil(() => called);
            Dispatcher.UIThread.RunJobs();
            unhandled.Should().BeFalse();
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= OnUnhandled;
        }
    }

    private static IDisposable Start(string form, Action callback)
    {
        if (form == "RunOnce")
            return DispatcherTimer.RunOnce(callback, TimeSpan.FromMilliseconds(1));
        if (form == "Run")
            return DispatcherTimer.Run(() => { callback(); return false; }, TimeSpan.FromMilliseconds(1));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1) };
        timer.Tick += (_, _) => { timer.Stop(); callback(); };
        timer.Start();
        return new StopTimer(timer);
    }

    private static async Task PumpUntil(Func<bool> done)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
        }
        done().Should().BeTrue("the timer must actually fire");
    }

    private sealed class StopTimer(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }

    private sealed class Capture : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
