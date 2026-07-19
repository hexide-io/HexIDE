using System;
using System.Text.Json.Serialization;

namespace HexIDE.Addins;

/// <summary>A HexIDE-root-signed revocation list. Revokes by build (manifest hash), publisher, or
/// marketplace intermediate. Bundled (floor) and/or fetched; the freshest validly-signed list wins.</summary>
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
    /// disabling the bundled first-party add-ins.
    /// </summary>
    public static string? Reason(RevocationList list, string? manifestHash, string? publisherId,
                                 string? intermediateId, bool isFirstParty)
    {
        if (Contains(list.Revoked.ManifestHashes, manifestHash))
            return "build revoked";

        if (isFirstParty)
            return null;   // first-party is immune to identity-level (publisher/intermediate) revocation

        if (Contains(list.Revoked.PublisherIds, publisherId))
            return "publisher revoked";
        if (Contains(list.Revoked.IntermediateIds, intermediateId))
            return "intermediate revoked";
        return null;
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
