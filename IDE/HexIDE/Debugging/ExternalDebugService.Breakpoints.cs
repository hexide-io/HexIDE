using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HexIDE.Dap;

namespace HexIDE.Debugging;

public sealed record ExternalBreakpoint(int RequestedLine, int? Line = null, int? Id = null,
    bool Verified = false, bool Pending = true, string Message = "")
{
    public bool Relocated => Verified && Line is { } line && line != RequestedLine;
}

public sealed partial class ExternalDebugService
{
    private sealed class BreakpointSource(string path)
    {
        public string Path { get; } = path;
        public SortedSet<int> Desired { get; set; } = [];
        public IReadOnlyList<ExternalBreakpoint> Rows { get; set; } = [];
        public long Version { get; set; }
        public bool Sending { get; set; }
    }

    private readonly Dictionary<string, BreakpointSource> breakpointSources = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private bool breakpointsReady;
    public bool CanEditBreakpoints => IsActive && breakpointsReady && session?.Snapshot.State is DapSessionState.Running or DapSessionState.Stopped;

    private void ResetBreakpoints()
    {
        breakpointsReady = false;
        breakpointSources.Clear();
    }

    private IReadOnlyList<DapSourceBreakpoints> InitializeBreakpoints(IReadOnlyList<DapSourceBreakpoints> sources)
    {
        foreach (var source in sources)
        {
            var state = new BreakpointSource(Path.GetFullPath(source.Path)) { Desired = new(source.Lines) };
            state.Rows = state.Desired.Select(line => new ExternalBreakpoint(line)).ToArray();
            breakpointSources[state.Path] = state;
        }
        Changed?.Invoke();
        return breakpointSources.Values.Select(source => new DapSourceBreakpoints(source.Path, source.Desired.ToArray())).ToArray();
    }

    private static string? SourcePath(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !parsed.IsFile) return null;
        try { return Path.GetFullPath(parsed.LocalPath); }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    public IReadOnlyList<ExternalBreakpoint> GetBreakpoints(string uri) =>
        SourcePath(uri) is { } path && breakpointSources.TryGetValue(path, out var source) ? source.Rows : [];

    public bool ToggleBreakpoint(string uri, int line)
    {
        if (!CanEditBreakpoints || line < 1 || SourcePath(uri) is not { } path) return false;
        if (!breakpointSources.TryGetValue(path, out var source))
            breakpointSources[path] = source = new(path);
        if (!source.Desired.Remove(line)) source.Desired.Add(line);
        QueueBreakpoints(source);
        return true;
    }

    public void ClearAllBreakpoints()
    {
        if (!CanEditBreakpoints) return;
        foreach (var source in breakpointSources.Values.ToArray())
        {
            if (source.Desired.Count == 0 && source.Rows.Count == 0) continue;
            source.Desired.Clear();
            QueueBreakpoints(source);
        }
    }

    private void QueueBreakpoints(BreakpointSource source)
    {
        ++source.Version;
        // Keep removed rows pending until the adapter confirms removal. On refusal they remain visible
        // as unverified, so a rejected Clear All cannot falsely claim that the backend has no breakpoints.
        source.Rows = source.Rows.Select(row => row.RequestedLine).Union(source.Desired).Order()
            .Select(line => new ExternalBreakpoint(line)).ToArray();
        Changed?.Invoke();
        if (!source.Sending && session is { } current) _ = SendBreakpointsAsync(current, source);
    }

    private async Task SendBreakpointsAsync(DapSession current, BreakpointSource source)
    {
        source.Sending = true;
        try
        {
            while (session == current && CanEditBreakpoints)
            {
                var version = source.Version;
                var lines = source.Desired.ToArray();
                try
                {
                    var reply = await current.SetBreakpointsAsync(source.Path, lines);
                    if (session != current || !CanEditBreakpoints) return;
                    if (source.Version != version) continue;
                    ApplyBreakpointReply(source, lines, reply);
                }
                catch (Exception exception)
                {
                    if (session != current || !CanEditBreakpoints) return;
                    if (source.Version != version) continue;
                    source.Rows = source.Rows.Select(row => row with { Pending = false, Verified = false, Message = exception.Message }).ToArray();
                    source.Desired = new(source.Rows.Select(row => row.RequestedLine));
                    Failed?.Invoke(exception.Message);
                }
                Changed?.Invoke();
                return;
            }
        }
        finally { source.Sending = false; }
    }

    private static int? Number(JsonElement value, string property) =>
        value.TryGetProperty(property, out var number) && number.ValueKind == JsonValueKind.Number && number.TryGetInt32(out var result) ? result : null;

    private static ExternalBreakpoint ReadBreakpoint(int requested, JsonElement value) => new(requested,
        Number(value, "line"), Number(value, "id"),
        value.TryGetProperty("verified", out var verified) && verified.ValueKind == JsonValueKind.True,
        Pending: false, Message: value.TryGetProperty("message", out var message) ? message.GetString() ?? "" : "");

    private static void ApplyBreakpointReply(BreakpointSource source, IReadOnlyList<int> lines, DapResponse reply)
    {
        var breakpoints = reply.Body!.Value.GetProperty("breakpoints").EnumerateArray().ToArray();
        source.Rows = lines.Select((line, index) => index < breakpoints.Length
            ? ReadBreakpoint(line, breakpoints[index]) : new ExternalBreakpoint(line, Pending: false)).ToArray();
    }

    private void ApplyBreakpointEvent(JsonElement body)
    {
        if (!body.TryGetProperty("breakpoint", out var value) || value.ValueKind != JsonValueKind.Object ||
            Number(value, "id") is not { } id) return;
        var removed = body.TryGetProperty("reason", out var reason) && reason.GetString() == "removed";
        foreach (var source in breakpointSources.Values)
        {
            source.Rows = source.Rows.Select(row => row.Id == id && !row.Pending
                ? ReadBreakpoint(row.RequestedLine, value) with
                {
                    Line = Number(value, "line") ?? row.Line,
                    Verified = !removed && value.TryGetProperty("verified", out var verified) && verified.ValueKind == JsonValueKind.True,
                } : row).ToArray();
        }
    }
}
