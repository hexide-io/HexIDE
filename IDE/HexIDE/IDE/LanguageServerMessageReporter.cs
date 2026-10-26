using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

namespace HexIDE.IDE;

/// <summary>
/// Puts a language server's own messages where a user will see them.
///
/// <para>
/// A server has exactly one channel for talking about itself — "I cannot find your toolchain", "I am
/// falling back to a degraded mode" — and HexIDE used to discard it. Since a user can attach any server by
/// editing <c>lsp-servers.json</c>, that meant a misconfigured server started, connected, produced no
/// diagnostics, and explained nothing. Indistinguishable from a broken IDE (hexide-io/HexIDE#289).
/// </para>
///
/// <para>
/// The status bar rather than a dialog, deliberately. A server in a bad state can be voluble, and a modal
/// per message would trade a silent failure for an unusable one — with no repeat suppression, a server
/// looping on a broken config would make the IDE impossible to dismiss. The status bar is glanceable,
/// costs nothing when it is wrong, and every message is written to the log as well, which is where someone
/// looks once they notice something is missing. If a case turns up that genuinely warrants interrupting
/// the user, it should be argued for on its own evidence rather than assumed here.
/// </para>
/// </summary>
public sealed class LanguageServerMessageReporter : IDisposable
{
    private readonly ILspClient _client;
    private readonly IStatusBarService _statusBar;
    private readonly ILocalizationService _localization;
    private readonly Dictionary<(object? Server, string Token), WorkDoneProgressValue> _progress = [];
    private readonly object _progressGate = new();
    private string? _displayedProgress;

    /// <summary>
    /// How long each severity lingers. An error is worth reading twice; running commentary is not worth
    /// displacing whatever the status bar was saying for long.
    /// </summary>
    private static TimeSpan DwellFor(LspMessageType type) => type switch
    {
        LspMessageType.Error => TimeSpan.FromSeconds(15),
        LspMessageType.Warning => TimeSpan.FromSeconds(10),
        _ => TimeSpan.FromSeconds(5),
    };

    public LanguageServerMessageReporter(ILspClient client, IStatusBarService statusBar, ILocalizationService localization)
    {
        _client = client;
        _statusBar = statusBar;
        _localization = localization;
        _client.ProgressReceived += OnProgress;
        _client.MessageShown += OnMessageShown;
    }

    private void OnMessageShown(object? sender, ShowMessageParams p)
    {
        // A server's text is arbitrary and arrives from a process HexIDE did not write. Collapsing
        // whitespace keeps a multi-line message from pushing the status bar around, and the length cap
        // stops a runaway one from filling it — neither is sanitisation, because nothing here interprets
        // the string; it is only ever displayed.
        var text = Flatten(p.Message);
        if (text.Length == 0) return;

        _statusBar.SetTemporaryMessage(Prefixed(p.Type, sender is VBLspClient client ? $"[{client.ConnectionId}] {text}" : text), DwellFor(p.Type));
    }

    private void OnProgress(object? sender, ProgressParams p)
    {
        lock (_progressGate)
        {
            var key = (sender, p.Token.ValueKind + ":" + p.Token.ToString());
            if (p.Value.Kind == "end")
            {
                if (!_progress.Remove(key)) return;
                // Do not erase a later status message from another IDE operation.
                if (_statusBar.Message != _displayedProgress) return;
                if (_progress.Count == 0)
                {
                    _statusBar.ClearMessage();
                    _displayedProgress = null;
                    return;
                }
            }
            else if (p.Value.Kind == "begin")
                _progress[key] = p.Value;
            else if (p.Value.Kind == "report" && _progress.TryGetValue(key, out var previous))
                _progress[key] = p.Value with
                {
                    Title = previous.Title,
                    Message = p.Value.Message ?? previous.Message,
                    Percentage = p.Value.Percentage ?? previous.Percentage,
                };
            else return;

            var value = _progress.TryGetValue(key, out var current) ? current : _progress.Last().Value;
            var text = Flatten((value.Title ?? "") + " " + (value.Message ?? ""));
            if (value.Percentage is { } percentage) text += $" ({percentage}%)";
            _displayedProgress = string.Format(_localization.GetString("Str.StatusBar.LanguageServerProgress"), text);
            _statusBar.SetMessage(_displayedProgress);
        }
    }

    private static string Prefixed(LspMessageType type, string text) => type switch
    {
        LspMessageType.Error => "Language server error: " + text,
        LspMessageType.Warning => "Language server: " + text,
        _ => "Language server: " + text,
    };

    private const int MaxLength = 200;

    private static string Flatten(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;

        var collapsed = string.Join(' ', message.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return collapsed.Length <= MaxLength ? collapsed : collapsed[..MaxLength] + "…";
    }

    public void Dispose()
    {
        _client.MessageShown -= OnMessageShown;
        _client.ProgressReceived -= OnProgress;
    }
}
