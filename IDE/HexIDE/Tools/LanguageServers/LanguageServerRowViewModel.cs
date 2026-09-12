using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexIDE.Conversations;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

namespace HexIDE.Tools.LanguageServers;

/// <summary>One rung of the connect chain: a stage, what happened there, and how long in.</summary>
public sealed class LanguageServerStepViewModel
{
    private readonly LanguageConnectionStep _s;
    private readonly ILocalizationService _localization;

    public LanguageServerStepViewModel(LanguageConnectionStep step, ILocalizationService localization)
    {
        _s = step;
        _localization = localization;
    }

    public string Stage => _localization.GetString(_s.Stage switch
    {
        LanguageConnectionStage.Connecting => "Str.Tool.LanguageServers.Vm.Stage.Connecting",
        LanguageConnectionStage.Connected => "Str.Tool.LanguageServers.Vm.Stage.Connected",
        LanguageConnectionStage.HandshakeSent => "Str.Tool.LanguageServers.Vm.Stage.HandshakeSent",
        LanguageConnectionStage.Initialized => "Str.Tool.LanguageServers.Vm.Stage.Initialized",
        _ => "Str.Tool.LanguageServers.Vm.Stage.NotAttempted",
    });

    /// <summary>A tick, a cross, or a dash. Glyphs rather than words so the chain reads as a shape and
    /// the eye finds the break without reading every rung.</summary>
    public string Marker => _s.Outcome switch
    {
        LanguageConnectionStepOutcome.Reached => "\u2713",
        LanguageConnectionStepOutcome.StoppedHere => "\u2717",
        LanguageConnectionStepOutcome.Cancelled => "\u2013",
        _ => " ",
    };

    public bool IsBreak => _s.Outcome == LanguageConnectionStepOutcome.StoppedHere;

    /// <summary>Cancelled is called out separately because it is NOT a fault: the IDE was shutting down.</summary>
    public bool IsCancelled => _s.Outcome == LanguageConnectionStepOutcome.Cancelled;

    public string Elapsed => _s.At is { } at ? $"{at.TotalMilliseconds:N0} ms" : "";

    /// <summary>Verbatim machine text — an exception message, an OS error. Never translated: it is the
    /// thing worth quoting to whoever wrote the server.</summary>
    public string Detail => _s.Detail ?? "";
    public bool HasDetail => !string.IsNullOrWhiteSpace(_s.Detail);
}
/// <summary>
/// One attached language server, as a row.
///
/// <para>
/// Everything here is a projection of a <see cref="LanguageServerConnection"/> taken at a moment. The row is
/// rebuilt rather than mutated when the registry changes, because a connection is a value and half-updating
/// one is how a view comes to show a state and a capability set that never coexisted.
/// </para>
/// </summary>
public sealed partial class LanguageServerRowViewModel : ObservableObject
{
    private readonly LanguageServerConnection _c;
    private readonly ILocalizationService _localization;
    private readonly ConversationLog _capture;
    private readonly IEventBus? _events;

    public LanguageServerRowViewModel(
        LanguageServerConnection connection, ILocalizationService localization, ConversationLog capture,
        IEventBus? events = null)
    {
        _c = connection;
        _localization = localization;
        _capture = capture;
        _events = events;
    }

    /// <summary>
    /// Opens the protocol inspector on this server's traffic alone.
    /// </summary>
    /// <remarks>
    /// <b>Offered whether or not the server is armed, and that is a deliberate departure from the plan.</b>
    /// The plan said to show it only when armed. Envelopes are recorded unconditionally, so an unarmed
    /// connection still answers "was it even sent, and what came back" — and the moment somebody most wants
    /// that answer is a server that has failed, which is exactly the moment they will not have armed it.
    /// Hiding the link there would withhold it in the only case it was built for.
    /// </remarks>
    [RelayCommand]
    private void ShowMessages() => _events?.Publish(new OpenProtocolInspectorEvent(Id));

    /// <summary>
    /// Whether message bodies are being kept for this server.
    /// </summary>
    /// <remarks>
    /// <b>The row's only writable state, and the window's first interactivity of any kind.</b> It reads
    /// through to the capture rather than holding a copy, so it cannot drift from what is actually being
    /// recorded — which matters here more than usual, because two other things arm the same connection: the
    /// automation surface and the launch flag.
    ///
    /// <para>
    /// Envelopes are recorded whether or not this is on. What it governs is content, which is the only part
    /// with a privacy cost and the only part large enough to have a memory cost.
    /// </para>
    /// </remarks>
    public bool IsArmed
    {
        get => _capture.IsArmed(_c.Id);
        set
        {
            if (value == IsArmed) return;

            // The capture raises ArmingChanged, and the window turns that back into the notification for
            // this property. Raising it here as well would be a second, earlier answer from a different
            // source, and the two would disagree the first time an arming request was refused.
            _capture.Arm(_c.Id, value);
        }
    }

    /// <summary>Re-reads the arming, for when something other than this row changed it.</summary>
    public void ArmingChanged() => OnPropertyChanged(nameof(IsArmed));

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


    /// <summary>
    /// How far the last attempt got, rung by rung, with the point it stopped at.
    /// </summary>
    /// <remarks>
    /// The shape is the add-in trust view's: a chain and where it broke. Empty for a connection nothing
    /// has tried yet, which is the honest answer rather than a chain of dashes.
    /// </remarks>
    public IReadOnlyList<LanguageServerStepViewModel> Chain =>
        _c.Attempt is { } a
            ? [.. a.Steps.Select(step => new LanguageServerStepViewModel(step, _localization))]
            : [];

    public bool HasChain => Chain.Count > 0;

    /// <summary>True when the attempt ended because the IDE was stopping. Not a fault, and the reason the
    /// registry writing Failed for it is wrong.</summary>
    public bool WasCancelled => Chain.Any(s => s.IsCancelled);

    /// <summary>Requests this client declined because the server never advertised them. A feature that is
    /// off because it was never claimed looks exactly like one that is broken; this is the difference.</summary>
    public IReadOnlyList<string> Declined => _c.DeclinedByClient ?? [];
    public bool HasDeclined => Declined.Count > 0;

    public int OpenDocumentCount => _c.OpenDocumentCount;

    /// <summary>Zero open documents has two very different causes — nothing of this language is open, or
    /// documents are open and are not reaching the server (hexide-io/HexIDE#273).</summary>
    public bool ShowsDocumentCount => IsRunning;

    public string WorkspaceRoot => _c.WorkspaceRootUri ?? "";
    public bool HasWorkspaceRoot => !string.IsNullOrWhiteSpace(_c.WorkspaceRootUri);
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
        if (HasChain)
        {
            lines.Add("  how far it got:");
            foreach (var step in Chain)
            {
                lines.Add($"    {step.Marker} {step.Stage}"
                        + (step.Elapsed.Length > 0 ? $"  ({step.Elapsed})" : "")
                        + (step.HasDetail ? $"  {step.Detail}" : ""));
            }
        }
        if (HasDeclined) lines.Add($"  declined by the client: {string.Join(", ", Declined)}");
        if (HasWorkspaceRoot) lines.Add($"  workspace root sent: {WorkspaceRoot}");
        lines.Add("  advertised at initialize:");
        lines.Add(HasCapabilities ? CapabilitiesJson : "    (nothing)");
        return string.Join(Environment.NewLine, lines);
    }
}
