using System.Text.Json;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

namespace HexIDE.Tools.LanguageServers;

/// <summary>
/// One attached language server, as a row.
///
/// <para>
/// Everything here is a projection of a <see cref="LanguageServerConnection"/> taken at a moment. The row is
/// rebuilt rather than mutated when the registry changes, because a connection is a value and half-updating
/// one is how a view comes to show a state and a capability set that never coexisted.
/// </para>
/// </summary>
public sealed class LanguageServerRowViewModel
{
    private readonly LanguageServerConnection _c;
    private readonly ILocalizationService _localization;

    public LanguageServerRowViewModel(LanguageServerConnection connection, ILocalizationService localization)
    {
        _c = connection;
        _localization = localization;
    }

    public string Id => _c.Id;
    public string DisplayName => string.IsNullOrWhiteSpace(_c.DisplayName) ? _c.Id : _c.DisplayName;

    /// <summary>
    /// Which protocol this row speaks. Not localized: <c>LSP</c> and <c>DAP</c> are protocol names, in the
    /// same class as the transport words below — translating them would make them harder to recognise, not
    /// easier.
    /// </summary>
    public string Kind => _c.Kind == LanguageConnectionKind.DebugAdapter ? "DAP" : "LSP";

    /// <summary>The literal word from the configuration file, so it can be compared to it character for
    /// character. Not a localization key for that reason.</summary>
    public string Transport => _c.Transport switch
    {
        LanguageConnectionTransport.Pipe => "pipe",
        LanguageConnectionTransport.WebSocket => "websocket",
        _ => "stdio",
    };

    /// <summary>Command line, URL, or pipe name and role. Machine text; never translated.</summary>
    public string Endpoint => _c.Endpoint ?? "";
    public bool HasEndpoint => !string.IsNullOrWhiteSpace(_c.Endpoint);

    public int Priority => _c.Priority;

    /// <summary>
    /// Both halves of the claim, never one. Routing accepts a server either because its language id matches
    /// or because its extensions do, so showing one reproduces the confusion in hexide-io/HexIDE#277: a
    /// server that receives a document it does not appear to claim, or appears to claim one it never sees.
    /// </summary>
    public string Claims => _c.Extensions.Count == 0
        ? _c.LanguageId
        : $"{string.Join(' ', _c.Extensions)}  →  \"{_c.LanguageId}\"";

    public string State => _localization.GetString(_c.State switch
    {
        LanguageConnectionState.NotStarted => "Str.Tool.LanguageServers.Vm.State.NotStarted",
        LanguageConnectionState.Starting => "Str.Tool.LanguageServers.Vm.State.Starting",
        LanguageConnectionState.Running => "Str.Tool.LanguageServers.Vm.State.Running",
        LanguageConnectionState.Failed => "Str.Tool.LanguageServers.Vm.State.Failed",
        _ => "Str.Tool.LanguageServers.Vm.State.Stopped",
    });

    public bool IsFailed => _c.State == LanguageConnectionState.Failed;
    public bool IsRunning => _c.State == LanguageConnectionState.Running;

    /// <summary>
    /// How long it has been in that state, for the one case where the state alone is not enough: a handshake
    /// is bounded in tens of seconds, so <c>Starting</c> for four seconds and <c>Starting</c> for forty are
    /// different situations wearing the same word.
    /// </summary>
    public string Age
    {
        get
        {
            if (_c.StateSince is not { } since) return "";
            var d = DateTimeOffset.UtcNow - since;
            if (d < TimeSpan.Zero) d = TimeSpan.Zero;
            var text = d.TotalSeconds < 60 ? $"{(int)d.TotalSeconds} s"
                     : d.TotalMinutes < 60 ? $"{(int)d.TotalMinutes} m {d.Seconds} s"
                     : $"{(int)d.TotalHours} h {d.Minutes} m";
            return string.Format(
                _localization.GetString("Str.Tool.LanguageServers.Vm.For"), text);
        }
    }

    /// <summary>The server's own claim about itself, or empty when it made none.</summary>
    public string ReportedBy => _c.ReportedIdentity switch
    {
        { Name: { Length: > 0 } n, Version: { Length: > 0 } v } => $"{n} {v}",
        { Name: { Length: > 0 } n } => n,
        { Version: { Length: > 0 } v } => v,
        _ => "",
    };

    public bool HasReportedIdentity => ReportedBy.Length > 0;

    /// <summary>Exactly what it advertised, pretty-printed. Raw because any summary invented here is wrong
    /// for a server nobody has met yet.</summary>
    public string CapabilitiesJson => _c.Capabilities is { } caps
        ? JsonSerializer.Serialize(caps, IndentedJson)
        : "";

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    public bool HasCapabilities => _c.Capabilities is { } c && c.ValueKind != JsonValueKind.Undefined;

    /// <summary>
    /// True when the server answered the handshake and then advertised nothing at all — the state that
    /// looks healthiest and is least useful, and the one a bare "Running" badge reports as success.
    /// </summary>
    public bool AdvertisedNothing =>
        IsRunning
        && (_c.Capabilities is not { } caps
            || caps.ValueKind != JsonValueKind.Object
            || !caps.EnumerateObject().Any());

    /// <summary>
    /// True when HexIDE is deliberately sending this server no documents, because it did not advertise
    /// document sync. The same call the didOpen gate makes, so the row cannot disagree with the behaviour.
    /// </summary>
    public bool SendingNothing => IsRunning && !ServerCapabilities.AcceptsOpenClose(_c.Capabilities);

    /// <summary>Plain text, for a bug report someone can send to whoever wrote the server.</summary>
    public string ToReportText()
    {
        var lines = new List<string>
        {
            $"{DisplayName}  [{Id}]  {Kind}",
            $"  transport : {Transport}" + (HasEndpoint ? $" · {Endpoint}" : ""),
            $"  claims    : {Claims}",
            $"  priority  : {Priority}",
            $"  state     : {State}{(Age.Length > 0 ? " · " + Age : "")}",
        };
        if (HasReportedIdentity) lines.Add($"  reported  : {ReportedBy}");
        lines.Add("  advertised at initialize:");
        lines.Add(HasCapabilities ? CapabilitiesJson : "    (nothing)");
        return string.Join(Environment.NewLine, lines);
    }
}
