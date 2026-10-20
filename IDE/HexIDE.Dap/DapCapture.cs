using System.Globalization;
using System.Text.Json;
using HexIDE.Conversations;

namespace HexIDE.Dap;

/// <summary>
/// Optional tap into the same bounded log used by the protocol inspector. Use a distinct connection id
/// for each session. The caller owns the log; capturing never waits for its consumer or throws into DAP.
/// </summary>
public sealed class DapCapture
{
    private readonly ConversationLog _log;
    public string ConnectionId { get; }

    public DapCapture(ConversationLog log, string connectionId)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        (_log, ConnectionId) = (log, connectionId);
    }

    internal void Frame(byte[] bytes, bool sent, JsonElement? parsed = null)
    {
        try
        {
            using var document = parsed is null ? JsonDocument.Parse(bytes) : null;
            var message = parsed ?? document!.RootElement;
            var type = Text(message, "type");
            var kind = type switch
            {
                "request" => ConversationEntryKind.Request,
                "event" => ConversationEntryKind.Notification,
                "response" when message.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False
                    => ConversationEntryKind.ErrorResponse,
                "response" => ConversationEntryKind.Response,
                _ => ConversationEntryKind.Note,
            };
            var idField = type == "response" ? "request_seq" : "seq";
            var id = kind != ConversationEntryKind.Notification && message.ValueKind == JsonValueKind.Object &&
                message.TryGetProperty(idField, out var sequence) && sequence.ValueKind == JsonValueKind.Number && sequence.TryGetInt32(out var number)
                ? number.ToString(CultureInfo.InvariantCulture) : null;
            _log.Record(ConnectionId, sent ? ConversationDirection.Sent : ConversationDirection.Received,
                kind, Text(message, type == "event" ? "event" : "command"), id, bytes.Length,
                _log.ShouldKeepBody(ConnectionId) ? bytes : null,
                sent ? "DAP write attempt; transport failure may leave a partial frame." : null);
        }
        catch (Exception) { /* Diagnostics must never break or delay protocol operation. */ }
    }

    internal void InvalidFrame(byte[] bytes, string detail = "Invalid DAP JSON body.")
    {
        try
        {
            _log.Record(ConnectionId, ConversationDirection.Received, ConversationEntryKind.Note, null, null,
                bytes.Length, _log.ShouldKeepBody(ConnectionId) ? bytes : null, detail, fault: true);
        }
        catch (Exception) { }
    }

    internal void Note(string text, bool standardError = false, bool fault = false)
    {
        try
        {
            _log.Record(ConnectionId, standardError ? ConversationDirection.Received : ConversationDirection.Local,
                standardError ? ConversationEntryKind.StandardError : ConversationEntryKind.Lifecycle,
                null, null, 0, detail: text, fault: fault);
        }
        catch (Exception) { }
    }

    internal void EndRequest(int sequence, bool cancelled)
    {
        if (sequence == 0) return;
        try
        {
            _log.EndRequests(ConnectionId, ConversationDirection.Sent, sequence.ToString(CultureInfo.InvariantCulture),
                cancelled ? ConversationOutcome.Cancelled : ConversationOutcome.Abandoned);
        }
        catch (Exception) { }
    }

    internal void Closed(Exception? failure)
    {
        Note(failure is null ? "DAP connection disposed." : $"DAP connection closed: {failure.GetType().Name}: {failure.Message}");
        try { _log.EndRequests(ConnectionId); }
        catch (Exception) { }
    }

    private static string? Text(JsonElement message, string field) =>
        message.ValueKind == JsonValueKind.Object && message.TryGetProperty(field, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
