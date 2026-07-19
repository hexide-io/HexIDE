namespace HexIDE.Addins;

/// <summary>
/// Verifies a signed add-in package directory against the baked-in HexIDE root of trust. Verification
/// runs entirely from files on disk, before any add-in assembly is loaded — the manifest is the
/// metadata source, so an untrusted assembly is never reflected over or executed to inspect it.
/// </summary>
public interface IPackageVerifier
{
    PackageVerificationResult Verify(string packageDirectory);
}

public enum PackageVerdict
{
    /// <summary>Integrity + the full signature chain to the HexIDE root all pass.</summary>
    Verified,

    /// <summary>A signature is missing, malformed, or does not chain to the root, or a file hash
    /// mismatches. Such a package is never loaded in normal operation (developer mode aside).</summary>
    Untrusted,
}

/// <summary>
/// Outcome of verifying a package. <see cref="Manifest"/> is populated whenever <c>addin.json</c> was
/// parseable — even for an <see cref="PackageVerdict.Untrusted"/> result — so developer mode can still
/// resolve the entry assembly. Publisher fields are only set on a <see cref="PackageVerdict.Verified"/>
/// result.
/// </summary>
public sealed record PackageVerificationResult(
    PackageVerdict Verdict,
    AddinManifest? Manifest,
    string? PublisherDisplayName,
    string? PublisherId,
    string? Error)
{
    public bool IsVerified => Verdict == PackageVerdict.Verified;

    /// <summary>SHA-256 (lower-case hex) of the raw <c>addin.json</c> bytes — a stable content identity
    /// for the package, used as the consent key. Null only when the manifest could not be read. Because
    /// the manifest lists the version and every file hash, any change to the package changes this.</summary>
    public string? ManifestHash { get; init; }

    /// <summary>True iff the package is <see cref="PackageVerdict.Verified"/> AND its verified publisher
    /// key equals the baked-in first-party key. First-party is pinned to a <b>key</b> (possession of the
    /// first-party private key), not a publisherId string — so a compromised intermediate or a
    /// mis-issued publisherId cannot masquerade as first-party.</summary>
    public bool IsFirstParty { get; init; }

    /// <summary>The verified intermediate's id (only on a <see cref="PackageVerdict.Verified"/> result) —
    /// used for intermediate-level revocation (the marketplace kill switch).</summary>
    public string? IntermediateId { get; init; }

    /// <summary>The publisher logo's package-relative path, <b>sanitized</b>: set only when the manifest's
    /// <c>logoPath</c> names a listed (hence hash-verified), in-package, traversal-free file on a
    /// <see cref="PackageVerdict.Verified"/> result. Null otherwise. Safe to combine with the package
    /// directory and decode — it can never point outside the package or at an unverified file.</summary>
    public string? LogoPath { get; init; }

    /// <summary>The verified signing chain (publisher → intermediate → root, with key fingerprints), set on
    /// a <see cref="PackageVerdict.Verified"/> result. Null otherwise. Feeds the read-only trust-chain
    /// inspector; carries only IDE-computed facts, never publisher-asserted decoration.</summary>
    public TrustChain? Chain { get; init; }
}
