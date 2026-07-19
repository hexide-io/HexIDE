using System.Text.Json.Serialization;

namespace HexIDE.Addins;

/// <summary>
/// The signed manifest at the root of an add-in package (<c>addin.json</c>). Carries the add-in's
/// identity and metadata plus a SHA-256 of every file in the package. The publisher signs the raw
/// bytes of this file (see <c>addin.sig</c>), so tampering with any listed file — or with the
/// metadata itself — is detectable. The manifest is the <b>single source of add-in metadata</b>:
/// assembly attributes are not trusted because they are attacker-controlled.
/// </summary>
public sealed record AddinManifest
{
    [JsonPropertyName("schema")]           public int Schema { get; init; } = 1;
    [JsonPropertyName("id")]               public string Id { get; init; } = "";
    [JsonPropertyName("title")]            public string Title { get; init; } = "";
    [JsonPropertyName("description")]      public string Description { get; init; } = "";
    [JsonPropertyName("version")]          public string Version { get; init; } = "";
    [JsonPropertyName("author")]           public string Author { get; init; } = "";
    [JsonPropertyName("publisherId")]      public string PublisherId { get; init; } = "";
    /// <summary>The package-relative file name of the assembly that implements <c>IAddin</c>.</summary>
    [JsonPropertyName("entry")]            public string Entry { get; init; } = "";
    [JsonPropertyName("minHexideVersion")] public string? MinHexideVersion { get; init; }
    /// <summary>Optional package-relative path (forward-slash) of the publisher logo — a PNG that must
    /// also appear in <see cref="Files"/> (so it is hash-verified). Null = no logo. The verifier only
    /// honours it when it names a listed, in-package, traversal-free file.</summary>
    [JsonPropertyName("logoPath")]         public string? LogoPath { get; init; }
    /// <summary>Every file in the package and its SHA-256 (lower-case hex). The signature over the
    /// manifest therefore transitively covers the whole payload.</summary>
    [JsonPropertyName("files")]            public AddinFileHash[] Files { get; init; } = [];
}

public sealed record AddinFileHash
{
    [JsonPropertyName("path")]   public string Path { get; init; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; init; } = "";
}

/// <summary>
/// A publisher's identity record (<c>publisher.json</c>), signed by a marketplace intermediate. Binds
/// a stable <see cref="PublisherId"/> to the public key the publisher uses to sign their add-ins.
/// </summary>
public sealed record PublisherRecord
{
    [JsonPropertyName("publisherId")]    public string PublisherId { get; init; } = "";
    [JsonPropertyName("displayName")]    public string DisplayName { get; init; } = "";
    /// <summary>Base64 SubjectPublicKeyInfo (SPKI) of the publisher's ECDSA P-256 public key.</summary>
    [JsonPropertyName("publicKey")]      public string PublicKey { get; init; } = "";
    [JsonPropertyName("intermediateId")] public string IntermediateId { get; init; } = "";
}

/// <summary>
/// A marketplace intermediate record (<c>intermediate.json</c>), signed by the HexIDE root. The root
/// delegates publisher-identity issuance to the intermediate without ever exposing the root key.
/// </summary>
public sealed record IntermediateRecord
{
    [JsonPropertyName("intermediateId")] public string IntermediateId { get; init; } = "";
    [JsonPropertyName("displayName")]    public string DisplayName { get; init; } = "";
    /// <summary>Base64 SubjectPublicKeyInfo (SPKI) of the intermediate's ECDSA P-256 public key.</summary>
    [JsonPropertyName("publicKey")]      public string PublicKey { get; init; } = "";
}

[JsonSerializable(typeof(AddinManifest))]
[JsonSerializable(typeof(PublisherRecord))]
[JsonSerializable(typeof(IntermediateRecord))]
[JsonSourceGenerationOptions(WriteIndented = true)]
public partial class AddinJsonContext : JsonSerializerContext;
