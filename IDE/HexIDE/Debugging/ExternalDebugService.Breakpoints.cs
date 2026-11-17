using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
        public Dictionary<int, string> Conditions { get; } = [];
        public IReadOnlyList<ExternalBreakpoint> Rows { get; set; } = [];
        public long Version { get; set; }
        public bool Sending { get; set; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public Task PendingSend { get; set; } = Task.CompletedTask;
    }

    private readonly Dictionary<string, BreakpointSource> breakpointSources = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private bool breakpointsReady;
    public bool CanEditBreakpoints => IsActive && breakpointsReady && session?.Snapshot.State is DapSessionState.Running or DapSessionState.Stopped;

    private void ResetBreakpoints()
    {
        breakpointsReady = false;
        conditionsNoticeShown = false;
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
        if (breakpoints is not null)
            foreach (var (document, lines) in breakpoints.All())
            {
                if (document.AbsolutePath is not { } path) continue;
                path = Path.GetFullPath(path);
                if (!breakpointSources.TryGetValue(path, out var state))
                    breakpointSources[path] = state = new(path);
                foreach (var line in lines)
                {
                    state.Desired.Add(line);
                    if (breakpoints.GetCondition(document, line) is { } condition) state.Conditions[line] = condition;
                }
                state.Rows = state.Desired.Select(line => new ExternalBreakpoint(line)).ToArray();
            }
        Changed?.Invoke();
        return breakpointSources.Values.Select(source => new DapSourceBreakpoints(source.Path, source.Desired.ToArray())
            { Conditions = new Dictionary<int, string>(source.Conditions) }).ToArray();
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
        else source.Conditions.Remove(line);
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
            source.Conditions.Clear();
            QueueBreakpoints(source);
        }
    }

    /// <summary>Replaces the editor's external breakpoint set, preserving omitted conditions.</summary>
    public async Task ReplaceBreakpointsAsync(string uri, IReadOnlyList<int> lines, IReadOnlyDictionary<int, string>? conditions)
    {
        if (!CanEditBreakpoints || SourcePath(uri) is not { } path)
            throw new InvalidOperationException("External breakpoints are not editable; wait for the adapter to finish starting and use a saved document.");
        if (!breakpointSources.TryGetValue(path, out var source))
            breakpointSources[path] = source = new(path);
        source.Desired = new(lines);
        foreach (var line in source.Conditions.Keys.Except(lines).ToArray()) source.Conditions.Remove(line);
        if (conditions is not null)
            foreach (var (line, condition) in conditions)
                if (string.IsNullOrWhiteSpace(condition)) source.Conditions.Remove(line);
                else source.Conditions[line] = condition;
        NoticeIgnoredConditions();
        QueueBreakpoints(source);
        await source.PendingSend;
    }

    public async Task ClearAllBreakpointsAsync()
    {
        if (!CanEditBreakpoints) throw new InvalidOperationException("External breakpoints are not editable; wait for the adapter to finish starting.");
        ClearAllBreakpoints();
        await Task.WhenAll(breakpointSources.Values.Select(source => source.PendingSend));
    }

    public bool SupportsConditionalBreakpoints => session?.Supports("supportsConditionalBreakpoints") == true;

    private bool conditionsNoticeShown;

    private void NoticeIgnoredConditions()
    {
        if (conditionsNoticeShown || session?.Supports("supportsConditionalBreakpoints") != false ||
            !breakpointSources.Values.Any(source => source.Conditions.Count > 0)) return;
        conditionsNoticeShown = true;
        Output?.Invoke((localization ?? new HexIDE.Localization.LocalizationService())
            .GetString("Str.Debug.Conditions.AdapterIgnored") + "\n");
    }

    public string? GetCondition(string uri, int line) =>
        SourcePath(uri) is { } path && breakpointSources.TryGetValue(path, out var source) &&
        source.Conditions.TryGetValue(line, out var condition) ? condition : null;

    public void SetCondition(string uri, int line, string? condition)
    {
        if (!CanEditBreakpoints || SourcePath(uri) is not { } path ||
            !breakpointSources.TryGetValue(path, out var source) || !source.Desired.Contains(line)) return;
        if (string.IsNullOrWhiteSpace(condition)) source.Conditions.Remove(line);
        else source.Conditions[line] = condition;
        NoticeIgnoredConditions();
        QueueBreakpoints(source);
    }

    private void QueueBreakpoints(BreakpointSource source)
    {
        ++source.Version;
        // Keep removed rows pending until the adapter confirms removal. On refusal they remain visible
        // as unverified, so a rejected Clear All cannot falsely claim that the backend has no breakpoints.
        source.Rows = source.Rows.Select(row => row.RequestedLine).Union(source.Desired).Order()
            .Select(line => new ExternalBreakpoint(line)).ToArray();
        Changed?.Invoke();
        if (!source.Sending && session is { } current) source.PendingSend = SendBreakpointsAsync(current, source);
    }

    private async Task SendBreakpointsAsync(DapSession current, BreakpointSource source)
    {
        source.Sending = true;
        try
        {
            while (session == current && CanEditBreakpoints)
            {
                var version = source.Version;
                await source.Gate.WaitAsync();
                var lines = source.Desired.ToArray();
                try
                {
                    var reply = await current.SetBreakpointsAsync(source.Path, lines, conditions: new Dictionary<int, string>(source.Conditions), temporaryLine: CursorLine(source));
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
                finally { source.Gate.Release(); }
                Changed?.Invoke();
                return;
            }
        }
        finally { source.Sending = false; }
    }

    private sealed class CursorBreakpoint(DapSession session, BreakpointSource source, int line)
    {
        public DapSession Session { get; } = session;
        public BreakpointSource Source { get; } = source;
        public int Line { get; } = line;
        public Task? Cleanup { get; set; }
    }

    private CursorBreakpoint? cursorBreakpoint;
    public bool CanRunToCursor => CanEditBreakpoints && !IsBusy && cursorBreakpoint is null &&
        Stop is { } stop && IsCurrent(stop) && ThreadId is not null;

    private int? CursorLine(BreakpointSource source) => cursorBreakpoint is { } cursor &&
        cursor.Source == source && cursor.Cleanup is null ? cursor.Line : null;

    public async Task RunToCursorAsync(string uri, int line)
    {
        if (!CanRunToCursor || session is not { } current || Stop is not { } stop ||
            ThreadId is not { } thread || line < 1 || SourcePath(uri) is not { } path) return;
        if (!breakpointSources.TryGetValue(path, out var source))
            breakpointSources[path] = source = new(path);
        var cursor = new CursorBreakpoint(current, source, line);
        cursorBreakpoint = cursor;
        IsBusy = true;
        Changed?.Invoke();
        try
        {
            await source.Gate.WaitAsync();
            try
            {
                if (!current.IsCurrent(stop) || cursor.Cleanup is not null)
                    throw new InvalidOperationException("The external session moved before Run To Cursor could start.");
                var lines = source.Desired.ToArray();
                var reply = await current.SetBreakpointsAsync(path, lines, conditions: new Dictionary<int, string>(source.Conditions), temporaryLine: line);
                var values = reply.Body!.Value.GetProperty("breakpoints");
                var index = lines.Length;
                var breakpoint = index < values.GetArrayLength() ? ReadBreakpoint(line, values[index]) : new ExternalBreakpoint(line, Pending: false);
                if (!breakpoint.Verified || breakpoint.Line is { } actual && actual != line)
                    throw new InvalidOperationException($"Run To Cursor refused at {path} line {line}: " +
                        (breakpoint.Message.Length > 0 ? breakpoint.Message + ". " : "") +
                        (breakpoint.Line is { } moved && moved != line ? $"The adapter moved the breakpoint to line {moved}." : "The adapter did not verify the breakpoint."));
            }
            finally { source.Gate.Release(); }
            await current.ResumeAsync(stop, "continue", thread);
        }
        catch (Exception exception)
        {
            await RemoveCursorBreakpointAsync(current);
            Failed?.Invoke(exception.Message);
        }
        finally
        {
            if (session == current) { IsBusy = false; Changed?.Invoke(); }
        }
    }

    /// <summary>True from a Run To Cursor's start until its temporary line has left the adapter's set.</summary>
    public bool RunToCursorPending => cursorBreakpoint is not null;

    private Task RemoveCursorBreakpointAsync(DapSession current, bool restore = true)
    {
        if (cursorBreakpoint is not { } cursor || cursor.Session != current) return Task.CompletedTask;
        if (cursor.Cleanup is { } started) return started;
        // A session past `terminated` took its breakpoints with it, and its adapter need not answer any more.
        if (!restore || current.Snapshot.State is not (DapSessionState.Running or DapSessionState.Stopped))
        {
            cursor.Cleanup = Task.CompletedTask;
            cursorBreakpoint = null;
            Changed?.Invoke();
            return cursor.Cleanup;
        }
        return cursor.Cleanup = RestoreAsync();

        async Task RestoreAsync()
        {
            // Yield before taking the gate so Cleanup is set before any queued editor update reads the set.
            await Task.Yield();
            await cursor.Source.Gate.WaitAsync();
            try
            {
                var lines = cursor.Source.Desired.ToArray();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var reply = await current.SetBreakpointsAsync(cursor.Source.Path, lines, deadline.Token,
                    new Dictionary<int, string>(cursor.Source.Conditions));
                if (session == current) ApplyBreakpointReply(cursor.Source, lines, reply);
            }
            catch (Exception exception)
            {
                // Keep the session: ending someone's debug session over a refused clean-up costs more than one extra
                // stop, and the message says where that stop could come from.
                Failed?.Invoke($"Run To Cursor could not remove its temporary breakpoint at {cursor.Source.Path} line " +
                    $"{cursor.Line}, so the adapter may stop there again: {exception.Message}");
            }
            finally
            {
                cursor.Source.Gate.Release();
                if (cursorBreakpoint == cursor) cursorBreakpoint = null;
                Changed?.Invoke();
            }
        }
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
