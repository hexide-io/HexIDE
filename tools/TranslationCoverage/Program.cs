// Translation-coverage report: how current is each language pack vs the canonical en pack?
//
// Run:  cd tools/TranslationCoverage && dotnet run
//
// Visibility only — ALWAYS exits 0, never fails a build. New feature keys land in en.json first
// (enforced by LocalizationCoverageTests); the translated packs inherit any missing key as English, so
// they silently drift until backfilled. This surfaces that drift: which full translations are missing
// which keys, so they can be batch-backfilled (see CLAUDE.md → Localization, language-packs spec).

using System.Text.Json;

static string FindPacksDir()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        var p = Path.Combine(dir.FullName, "IDE", "HexIDE", "Localization", "Packs");
        if (Directory.Exists(p)) return p;
    }
    throw new DirectoryNotFoundException("Could not locate IDE/HexIDE/Localization/Packs from " + AppContext.BaseDirectory);
}

static HashSet<string> Keys(string file)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(file));
    var set = new HashSet<string>();
    if (doc.RootElement.TryGetProperty("strings", out var s) && s.ValueKind == JsonValueKind.Object)
        foreach (var p in s.EnumerateObject()) set.Add(p.Name);
    return set;
}

// A "full translation" is a neutral pack (no region subtag), incl. the two Chinese script neutrals.
// Region packs are either empty (inherit their neutral) or thin variants (e.g. en-GB overrides only).
static bool IsNeutral(string id) => !id.Contains('-') || id is "zh-Hans" or "zh-Hant";

var packsDir = FindPacksDir();
var canonical = Keys(Path.Combine(packsDir, "en.json"));
int total = canonical.Count;
Console.WriteLine($"Translation coverage vs canonical en ({total} keys)\n");

var complete = new List<string>();
var stale = new List<(string id, int have, List<string> missing)>();
var variants = new List<(string id, int have)>();
int empty = 0;

foreach (var file in Directory.EnumerateFiles(packsDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
{
    var id = Path.GetFileNameWithoutExtension(file);
    if (id is "en" or "zz" or "zz-ZZ") continue;   // canonical + synthetic test fixtures

    var keys = Keys(file);
    if (keys.Count == 0) { empty++; continue; }

    var missing = canonical.Where(k => !keys.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
    if (IsNeutral(id))
    {
        if (missing.Count == 0) complete.Add(id);
        else stale.Add((id, keys.Count, missing));
    }
    else
    {
        variants.Add((id, keys.Count));   // non-empty region pack = intentional variant
    }
}

Console.WriteLine($"Full translations complete (100%): {complete.Count}"
    + (complete.Count > 0 ? "  [" + string.Join(", ", complete) + "]" : ""));
Console.WriteLine();

if (stale.Count == 0)
{
    Console.WriteLine("STALE full translations: none — every full translation is current. ✅\n");
}
else
{
    Console.WriteLine("STALE full translations (backfill the missing keys — see CLAUDE.md → Localization):");
    foreach (var (id, have, missing) in stale.OrderByDescending(p => p.missing.Count))
    {
        Console.WriteLine($"  {id,-10} {have}/{total}  {100.0 * have / total:F1}%   missing {missing.Count}:");
        foreach (var k in missing.Take(40)) Console.WriteLine($"      {k}");
        if (missing.Count > 40) Console.WriteLine($"      … +{missing.Count - 40} more");
    }
    Console.WriteLine();
}

if (variants.Count > 0)
{
    Console.WriteLine("Thin variants / partial region packs (intentional — override only what differs):");
    foreach (var (id, have) in variants.OrderBy(v => v.id, StringComparer.Ordinal))
        Console.WriteLine($"  {id,-10} {have}/{total}  {100.0 * have / total:F1}%");
    Console.WriteLine();
}

Console.WriteLine($"Empty region packs (inherit their neutral, by design): {empty}");
