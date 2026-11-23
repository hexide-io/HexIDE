using HexIDE.IDE;
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
    private readonly bool _recoveryMode = Static.IsRecoveryMode;

    // The IDE's canonical (en) map. Never mutated.
    private readonly Dictionary<string, string> _ideCanonical;

    // The IDE's canonical map with every registered area's (#5394): what an override must translate, and whose placeholders
    // it must keep. Rebuilt when the areas change.
    private Dictionary<string, string> _canonicalStrings;
    private int _canonicalVersion = -1;
    private readonly LanguagePackAreas _areas;

    // Validated overrides per locale id (case-sensitive keys, matching the Str.* convention).
    private readonly Dictionary<string, Dictionary<string, string>> _byId = new(StringComparer.Ordinal);

    // Overrides whose key nothing registered holds now, per locale id: an area's that isn't registered yet or has gone, or
    // a key since retired. They're kept and saved with the rest, never applied, and placed if an area that owns them
    // registers (#5394). Dropping them would lose them from disk at the language's next save, and a tier-2 pack is
    // installed as just such a file.
    private readonly Dictionary<string, Dictionary<string, string>> _kept = new(StringComparer.Ordinal);

    // Matches a single-brace placeholder like {0}, {1,-8}, {2:N2}; captures the integer index. Doubled
    // braces ({{ / }}) are stripped before matching so escaped literals are not treated as placeholders.
    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex PlaceholderRegex();

    public event Action<string>? OverridesChanged;

    /// <summary>Production constructor: the per-user translations directory under <c>%AppData%/HexIDE</c>.</summary>
    public UserTranslationsService() : this(DefaultDir())
    {
    }

    /// <summary>Test/seam constructor: overrides the storage directory, and the language-pack areas read.</summary>
    internal UserTranslationsService(string dir, LanguagePackAreas? areas = null)
    {
        _dir = dir;
        _areas = areas ?? LanguagePackAreas.Current;
        try
        {
            if (!_recoveryMode)
                Directory.CreateDirectory(_dir);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "UserTranslationsService: Failed to create translations dir {Dir}", _dir);
        }

        _ideCanonical = LoadCanonical();
        _canonicalStrings = _ideCanonical;
        LoadAll();
    }

    private Dictionary<string, string> Canonical
    {
        get
        {
            var version = _areas.Version;
            if (version != _canonicalVersion)
            {
                var canonical = new Dictionary<string, string>(_ideCanonical);
                _areas.AddEnglish(canonical);
                _canonicalStrings = canonical;
                _canonicalVersion = version;
                Place();
            }

            return _canonicalStrings;
        }
    }

    /// <summary>
    /// Moves each kept override whose key is now held, with its placeholders intact, into the overrides applied, and each
    /// applied one whose key is no longer held into those kept. Changes nothing on disk: the file holds both.
    /// </summary>
    private void Place()
    {
        var canonical = _canonicalStrings;
        foreach (var id in _byId.Keys.Concat(_kept.Keys).Distinct().ToList())
        {
            var placed = _byId.GetValueOrDefault(id);
            var kept = _kept.GetValueOrDefault(id);
            foreach (var (key, value) in kept?.ToList() ?? [])
                if (canonical.ContainsKey(key) && PreservesPlaceholders(canonical, key, value))
                {
                    kept!.Remove(key);
                    (placed ??= _byId[id] = new Dictionary<string, string>(StringComparer.Ordinal))[key] = value;
                }

            foreach (var (key, value) in placed?.ToList() ?? [])
                if (!canonical.ContainsKey(key))
                {
                    placed!.Remove(key);
                    (kept ??= _kept[id] = new Dictionary<string, string>(StringComparer.Ordinal))[key] = value;
                }

            if (placed is { Count: 0 })
                _byId.Remove(id);
            if (kept is { Count: 0 })
                _kept.Remove(id);
        }
    }

    private static string DefaultDir()
    {
        return UserDataPath.For("translations");
    }

    public void LoadAll()
    {
        if (_recoveryMode)
            return;
        _byId.Clear();
        _kept.Clear();
        var canonical = Canonical;

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
                var kept = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (key, value) in strings)
                {
                    if (string.IsNullOrWhiteSpace(value)) continue;         // empty/whitespace — drop
                    if (!canonical.ContainsKey(key))                        // no area holds it now — keep, unapplied
                    {
                        kept[key] = value;
                        continue;
                    }
                    if (!PreservesPlaceholders(canonical, key, value)) continue; // placeholder-breaker — drop
                    validated[key] = value;
                }

                if (validated.Count > 0)
                    _byId[id] = validated;
                if (kept.Count > 0)
                    _kept[id] = kept;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "UserTranslationsService: Failed to load override file {File}", file);
            }
        }
    }

    public IReadOnlyDictionary<string, string> GetOverrides(string id)
    {
        _ = Canonical;
        return _byId.TryGetValue(id, out var map) ? map : EmptyMap;
    }

    public bool SetOverride(string id, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ClearOverride(id, key);
            return true;
        }

        if (!PreservesPlaceholders(key, value))
            return false;

        _kept.GetValueOrDefault(id)?.Remove(key);
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
        _ = Canonical;
        if (!_byId.TryGetValue(id, out var map) || !map.Remove(key))
            return;

        if (map.Count == 0)
            _byId.Remove(id);
        // The file goes only when nothing is left in it, kept overrides included.
        if (map.Count == 0 && !_kept.ContainsKey(id))
            DeleteFile(id);
        else
            Persist(id);

        OverridesChanged?.Invoke(id);
    }

    public void ResetAll(string id)
    {
        _ = Canonical;
        var existed = _byId.Remove(id);
        // Resetting removes what the Translation Editor shows. Overrides of keys no area holds now aren't shown, and stay.
        if (_kept.ContainsKey(id))
            Persist(id);
        else
            DeleteFile(id);
        if (existed)
            OverridesChanged?.Invoke(id);
    }

    public IReadOnlyCollection<string> OverriddenIds => _byId.Keys;

    public bool PreservesPlaceholders(string canonicalKey, string candidate) =>
        PreservesPlaceholders(Canonical, canonicalKey, candidate);

    internal static bool PreservesPlaceholders(IReadOnlyDictionary<string, string> canonicalStrings,
        string canonicalKey, string candidate)
    {
        var candSet = IndexSet(candidate);

        // A key with a canonical entry must keep exactly the same {N} index set; a key with none imposes
        // no index requirement (but still must be a well-formed format string — checked below).
        if (canonicalStrings.TryGetValue(canonicalKey, out var canonical) && !IndexSet(canonical).SetEquals(candSet))
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
        if (_recoveryMode) return;
        var placed = _byId.GetValueOrDefault(id);
        var kept = _kept.GetValueOrDefault(id);
        if (placed is null && kept is null) return;
        try
        {
            Directory.CreateDirectory(_dir);
            var strings = new Dictionary<string, string>(kept ?? EmptyMap, StringComparer.Ordinal);
            foreach (var (key, value) in placed ?? EmptyMap)
                strings[key] = value;
            var record = new LanguagePackRecord(id, null, strings);
            var json = JsonSerializer.Serialize(record, LocalizationJsonContext.Default.LanguagePackRecord);
            AtomicFileWriter.WriteRecoverableText(PathFor(id), json);
            Log.Debug("UserTranslationsService: Wrote overrides for '{Id}'", id);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "UserTranslationsService: Failed to write overrides for '{Id}'", id);
        }
    }

    private void DeleteFile(string id)
    {
        if (_recoveryMode) return;
        try
        {
            var path = PathFor(id);
            if (File.Exists(path))
                AtomicFileWriter.DeleteRecoverable(path);
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
