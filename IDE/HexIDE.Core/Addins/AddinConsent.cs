using System.Text.Json.Serialization;

namespace HexIDE.Addins;

/// <summary>The two consent decisions, as stored in the consent file.</summary>
public static class ConsentDecisions
{
    public const string Allow = "allow";
    public const string Block = "block";
}

/// <summary>
/// A persisted first-load consent decision for one add-in build, keyed by its
/// <see cref="ManifestHash"/>. Because the hash is the content identity of the package, a new version
/// (new manifest) does not match a prior record, so the user is re-prompted on update.
/// </summary>
public sealed record AddinConsentRecord
{
    [JsonPropertyName("id")]           public string Id { get; init; } = "";
    [JsonPropertyName("publisherId")]  public string PublisherId { get; init; } = "";
    [JsonPropertyName("manifestHash")] public string ManifestHash { get; init; } = "";
    /// <summary><see cref="ConsentDecisions.Allow"/> or <see cref="ConsentDecisions.Block"/>.</summary>
    [JsonPropertyName("decision")]     public string Decision { get; init; } = "";
    [JsonPropertyName("timestampUtc")] public string TimestampUtc { get; init; } = "";
}

/// <summary>On-disk shape of <c>addin-consent.json</c>.</summary>
public sealed record AddinConsentFile
{
    [JsonPropertyName("schema")]   public int Schema { get; init; } = 2;
    [JsonPropertyName("consents")] public AddinConsentRecord[] Consents { get; init; } = [];
}

[JsonSerializable(typeof(AddinConsentFile))]
[JsonSourceGenerationOptions(WriteIndented = true)]
public partial class AddinConsentJsonContext : JsonSerializerContext;
