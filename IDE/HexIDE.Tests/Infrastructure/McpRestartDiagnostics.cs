using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace HexIDE.Tests.Infrastructure;

internal sealed class McpRestartDiagnostics(Action<LogLevel, string> write)
{
    private readonly object gate = new();
    private sealed class Phase(string name, TimeSpan start)
    {
        public string Name { get; } = name;
        public TimeSpan Start { get; } = start;
        public TimeSpan? End { get; set; }
    }

    private readonly List<Phase> phases = new();
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private int connections;
    private int reconnects;
    private bool restarting;

    public async Task<T> PhaseAsync<T>(string phase, Func<Task<T>> action)
    {
        var timing = new Phase(phase, elapsed.Elapsed);
        lock (gate)
            phases.Add(timing);
        try
        {
            return await action();
        }
        finally
        {
            lock (gate)
            {
                timing.End = elapsed.Elapsed;
                write(LogLevel.Debug, Describe(timing) + $"; Reconnect attempts: {reconnects}");
            }
        }
    }

    public Task PhaseAsync(string phase, Func<Task> action) => PhaseAsync(phase, async () =>
    {
        await action();
        return true;
    });

    public Task<T> ConnectAsync<T>(Func<Task<T>> action)
    {
        string phase;
        lock (gate)
        {
            connections++;
            phase = restarting ? $"reconnect attempt {++reconnects}"
                : connections == 1 ? "first connect attempt" : $"connect retry {connections - 1}";
        }
        return PhaseAsync(phase, action);
    }

    public void Restarting()
    {
        lock (gate)
            restarting = true;
    }

    private string Describe(Phase phase) => string.Create(CultureInfo.InvariantCulture,
        $"{phase.Name}: {((phase.End ?? elapsed.Elapsed) - phase.Start).TotalMilliseconds:F3} ms (started at {phase.Start.TotalMilliseconds:F3} ms){(phase.End is null ? "; pending" : "")}");

    public string Failure(Exception exception)
    {
        lock (gate)
            return $"MCP restart diagnostic; reconnect attempts count TCP opens after stop (pooling retained).{Environment.NewLine}"
                + $"Reconnect attempts: {reconnects}{Environment.NewLine}"
                + string.Join(Environment.NewLine, phases.Select(Describe)) + Environment.NewLine + exception;
    }
}
