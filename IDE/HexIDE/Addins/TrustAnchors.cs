using System;
using System.IO;
using System.Linq;
using Serilog;

namespace HexIDE.Addins;

/// <summary>
/// The IDE's baked-in trust anchors (embedded <c>hexide-root.pub</c> / <c>hexide-firstparty.pub</c>) and
/// their key fingerprints. The fingerprints are the <b>canonical values HexIDE publishes</b> (TRUST.md
/// and the About dialog) so a user can confirm out of band that this binary trusts the genuine root and
/// first-party key — the anchor the whole trust-chain inspector compares back to.
/// </summary>
internal static class TrustAnchors
{
    private static readonly Lazy<string?> s_root = new(() => LoadEmbeddedKey("hexide-root.pub"));
    private static readonly Lazy<string?> s_firstParty = new(() => LoadEmbeddedKey("hexide-firstparty.pub"));

    public static string? RootPublicKey => s_root.Value;
    public static string? FirstPartyPublicKey => s_firstParty.Value;

    public static string? RootFingerprint => s_root.Value is { } k ? PackageCrypto.KeyFingerprint(k) : null;
    public static string? FirstPartyFingerprint => s_firstParty.Value is { } k ? PackageCrypto.KeyFingerprint(k) : null;

    private static string? LoadEmbeddedKey(string fileName)
    {
        var asm = typeof(TrustAnchors).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            Log.Error("TrustAnchors: embedded key {File} not found", fileName);
            return null;
        }

        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }
}
