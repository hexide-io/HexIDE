using System.Collections.Concurrent;
using System.Text.Json;
using HexIDE.Lsp.Messages;
using HexIDE.Conversations;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace HexIDE.Lsp;

// Send registered params with scalar IDs; RequestId never enters the generated serializer.
// RequestId's own converter is internal to StreamJsonRpc and cannot be used by our generated context.
internal sealed class LspCancellationStrategy(JsonRpc rpc, ILogger logger) : ICancellationStrategy
{
    [JsonRpcIgnore]
    public void CancelOutboundRequest(RequestId requestId)
    {
        RequestCancellation.Current.Value?.Record(requestId);
        _ = SendCancellationAsync(requestId);
    }

    private async Task SendCancellationAsync(RequestId requestId)
    {
        try
        {
            if (requestId.Number is { } number)
                await rpc.NotifyWithParameterObjectAsync("$/cancelRequest", new CancelNumericRequestParams(number));
            else if (requestId.String is { } text)
                await rpc.NotifyWithParameterObjectAsync("$/cancelRequest", new CancelStringRequestParams(text));
            else
                throw new ArgumentException("Cancellation requires a numeric or string request ID.", nameof(requestId));
        }
        catch (Exception ex)
        {
            // The strategy callback is synchronous; observe send failures rather than orphaning a faulted task.
            logger.LogDebug(ex, "Could not send language-server cancellation for {RequestId}", requestId);
        }
    }

    private readonly ConcurrentDictionary<RequestId, CancellationTokenSource> _incoming = new();

    [JsonRpcIgnore]
    public void OutboundRequestEnded(RequestId requestId) { }
    [JsonRpcIgnore]
    public void IncomingRequestStarted(RequestId requestId, CancellationTokenSource cancellationTokenSource) =>
        _incoming[requestId] = cancellationTokenSource;
    [JsonRpcIgnore]
    public void IncomingRequestEnded(RequestId requestId) => _incoming.TryRemove(requestId, out _);

    [JsonRpcMethod("$/cancelRequest", UseSingleObjectParameterDeserialization = true)]
    public void CancelIncoming(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("id", out var id)) return;
        RequestId requestId;
        if (id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var number)) requestId = new RequestId(number);
        else if (id.ValueKind == JsonValueKind.String) requestId = new RequestId(id.GetString()!);
        else return;
        if (_incoming.TryGetValue(requestId, out var source))
        {
            try { source.Cancel(); }
            catch (ObjectDisposedException) { /* The request ended concurrently; JsonRpc owns the source. */ }
        }
    }
}

// StreamJsonRpc captures the invocation's execution context for its token callback. The mutable
// scope tells that callback whether this request was superseded or reached its local deadline.
internal sealed class RequestCancellation(ConversationLog? log, string? connectionId)
{
    internal static readonly AsyncLocal<RequestCancellation?> Current = new();
    internal ConversationOutcome Outcome = ConversationOutcome.Cancelled;
    private RequestId? _id;

    internal void Observe(RequestId id) => _id = id;

    internal void DropAnswer(Exception exception)
    {
        if (log is null || connectionId is null || _id is not { } id) return;
        var correlation = id.Number is { } number
            ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : id.String;
        try { log.DropAnswer(connectionId, correlation!, $"answer dropped: {exception.Message}"); }
        catch (Exception) { /* recording must not replace the original failure */ }
    }

    internal void Record(RequestId id)
    {
        if (log is null || connectionId is null) return;
        var correlation = id.Number is { } number
            ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : id.String;
        log.EndRequests(connectionId, ConversationDirection.Sent, correlation, Outcome);
    }
}
