using HexIDE.Addins;

namespace HexIDE.Addins;

internal sealed class AddinContext(AddinInfo info)
{
    public AddinInfo Info { get; } = info;
    public IAddin? Instance { get; set; }
    public AddinStatus Status { get; set; }
    public string? ErrorMessage { get; set; }

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
