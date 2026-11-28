using System;
using System.Text.Json.Serialization;

namespace HexIDE.Addins;

/// <summary>A HexIDE-root-signed revocation list. Revokes by build (manifest hash), publisher, marketplace
/// intermediate, or signing key. Bundled (floor) and/or fetched; the freshest validly-signed list wins.</summary>
/// <remarks>Unknown fields are skipped and <see cref="Schema"/> is never checked, as in every build so far (measured on
/// v0.7.0, #5315). Keep it so: an older build that refused a list with a new field would keep its last list, and stop
/// updating, the first time a newer one used it.</remarks>
public sealed record RevocationList
{
    [JsonPropertyName("schema")]    public int Schema { get; init; } = 1;
    /// <summary>ISO-8601. The freshest validly-signed list (latest <c>issuedUtc</c>) is the active one.</summary>
    [JsonPropertyName("issuedUtc")] public string IssuedUtc { get; init; } = "";
    [JsonPropertyName("revoked")]   public RevokedSets Revoked { get; init; } = new();
}

public sealed record RevokedSets
{
    [JsonPropertyName("manifestHashes")]  public string[] ManifestHashes { get; init; } = [];
    [JsonPropertyName("publisherIds")]    public string[] PublisherIds { get; init; } = [];
    [JsonPropertyName("intermediateIds")] public string[] IntermediateIds { get; init; } = [];
    /// <summary>
    /// Revoked signing keys, by SHA-256 fingerprint of the key's DER SubjectPublicKeyInfo (#5315): 64 hex digits, with
    /// case and spaces ignored, so the grouped form TRUST.md and Trust details show works as written. Checked against a
    /// package's publisher key and its intermediate's key, never the root's. Builds from before #5315 skip this field, so
    /// on them a key revocation does nothing; where that matters, revoke the publisher id as well.
    /// </summary>
    [JsonPropertyName("keyFingerprints")] public string[] KeyFingerprints { get; init; } = [];
}

[JsonSerializable(typeof(RevocationList))]
[JsonSourceGenerationOptions(WriteIndented = true)]
public partial class RevocationJsonContext : JsonSerializerContext;

/// <summary>Pure revocation decision for one package against a parsed list.</summary>
public static class RevocationCheck
{
    /// <summary>
    /// Returns the revocation reason, or null if not revoked. <b>Build</b> (manifest-hash) revocation
    /// applies to everyone — including a first-party build, so HexIDE can pull a bad release of its own
    /// add-in. <b>Publisher</b> and <b>intermediate</b> revocation are identity-level and <b>exempt
    /// key-pinned first-party</b> add-ins, so a marketplace-key compromise can be killed without
    /// disabling the bundled first-party add-ins. Checks no key: callers that have the chain's keys use the overload
    /// that takes them.
    /// </summary>
    public static string? Reason(RevocationList list, string? manifestHash, string? publisherId,
                                 string? intermediateId, bool isFirstParty) =>
        Reason(list, manifestHash, publisherId, intermediateId, isFirstParty, null, null);

    /// <summary>
    /// Returns the revocation reason, or null if not revoked, checking the package's keys too (#5315). A revoked
    /// <b>publisher key</b> reaches every package it signed, first-party included: revoking the first-party key is a
    /// deliberate act about that key. A revoked <b>intermediate key</b> is identity-level, like an intermediate id, so it
    /// exempts first-party, and a stolen marketplace key can't switch off the bundled add-ins. The fingerprints are as
    /// <see cref="PackageCrypto.KeyFingerprint"/> gives them; any spacing and case match.
    /// </summary>
    public static string? Reason(RevocationList list, string? manifestHash, string? publisherId,
                                 string? intermediateId, bool isFirstParty,
                                 string? publisherKeyFingerprint, string? intermediateKeyFingerprint)
    {
        if (Contains(list.Revoked.ManifestHashes, manifestHash))
            return "build revoked";
        if (IsKeyRevoked(list, publisherKeyFingerprint))
            return "publisher key revoked";

        if (isFirstParty)
            return null;   // first-party is immune to identity-level (publisher/intermediate) revocation

        if (Contains(list.Revoked.PublisherIds, publisherId))
            return "publisher revoked";
        if (Contains(list.Revoked.IntermediateIds, intermediateId))
            return "intermediate revoked";
        if (IsKeyRevoked(list, intermediateKeyFingerprint))
            return "intermediate key revoked";
        return null;
    }

    /// <summary>Whether the list revokes the key with this fingerprint (#5315). False for a fingerprint that isn't 64 hex
    /// digits once spaces are dropped, and an entry in the list that isn't one never matches.</summary>
    public static bool IsKeyRevoked(RevocationList list, string? keyFingerprint)
    {
        if (NormalizeKeyFingerprint(keyFingerprint) is not { } key)
            return false;
        foreach (var entry in list.Revoked.KeyFingerprints)
            if (string.Equals(NormalizeKeyFingerprint(entry), key, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>A key fingerprint as 64 upper-case hex digits, with its spaces dropped, or null when it isn't one.</summary>
    public static string? NormalizeKeyFingerprint(string? fingerprint)
    {
        if (fingerprint is null)
            return null;
        var digits = fingerprint.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        if (digits.Length != 64)
            return null;
        foreach (var c in digits)
            if (c is not (>= '0' and <= '9' or >= 'A' and <= 'F'))
                return null;
        return digits;
    }

    private static bool Contains(string[] set, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;
        foreach (var s in set)
            if (string.Equals(s, value, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
