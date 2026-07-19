using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Platform;
using Serilog;

namespace HexIDE.Localization;

/// <summary>
/// File-backed implementation of <see cref="IUserTranslationsService"/>. Each locale's overrides live in
/// <c>{dir}/{id}.json</c> as a <see cref="LanguagePackRecord"/>. The service loads <c>en.json</c> itself
/// (via the same <see cref="AssetLoader"/> path as <c>LocalizationService</c>) so it can validate against
/// the canonical map without depending on <see cref="ILocalizationService"/> — keeping the DI graph acyclic.
/// </summary>
public sealed partial class UserTranslationsService : IUserTranslationsService
{
    private readonly string _dir;

    // The canonical (en) map — used only to reject orphan keys and validate placeholders. Never mutated.
    private readonly Dictionary<string, string> _canonical;

    // Validated overrides per locale id (case-sensitive keys, matching the Str.* convention).
    private readonly Dictionary<string, Dictionary<string, string>> _byId = new(StringComparer.Ordinal);

    // Matches a single-brace placeholder like {0}, {1,-8}, {2:N2}; captures the integer index. Doubled
    // braces ({{ / }}) are stripped before matching so escaped literals are not treated as placeholders.
    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex PlaceholderRegex();

    public event Action<string>? OverridesChanged;

    /// <summary>Production constructor: the per-user translations directory under <c>%AppData%/HexIDE</c>.</summary>
    public UserTranslationsService() : this(DefaultDir())
    {
    }

    /// <summary>Test/seam constructor: overrides the storage directory.</summary>
    internal UserTranslationsService(string dir)
    {
        _dir = dir;
        try
        {
            Directory.CreateDirectory(_dir);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "UserTranslationsService: Failed to create translations dir {Dir}", _dir);
        }

        _canonical = LoadCanonical();
        LoadAll();
    }

    private static string DefaultDir()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "HexIDE", "translations");
    }

    public void LoadAll()
    {
        _byId.Clear();

        string[] files;
        try
        {
            if (!Directory.Exists(_dir)) return;
            files = Directory.GetFiles(_dir, "*.json");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "UserTranslationsService: Failed to enumerate {Dir}", _dir);
            return;
        }

        foreach (var file in files)
        {
            var id = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrEmpty(id)) continue;

            try
            {
                var json = File.ReadAllText(file);
                var record = JsonSerializer.Deserialize(json, LocalizationJsonContext.Default.LanguagePackRecord);
                var strings = record?.Strings;
                if (strings is null || strings.Count == 0) continue;

                var validated = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (key, value) in strings)
                {
                    if (!_canonical.ContainsKey(key)) continue;            // orphan key — drop
                    if (string.IsNullOrWhiteSpace(value)) continue;         // empty/whitespace — drop
                    if (!PreservesPlaceholders(key, value)) continue;       // placeholder-breaker — drop
                    validated[key] = value;
                }

                if (validated.Count > 0)
                    _byId[id] = validated;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "UserTranslationsService: Failed to load override file {File}", file);
            }
        }
    }

    public IReadOnlyDictionary<string, string> GetOverrides(string id) =>
        _byId.TryGetValue(id, out var map) ? map : EmptyMap;

    public bool SetOverride(string id, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ClearOverride(id, key);
            return true;
        }

        if (!PreservesPlaceholders(key, value))
            return false;

        if (!_byId.TryGetValue(id, out var map))
        {
            map = new Dictionary<string, string>(StringComparer.Ordinal);
            _byId[id] = map;
        }
        map[key] = value;

        Persist(id);
        OverridesChanged?.Invoke(id);
        return true;
    }

    public void ClearOverride(string id, string key)
    {
        if (!_byId.TryGetValue(id, out var map) || !map.Remove(key))
            return;

        if (map.Count == 0)
        {
            _byId.Remove(id);
            DeleteFile(id);
        }
        else
        {
            Persist(id);
        }

        OverridesChanged?.Invoke(id);
    }

    public void ResetAll(string id)
    {
        var existed = _byId.Remove(id);
        DeleteFile(id);
        if (existed)
            OverridesChanged?.Invoke(id);
    }

    public IReadOnlyCollection<string> OverriddenIds => _byId.Keys;

    public bool PreservesPlaceholders(string canonicalKey, string candidate)
    {
        var candSet = IndexSet(candidate);

        // A key with a canonical entry must keep exactly the same {N} index set; a key with none imposes
        // no index requirement (but still must be a well-formed format string — checked below).
        if (_canonical.TryGetValue(canonicalKey, out var canonical) && !IndexSet(canonical).SetEquals(candSet))
            return false;

        // Reject anything string.Format cannot render — a stray unescaped '{' or '}' passes the index-set
        // check above but throws FormatException at the call site, so it must never reach resolution.
        try
        {
            var argCount = candSet.Count == 0 ? 0 : candSet.Max() + 1;
            _ = string.Format(candidate, new object?[argCount]);
        }
        catch (FormatException)
        {
            return false;
        }

        return true;
    }

    // ── Internals ────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, string> EmptyMap =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static HashSet<int> IndexSet(string text)
    {
        // Strip escaped braces so "{{0}}" contributes no placeholder.
        var stripped = text.Replace("{{", string.Empty).Replace("}}", string.Empty);
        var set = new HashSet<int>();
        foreach (Match m in PlaceholderRegex().Matches(stripped))
            if (int.TryParse(m.Groups[1].Value, out var idx))
                set.Add(idx);
        return set;
    }

    private string PathFor(string id) => Path.Combine(_dir, $"{id}.json");

    private void Persist(string id)
    {
        if (!_byId.TryGetValue(id, out var map)) return;
        try
        {
            Directory.CreateDirectory(_dir);
            var record = new LanguagePackRecord(id, null, new Dictionary<string, string>(map));
            var json = JsonSerializer.Serialize(record, LocalizationJsonContext.Default.LanguagePackRecord);
            File.WriteAllText(PathFor(id), json);
            Log.Debug("UserTranslationsService: Wrote overrides for '{Id}'", id);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "UserTranslationsService: Failed to write overrides for '{Id}'", id);
        }
    }

    private void DeleteFile(string id)
    {
        try
        {
            var path = PathFor(id);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "UserTranslationsService: Failed to delete overrides for '{Id}'", id);
        }
    }

    private static Dictionary<string, string> LoadCanonical()
    {
        try
        {
            var uri = new Uri("avares://HexIDE/Localization/Packs/en.json");
            using var stream = AssetLoader.Open(uri);
            var record = JsonSerializer.Deserialize(stream, LocalizationJsonContext.Default.LanguagePackRecord);
            return record?.Strings ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "UserTranslationsService: Failed to load canonical pack (en.json)");
            return new Dictionary<string, string>();
        }
    }
}
