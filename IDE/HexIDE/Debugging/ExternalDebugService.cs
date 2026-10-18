using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using HexIDE.Conversations;
using HexIDE.Dap;
using HexIDE.Lsp;

namespace HexIDE.Debugging;

public sealed record ExternalVariable(string Name, string Value, string Type, int Reference);

/// <summary>One external run. Public state and events belong to the UI thread; wire reads never block it.</summary>
public sealed class ExternalDebugService(ConversationLog capture)
{
    private DapSession? session;
    private CancellationTokenSource? cancellation;
    private Task? stopping;
    private long inspectionVersion;
    public bool IsActive { get; private set; }
    public bool IsBusy { get; private set; }
    public DapSessionState State { get; private set; } = DapSessionState.Ended;
    public DapStop? Stop { get; private set; }
    public int? ThreadId { get; private set; }
    public IReadOnlyList<JsonElement> Frames { get; private set; } = [];
    public JsonElement? SelectedFrame { get; private set; }
    public IReadOnlyList<ExternalVariable> Variables { get; private set; } = [];
    public LanguageServerConnection? Connection { get; private set; }
    public event Action? Changed;
    public event Action<string>? Output;
    public event Action<string>? Failed;

    public bool IsCurrent(DapStop stop) => session?.IsCurrent(stop) == true;

    public (string Uri, int Line)? CurrentLocation => Stop is not null && Frames.Count > 0 ? Location(Frames[0]) : null;
    public (string Uri, int Line)? SelectedLocation => SelectedFrame is { } frame ? Location(frame) : null;

    private static (string Uri, int Line)? Location(JsonElement frame)
    {
        if (!frame.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.Object ||
            !source.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.String ||
            !frame.TryGetProperty("line", out var line) || line.ValueKind != JsonValueKind.Number ||
            !line.TryGetInt32(out var number) || number < 1)
            return null;
        try
        {
            var file = path.GetString();
            return file is not null && System.IO.Path.IsPathFullyQualified(file)
                ? (new Uri(System.IO.Path.GetFullPath(file!)).AbsoluteUri, number) : null;
        }
        catch (ArgumentException) { return null; }
        catch (UriFormatException) { return null; }
    }

    private void ClearInspection()
    {
        ++inspectionVersion;
        SelectedFrame = null;
        Frames = [];
        Variables = [];
    }

    public async Task StartAsync(string configurationFile)
    {
        if (IsActive) return;
        IsActive = true;
        State = DapSessionState.Starting;
        stopping = null;
        Connection = null;
        cancellation = new();
        Changed?.Invoke();
        Task? reader = null;
        try
        {
            var configuration = await DapLaunchConfiguration.ReadAsync(configurationFile, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var id = "dap-" + Guid.NewGuid().ToString("N");
            Connection = new(id, configuration.AdapterId, LanguageConnectionKind.DebugAdapter, LanguageConnectionState.Starting,
                [], "", null, Endpoint: configuration.Executable, StateSince: DateTimeOffset.UtcNow);
            session = DapSession.Start(configuration.ProcessOptions(new(capture, id)));
            var current = session;
            reader = ReadAsync(current);
            var replies = await current.LaunchAsync(configuration.AdapterId, configuration.Launch, configuration.Breakpoints, cancellation.Token);
            foreach (var reply in replies)
                Output?.Invoke($"{reply.Command}: {reply.Body}\n");
            Connection = Connection with { Capabilities = current.Capabilities };
            if (current.Snapshot.State is DapSessionState.Running or DapSessionState.Stopped)
            {
                var threads = await current.ThreadsAsync(cancellation.Token);
                if (threads.Body!.Value.GetProperty("threads").GetArrayLength() > 0)
                    ThreadId ??= threads.Body.Value.GetProperty("threads")[0].GetProperty("id").GetInt32();
            }
            Changed?.Invoke();
            await current.Completion;
            await reader;
            if (current.Snapshot.Failure is { } failure) throw failure;
            if (current.ProcessCompletion is { } completion)
            {
                var result = await completion;
                if (result.ExitCode is not (null or 0) || result.ForcedTermination || result.CleanupFailure is not null)
                    Failed?.Invoke($"Debug adapter exit: {result.ExitCode}; forced: {result.ForcedTermination}; {result.CleanupFailure}\n{result.StandardErrorTail}");
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            State = DapSessionState.Faulted;
            Failed?.Invoke(exception.Message);
        }
        finally
        {
            if (session is { } owned) await owned.DisposeAsync();
            if (reader is not null) { try { await reader; } catch (Exception exception) { Failed?.Invoke(exception.Message); } }
            session = null;
            cancellation.Dispose();
            cancellation = null;
            IsActive = IsBusy = false;
            Stop = null;
            ThreadId = null;
            ClearInspection();
            if (State != DapSessionState.Faulted) State = DapSessionState.Ended;
            UpdateConnection();
            Changed?.Invoke();
        }
    }

    private async Task ReadAsync(DapSession current)
    {
        try { await ReadUpdatesAsync(current); }
        catch
        {
            await current.DisposeAsync();
            throw;
        }
    }

    private async Task ReadUpdatesAsync(DapSession current)
    {
        await foreach (var update in current.Updates.ReadAllAsync())
        {
            // Capture only immutable protocol data across this dispatch.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (session != current) return;
                State = update.Snapshot.State;
                if (Stop != update.Snapshot.Stop) ClearInspection();
                Stop = update.Snapshot.Stop;
                if (Stop?.ThreadId is { } thread) ThreadId = thread;
                if (update.Event?.Event == "output" && update.Event.Body is { } body && body.TryGetProperty("output", out var output))
                    Output?.Invoke(output.GetString() ?? "");
                if (update.Snapshot.Stop is null) ClearInspection();
                UpdateConnection();
                Changed?.Invoke();
                if (update.Event?.Event == "stopped" && Stop is { } stop) _ = InspectAsync(current, stop);
            });
            if (update.Event?.Event == "terminated") await StopAsync();
        }
    }

