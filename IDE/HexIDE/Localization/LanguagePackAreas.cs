using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Avalonia.Platform;
using Serilog;

namespace HexIDE.Localization;

/// <summary>
/// The language-pack areas registered beside the IDE's own packs (#5394). An area is a folder of <c>{id}.json</c> files
/// in the IDE's pack format, whose keys only its owner holds. The localization service, the user's translations and the
/// Translation Editor all read the IDE's canonical English together with every area's.
/// </summary>
/// <remarks>
/// <para>
/// <b>An area is checked whole before it's added,</b> and one refused key refuses all of it. A key is refused when:
/// <list type="bullet">
/// <item>it doesn't start <c>Str.</c>, since every string the packs hold becomes an application resource;</item>
/// <item>the caller's <c>allowed</c> check, the owner's grant, doesn't cover it;</item>
/// <item>the IDE's canonical pack defines it;</item>
/// <item>another area holds it;</item>
/// <item>its English is blank;</item>
/// <item>a language file holds a key the area's own <c>en.json</c> lacks;</item>
/// <item>a translation's placeholders differ from its English.</item>
/// </list>
/// Each refusal is logged naming both owners.
/// </para>
/// <para>
/// <b>An area adds, never replaces.</b> Its keys are disjoint from the IDE's and every other area's, so the order the
/// layers merge in never decides between two owners. Text direction stays the IDE pack's.
/// </para>
/// <para>
/// <b>Who may register is decided before this,</b> by the grant the add-in host checks. Nothing here reads a manifest.
/// </para>
/// </remarks>
internal sealed class LanguagePackAreas
{
    /// <summary>What the IDE's own keys are said to belong to, in a refusal.</summary>
    internal const string IdeOwner = "the IDE";

    /// <summary>The areas the running IDE reads. Every service built without its own reads these.</summary>
    public static LanguagePackAreas Current { get; } = new();

    private readonly IAssetLoader _assets;
    private readonly Lazy<IReadOnlyDictionary<string, string>> _ideEnglish;
    private readonly object _gate = new();
    private ImmutableList<LanguagePackArea> _areas = ImmutableList<LanguagePackArea>.Empty;
    private int _version;

    /// <summary>Raised after an area is added or taken out, on the thread that did it and outside the registry's lock.</summary>
    public event Action? Changed;

    public LanguagePackAreas() : this(null, null)
    {
    }

    /// <summary>A registry checking against <paramref name="ideEnglish"/> in place of the IDE's shipped canonical pack.</summary>
    internal LanguagePackAreas(IReadOnlyDictionary<string, string>? ideEnglish, IAssetLoader? assets = null)
    {
        _assets = assets ?? new StandardAssetLoader();
        _ideEnglish = new Lazy<IReadOnlyDictionary<string, string>>(() => ideEnglish ?? LoadIdeEnglish(_assets));
    }

    /// <summary>The areas registered now, in the order they were.</summary>
    public IReadOnlyList<LanguagePackArea> Areas => Volatile.Read(ref _areas);

    /// <summary>Changes each time an area is added or taken out, so a reader can tell its copy is stale.</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>Adds every area's English to <paramref name="into"/>.</summary>
    public void AddEnglish(Dictionary<string, string> into)
    {
        foreach (var area in Areas)
            foreach (var (key, value) in area.English)
                into[key] = value;
    }

    /// <summary>Adds every area's text for <paramref name="id"/> to <paramref name="into"/>; English is not a translation.</summary>
    public void AddTranslations(string id, Dictionary<string, string> into)
    {
        foreach (var area in Areas)
            if (area.Translations.TryGetValue(id, out var strings))
                foreach (var (key, value) in strings)
                    into[key] = value;
    }

    /// <summary>Adds to <paramref name="into"/> the keys the areas' own files for <paramref name="id"/> hold.</summary>
    public void AddOwnKeys(string id, ISet<string> into)
    {
        foreach (var area in Areas)
            if (area.Translations.TryGetValue(id, out var strings))
                into.UnionWith(strings.Keys);
    }

    /// <summary>
    /// Reads the area in <paramref name="folder"/> for <paramref name="ownerTitle"/> (<paramref name="ownerId"/>) and adds it
    /// until the returned handle is disposed.
    /// </summary>
    /// <param name="allowed">Whether the owner's grant covers a key. A key it refuses refuses the area.</param>
    /// <exception cref="ArgumentException">The folder has no readable <c>en.json</c>, or a file in it is not a pack.</exception>
    /// <exception cref="InvalidOperationException">A key is refused; the message names each, with its owner.</exception>
    public IDisposable Register(string ownerId, string ownerTitle, Uri folder, Func<string, bool> allowed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerTitle);
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(allowed);
        if (!folder.IsAbsoluteUri)
            throw new ArgumentException($"{ownerTitle} named {folder}, which is not an absolute address.", nameof(folder));
        if (!folder.AbsoluteUri.EndsWith('/'))
            folder = new Uri(folder.AbsoluteUri + "/");

