using System.Text.Json;
using System.Text.Json.Serialization;

namespace HexIDE.IDE;

/// <summary>Pure (de)serialization for the dock <see cref="LayoutManifest"/>, including migration of the
/// legacy v1 (two-proportion) file. Kept free of file I/O so it can be unit-tested directly;
/// <see cref="WindowStateService"/> wraps it with the disk read/write.</summary>
public static class LayoutManifestJson
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(LayoutManifest manifest) => JsonSerializer.Serialize(manifest, s_json);

    /// <summary>Parse a manifest, migrating a legacy v1 file ({ leftProportion, rightProportion }) onto the
    /// default manifest. Returns <c>null</c> for empty or unparseable input (the caller falls back to
    /// <see cref="LayoutManifest.Default"/>).</summary>
    public static LayoutManifest? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Legacy v1 file has no "version" — migrate, preserving the two saved column proportions.
            if (!root.TryGetProperty("version", out _))
            {
                var left = root.TryGetProperty("leftProportion", out var l)
                    ? l.GetDouble() : LayoutManifest.DefaultLeftProportion;
                var right = root.TryGetProperty("rightProportion", out var r)
                    ? r.GetDouble() : LayoutManifest.DefaultRightProportion;
                return LayoutManifest.Default with { LeftProportion = left, RightProportion = right };
            }

            return JsonSerializer.Deserialize<LayoutManifest>(json, s_json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