    private async Task InspectAsync(DapSession current, DapStop stop)
    {
        try
        {
            var thread = stop.ThreadId;
            if (thread is null)
                thread = (await current.ThreadsAsync(stop)).Body!.Value.GetProperty("threads")[0].GetProperty("id").GetInt32();
            var stack = await current.StackTraceAsync(stop, thread.Value);
            var frames = stack.Body!.Value.GetProperty("stackFrames").EnumerateArray().ToArray();
            if (session != current || !current.IsCurrent(stop)) return;
            ThreadId = thread;
            Frames = frames;
            if (frames.Length > 0) await SelectFrameAsync(stop, frames[0].GetProperty("id").GetInt32());
            else Changed?.Invoke();
        }
        catch (Exception exception)
        {
            if (session == current && current.IsCurrent(stop)) Failed?.Invoke(exception.Message);
        }
    }

    public async Task SelectFrameAsync(DapStop stop, int frameId)
    {
        if (session is not { } current || !current.IsCurrent(stop) || Stop != stop) return;
        var frame = Frames.FirstOrDefault(item => item.GetProperty("id").GetInt32() == frameId);
        if (frame.ValueKind != JsonValueKind.Object ||
            SelectedFrame is { } selected && selected.GetProperty("id").GetInt32() == frameId) return;
        var version = ++inspectionVersion;
        SelectedFrame = frame;
        Variables = [];
        Changed?.Invoke();
        bool IsSelected() => session == current && current.IsCurrent(stop) && inspectionVersion == version;
        try
        {
            var variables = new List<ExternalVariable>();
            var scopes = await current.ScopesAsync(stop, frameId);
            if (!IsSelected()) return;
            foreach (var scope in scopes.Body!.Value.GetProperty("scopes").EnumerateArray())
            {
                if (scope.TryGetProperty("expensive", out var expensive) && expensive.ValueKind == JsonValueKind.True) continue;
                variables.AddRange(await LoadVariablesAsync(stop, scope.GetProperty("variablesReference").GetInt32()));
                if (!IsSelected()) return;
            }
            Variables = variables;
            Changed?.Invoke();
        }
        catch (Exception exception)
        {
            if (IsSelected()) Failed?.Invoke(exception.Message);
        }
    }

    public async Task<IReadOnlyList<ExternalVariable>> LoadVariablesAsync(DapStop stop, int reference)
    {
        var current = session ?? throw new InvalidOperationException("No external debug session.");
        var reply = await current.VariablesAsync(stop, reference);
        return reply.Body!.Value.GetProperty("variables").EnumerateArray().Select(variable => new ExternalVariable(
            variable.GetProperty("name").GetString() ?? "", variable.GetProperty("value").GetString() ?? "",
            variable.TryGetProperty("type", out var type) ? type.GetString() ?? "" : "",
            variable.GetProperty("variablesReference").GetInt32())).ToArray();
    }

    public async Task CommandAsync(string command)
    {
        if (session is not { } current || IsBusy || ThreadId is not { } thread) return;
        IsBusy = true;
        Changed?.Invoke();
        try
        {
            if (command == "pause") await current.PauseAsync(thread);
            else if (Stop is { } stop) await current.ResumeAsync(stop, command, thread);
        }
        catch (Exception exception) { Failed?.Invoke(exception.Message); }
        finally { if (session == current) { IsBusy = false; Changed?.Invoke(); } }
    }

    public Task StopAsync() => stopping ??= StopCoreAsync();

    private async Task StopCoreAsync()
    {
        cancellation?.Cancel();
        if (session is not { } current) return;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await current.DisconnectAsync(deadline.Token);
        }
        catch (Exception exception) { Output?.Invoke(exception.Message + "\n"); }
        finally { await current.DisposeAsync(); }
    }

    private void UpdateConnection()
    {
        if (Connection is null) return;
        var state = State switch
        {
            DapSessionState.Starting => LanguageConnectionState.Starting,
            DapSessionState.Faulted => LanguageConnectionState.Failed,
            DapSessionState.Ended => LanguageConnectionState.Stopped,
            _ => LanguageConnectionState.Running,
        };
        Connection = Connection with { State = state, StateSince = Connection.State == state ? Connection.StateSince : DateTimeOffset.UtcNow };
    }
}
