namespace HexIDE.Addins;

/// <summary>
/// One verified link in an add-in's signing chain, for display. Every field is an <b>IDE-computed fact</b>
/// — a signed display name, a stable id, or a key fingerprint — never publisher-asserted decoration. The
/// <see cref="KeyFingerprint"/> is the ground truth a power user compares out of band; the
/// <see cref="Label"/> is only a convenience.
/// </summary>
public sealed record TrustLink(string Label, string Id, string KeyFingerprint);

/// <summary>
/// The verified signing chain behind an add-in (publisher → marketplace intermediate → HexIDE root), built
/// by <see cref="PackageVerification"/> on a <see cref="PackageVerdict.Verified"/> result. Read-only and
/// content-sandboxed: it carries only cryptographic identity facts, so it is safe to surface even at the
/// first-load consent moment — the deliberate inverse of the publisher-asserted logo.
/// </summary>
public sealed record TrustChain
{
    /// <summary>The signed build: the add-in version and its manifest SHA-256.</summary>
    public required string BuildVersion { get; init; }
    public required string BuildHash { get; init; }
    /// <summary>True iff the publisher key is the pinned first-party key.</summary>
    public required bool IsFirstParty { get; init; }
    /// <summary>Signs the manifest (the build).</summary>
    public required TrustLink Publisher { get; init; }
    /// <summary>The marketplace intermediate; signs the publisher record.</summary>
    public required TrustLink Intermediate { get; init; }
    /// <summary>The baked-in HexIDE root of trust; signs the intermediate.</summary>
    public required TrustLink Root { get; init; }
}
