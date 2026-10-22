using System.Text.Json;

namespace HexIDE.Dap;

/// <summary>A response's complete JSON, including extension fields and structured adapter errors.</summary>
public sealed record DapResponse(int Sequence, int RequestSequence, string Command, bool Success, JsonElement Message)
{
    public JsonElement? Body => Message.TryGetProperty("body", out var body) ? body : null;
}

/// <summary>An adapter event in wire order. JSON elements own their data and survive the next read.</summary>
public sealed record DapEvent(int Sequence, string Event, JsonElement Message)
{
    public JsonElement? Body => Message.TryGetProperty("body", out var body) ? body : null;
}

/// <summary>A valid unsuccessful response; it fails that request, not the connection.</summary>
public sealed class DapRequestException(DapResponse response) : Exception(
    response.Message.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
        ? message.GetString() : $"Debug adapter refused '{response.Command}'.")
{
    public DapResponse Response { get; } = response;
}

/// <summary>Resource limits for one connection. No automatic retries: a timed-out command may have executed.</summary>
public sealed record DapConnectionOptions
{
    public int MaximumMessageBytes { get; init; } = 16 * 1024 * 1024;
    public int MaximumHeaderBytes { get; init; } = 8192;
    public int MaximumPendingRequests { get; init; } = 256;
    public int EventCapacity { get; init; } = 256;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumMessageBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumHeaderBytes, 4);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumPendingRequests);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(EventCapacity);
        if (RequestTimeout <= TimeSpan.Zero || RequestTimeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(RequestTimeout));
    }
}
