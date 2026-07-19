using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HexIDE.Addins;
using Serilog;

namespace HexIDE.Addins;

/// <summary>
/// Persists first-load consent decisions to <c>%AppData%/HexIDE/addin-consent.json</c>, keyed by the
/// package's manifest hash. Because the key is the package's content identity, a new version (new
/// manifest) has no matching record and the user is re-prompted.
/// </summary>
internal sealed class ConsentStore
{
    private readonly string _filePath;
    private readonly Dictionary<string, AddinConsentRecord> _byHash;

    public ConsentStore() : this(DefaultFilePath()) { }

    public ConsentStore(string filePath)
    {
        _filePath = filePath;
        _byHash = Load();
    }

    /// <summary>The recorded decision for a manifest hash (<see cref="ConsentDecisions"/>), or null if
    /// the user has never been asked about this exact build.</summary>
    public string? GetDecision(string manifestHash) =>
        _byHash.TryGetValue(manifestHash, out var r) ? r.Decision : null;

    public void Record(string id, string publisherId, string manifestHash, string decision)
    {
        _byHash[manifestHash] = new AddinConsentRecord
        {
            Id = id,
            PublisherId = publisherId,
            ManifestHash = manifestHash,
            Decision = decision,
            TimestampUtc = DateTime.UtcNow.ToString("o"),
        };
        Save();
    }

    /// <summary>Drops the recorded decision for a manifest hash, so the add-in is re-prompted next launch.</summary>
    public void Revoke(string manifestHash)
    {
        if (_byHash.Remove(manifestHash))
            Save();
    }

    private static string DefaultFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "HexIDE");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "addin-consent.json");
    }

    private Dictionary<string, AddinConsentRecord> Load()
    {
        var map = new Dictionary<string, AddinConsentRecord>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(_filePath))
                return map;

            var bytes = File.ReadAllBytes(_filePath);
            var data = JsonSerializer.Deserialize(bytes, AddinConsentJsonContext.Default.AddinConsentFile);
            if (data is not null && data.Schema != 2)
            {
                // Unknown (future) schema: fail safe — treat as no recorded consent (everything re-prompts)
                // rather than risk misreading it. Do not overwrite the file unless the user makes a decision.
                Log.Warning("ConsentStore: unsupported schema {Schema} in {Path}; ignoring", data.Schema, _filePath);
                return map;
            }
            foreach (var r in data?.Consents ?? [])
                if (!string.IsNullOrEmpty(r.ManifestHash))
                    map[r.ManifestHash] = r;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ConsentStore: failed to read {Path}, using empty", _filePath);
        }
        return map;
    }

    private void Save()
    {
        try
        {
            var data = new AddinConsentFile { Consents = _byHash.Values.ToArray() };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(data, AddinConsentJsonContext.Default.AddinConsentFile);
            File.WriteAllBytes(_filePath, bytes);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ConsentStore: failed to save {Path}", _filePath);
        }
    }
}
