// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

using System.Diagnostics;
using System.Text.Json;
using EmmyLua.LanguageServer.Framework.Protocol.JsonRpc;
using EmmyLua.LanguageServer.Framework.Server.Scheduler;
using Serilog;

namespace HexIDE.VbLspServer;

// Completion reads only published declaration snapshots; everything else stays sequential.
internal sealed class TimingScheduler : IScheduler, IDisposable
{
    private readonly SingleThreadScheduler worker = new();

    public void Schedule(Func<Message, Task> action, Message message)
    {
        var queued = Stopwatch.GetTimestamp();
        if (message is RequestMessage { Method: "textDocument/completion" })
            _ = ExecuteAsync(action, message, queued);
        else
            worker.Schedule(m => ExecuteAsync(action, m, queued), message);
    }

    private static async Task ExecuteAsync(Func<Message, Task> action, Message message, long queued)
    {
        var method = message switch
        {
            RequestMessage r => r.Method,
            NotificationMessage n => n.Method,
            _ => message.GetType().Name,
        };
        var started = Stopwatch.GetTimestamp();
        using var methodContext = Serilog.Context.LogContext.PushProperty("Method", method);
        using var idContext = Serilog.Context.LogContext.PushProperty("RequestId",
            message is RequestMessage request ? request.Id.StringValue ?? (object)request.Id.IntValue : null);
        var parameters = message switch
        {
            RequestMessage r => r.Params,
            NotificationMessage n => n.Params,
            _ => null,
        };
        var document = parameters?.RootElement is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty("textDocument", out var td)
            ? td : default;
        using var versionContext = Serilog.Context.LogContext.PushProperty("DocumentVersion",
            document.ValueKind == JsonValueKind.Object && document.TryGetProperty("version", out var version)
                && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number)
                ? number : (int?)null);
        Log.Debug("{Method} queue wait {QueueMs} ms", method,
            Stopwatch.GetElapsedTime(queued).TotalMilliseconds);
        try { await action(message); }
        catch (Exception ex) { Log.Error(ex, "{Method} handler failed", method); }
        finally
        {
            Log.Debug("{Method} elapsed {ElapsedMs} ms", method,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    public void Dispose() => worker.Dispose();
}