        var english = Read(folder, "en", ownerTitle)
            ?? throw new ArgumentException($"{ownerTitle} named {folder}, which holds no en.json.", nameof(folder));
        var translations = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var id in LanguageIds())
            if (Read(folder, id, ownerTitle) is { } strings)
                translations[id] = strings;

        LanguagePackArea area;
        lock (_gate)
        {
            var refusals = Refusals(english, translations, allowed);
            if (refusals.Count > 0)
            {
                foreach (var refusal in refusals)
                    Log.Warning("Language packs: refused {Folder} from {Owner} ({OwnerId}): {Refusal}",
                        folder, ownerTitle, ownerId, refusal);
                throw new InvalidOperationException(
                    $"{ownerTitle} can't register the language packs in {folder}: " + string.Join("; ", refusals) + ".");
            }

            area = new LanguagePackArea(ownerId, ownerTitle, folder, english,
                translations.ToDictionary(pair => pair.Key, pair => Translated(pair.Value), StringComparer.Ordinal));
            _areas = _areas.Add(area);
            _version++;
        }

        Log.Information("Language packs: {Owner} ({OwnerId}) added {Folder} with {Count} key(s) in {Languages} language(s)",
            ownerTitle, ownerId, folder, english.Count, translations.Count + 1);
        Changed?.Invoke();
        return new Registration(this, area);
    }

    private List<string> Refusals(IReadOnlyDictionary<string, string> english,
        Dictionary<string, IReadOnlyDictionary<string, string>> translations, Func<string, bool> allowed)
    {
        var ide = _ideEnglish.Value;
        var refusals = new List<string>();
        foreach (var (key, value) in english)
        {
            if (!key.StartsWith("Str.", StringComparison.Ordinal))
                refusals.Add($"{key}: only Str.* keys are text");
            else if (!allowed(key))
                refusals.Add($"{key}: outside what its grant names");
            else if (ide.ContainsKey(key))
                refusals.Add($"{key}: already held by {IdeOwner}");
            else if (_areas.FirstOrDefault(other => other.English.ContainsKey(key)) is { } other)
                refusals.Add($"{key}: already registered by {other.OwnerTitle} ({other.OwnerId})");
            else if (string.IsNullOrWhiteSpace(value))
                refusals.Add($"{key}: its English is blank");
        }

        foreach (var (id, strings) in translations)
            foreach (var (key, value) in strings)
            {
                if (!english.ContainsKey(key))
                    refusals.Add($"{key} in {id}.json: not in the area's en.json");
                else if (!string.IsNullOrWhiteSpace(value)
                         && !UserTranslationsService.PreservesPlaceholders(english, key, value))
                    refusals.Add($"{key} in {id}.json: its placeholders differ from its English");
            }

        return refusals;
    }

    // A blank translation falls back to English, as a blank entry in the IDE's own packs does.
    private static IReadOnlyDictionary<string, string> Translated(IReadOnlyDictionary<string, string> strings) =>
        strings.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private void Remove(LanguagePackArea area)
    {
        lock (_gate)
        {
            if (!_areas.Contains(area))
                return;
            _areas = _areas.Remove(area);
            _version++;
        }

        Log.Information("Language packs: {Owner} ({OwnerId}) took out {Folder}", area.OwnerTitle, area.OwnerId, area.Folder);
        Changed?.Invoke();
    }

    /// <summary>Every id an area can hold a file for: each shipped language and each of its regions.</summary>
    private static IEnumerable<string> LanguageIds() =>
        LanguageManifest.Packs.Select(language => language.Id)
            .Concat(LanguageManifest.Regions.Values.SelectMany(regions => regions).Select(region => region.Id))
            .Where(id => id != "en")
            .Distinct(StringComparer.Ordinal);

    private IReadOnlyDictionary<string, string>? Read(Uri folder, string id, string ownerTitle)
    {
        var file = new Uri(folder, id + ".json");
        try
        {
            if (!_assets.Exists(file))
                return null;
            using var stream = _assets.Open(file);
            var record = JsonSerializer.Deserialize(stream, LocalizationJsonContext.Default.LanguagePackRecord);
            return record?.Strings ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or System.IO.IOException)
        {
            throw new ArgumentException($"{file} from {ownerTitle} is not a language pack: {ex.Message}", nameof(folder), ex);
        }
    }

    private static IReadOnlyDictionary<string, string> LoadIdeEnglish(IAssetLoader assets)
    {
        try
        {
            using var stream = assets.Open(new Uri("avares://HexIDE/Localization/Packs/en.json"));
            return JsonSerializer.Deserialize(stream, LocalizationJsonContext.Default.LanguagePackRecord)?.Strings
                ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            // Refusing nothing would let an area shadow any IDE key, so with no canonical pack every area is refused.
            Log.Error(ex, "Language packs: the IDE's canonical pack couldn't be read; no area can be registered");
            throw new InvalidOperationException("The IDE's canonical language pack couldn't be read.", ex);
        }
    }

    private sealed class Registration(LanguagePackAreas owner, LanguagePackArea area) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                owner.Remove(area);
        }
    }
}

/// <summary>One registered area: its owner, where it was read from, its English and its translations by language id.</summary>
internal sealed record LanguagePackArea(string OwnerId, string OwnerTitle, Uri Folder,
    IReadOnlyDictionary<string, string> English, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Translations);
