namespace HexIDE.Conversations;

/// <summary>Session output kept independently of protocol capture and its arming.</summary>
public sealed class ServerOutputLog
{
    public const int MaxCharacters = 64 * 1024;
    private readonly Dictionary<string, string> _text = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public event EventHandler<string>? Changed;

    public string Text(string connectionId)
    {
        lock (_gate) return _text.GetValueOrDefault(connectionId, "");
    }

    public void Append(string connectionId, string source, string message)
    {
        if (message.Length > MaxCharacters) message = message[^MaxCharacters..];
        lock (_gate)
        {
            var text = _text.GetValueOrDefault(connectionId, "")
                + $"[{DateTimeOffset.Now:HH:mm:ss}] [{source}] {message}" + Environment.NewLine;
            _text[connectionId] = text.Length > MaxCharacters ? "…" + text[^(MaxCharacters - 1)..] : text;
        }
        // Output consumers must never interrupt an RPC or transport callback.
        try { Changed?.Invoke(this, connectionId); }
        catch { }
    }
}
