namespace HexIDE.Addins;

/// <summary>
/// Public, read-only view of an installed add-in. For a signed package, <see cref="AssemblyPath"/> is
/// the package directory and the metadata comes from the (verified) manifest. <see cref="Publisher"/>
/// is the verified publisher display name, or empty for an unsigned/developer-mode add-in.
/// <see cref="ManifestHash"/> is the package's content identity (also the consent key).
/// </summary>
public record AddinInfo(
    string AssemblyPath,
    string Title,
    string Description,
    string Version,
    string Author,
    string Publisher = "",
    string Id = "",
    string ManifestHash = "",
    string? LogoPath = null,
    TrustChain? Chain = null);

public enum AddinStatus
{
    /// <summary>Verified package, consented (or first-party), loaded and initialized.</summary>
    Loaded,

    /// <summary>Verification or initialization failed.</summary>
    Error,

    /// <summary>Disabled by the user (enable/disable axis); not loaded this session.</summary>
    Disabled,

    /// <summary>Signature missing/invalid and developer mode is off — refused.</summary>
    Untrusted,

    /// <summary>Loaded via developer mode despite a missing/invalid signature.</summary>
    DeveloperUnsigned,

    /// <summary>Verified (or dev-unsigned) but awaiting the user's first-load consent — not loaded yet.</summary>
    AwaitingConsent,

    /// <summary>The user blocked this add-in at the consent prompt — not loaded.</summary>
    ConsentDenied,

    /// <summary>Revoked by the HexIDE-root-signed revocation list — not loaded.</summary>
    Revoked,
}
