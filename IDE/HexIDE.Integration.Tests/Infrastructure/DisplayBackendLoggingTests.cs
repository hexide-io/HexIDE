using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using HexIDE.Startup;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace HexIDE.Integration.Tests.Infrastructure;

public class DisplayBackendLoggingTests
{
    [AvaloniaFact]
    public void First_window_logs_actual_backend_once_even_when_requested_backend_differs()
    {
        var sink = new CapturingSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        var builder = AppBuilder.Configure<Application>().UseDisplayBackend("auto", logger);
        builder.AfterSetupCallback(builder);
        var first = new Window();
        var second = new Window();
        try
        {
            first.Show();
            second.Show();
            var entry = sink.Events.Should().ContainSingle().Subject;
            entry.Properties["Backend"].Should().BeOfType<ScalarValue>()
                .Which.Value.Should().Be(first.PlatformImpl!.GetType().FullName);
            entry.Properties["Selection"].Should().BeOfType<ScalarValue>()
                .Which.Value.Should().Be("auto");
        }
        finally
        {
            first.Close();
            second.Close();
        }
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
