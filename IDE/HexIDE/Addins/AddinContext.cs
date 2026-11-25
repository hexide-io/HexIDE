using HexIDE.Addins;

namespace HexIDE.Addins;

internal sealed class AddinContext(AddinInfo info)
{
    public AddinInfo Info { get; set; } = info;
    public IAddin? Instance { get; set; }
    public AddinStatus Status { get; set; }

    /// <summary>What Initialize registered, kept to take out if the add-in is revoked mid-session (#5577): its commands,
    /// menus, tool windows, options pages, templates and event subscriptions among them.</summary>
    public IReadOnlyList<Action> InitializationWithdrawals { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public AddinFailureReport? ReportedFailure { get; set; }

    /// <summary>
    /// Whether the package was refused before it could load: incompatible, under a reserved or duplicate id, untrusted, or
    /// without a manifest. Such a row can share the id of a package that did load, or of the Basic provider, so it owns no
    /// contributions and its state refuses no one's launches (#5597).
    /// </summary>
    public bool RefusedAtLoad { get; init; }

    /// <summary>The package directory this add-in was loaded from (also the enable/disable key).</summary>
    public string PackageDirectory { get; set; } = info.AssemblyPath;

    /// <summary>The collectible context the entry assembly was loaded into, if it loaded.</summary>
    public AddinLoadContext? LoadContext { get; set; }

    /// <summary>The verified manifest (when known) — needed to (late-)load the entry assembly.</summary>
    public AddinManifest? Manifest { get; set; }

    /// <summary>For an <see cref="AddinStatus.AwaitingConsent"/> add-in: the verification result kept so
    /// the post-window consent pass can prompt and then late-load. Null once resolved.</summary>
    public PackageVerificationResult? PendingResult { get; set; }
}

internal sealed record AddinFailureReport(string Message, string? ExceptionType, string? ExceptionMessage, DateTimeOffset Time);
