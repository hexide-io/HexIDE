using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using HexIDE.Localization;
using HexIDE.Runtime.Components;

namespace HexIDE.Tests;

/// <summary>
/// Source-level coverage: every <c>{DynamicResource Str.*}</c> referenced in HexIDE AXAML must have a
/// key in en.json — otherwise the binding resolves to null and the control renders blank at runtime.
/// This is the backstop for the area-by-area string extraction (no blank chrome can ship).
/// </summary>
public class LocalizationCoverageTests
{
    [Fact]
    public void EveryAxamlStrKey_ExistsInEnglishPack()
    {
        var hexIdeDir = FindHexIdeProjectDir();
        var enKeys = LoadEnglishKeys(Path.Combine(hexIdeDir, "Localization", "Packs", "en.json"));

        var pattern = new Regex(@"\{DynamicResource\s+(Str\.[A-Za-z0-9_.]+)\}", RegexOptions.Compiled);
        var missing = new SortedSet<string>();

        foreach (var file in Directory.EnumerateFiles(hexIdeDir, "*.axaml", SearchOption.AllDirectories))
        {
            foreach (Match m in pattern.Matches(File.ReadAllText(file)))
            {
                var key = m.Groups[1].Value;
                if (!enKeys.Contains(key))
                    missing.Add($"{key}  ({Path.GetFileName(file)})");
            }
        }

        missing.Should().BeEmpty("every Str.* key used in AXAML must exist in en.json");
    }

    [Fact]
    public void EveryVBProperty_HasAPropDescKey()
    {
        // The property grid localizes descriptions via Str.PropDesc.{name}; every VB6 property must
        // have one (else GetPropertyDescription returns null and the grid silently falls back).
        AvaloniaTestSetup.EnsureInitialized();
        _ = VBProperties.NameProperty; // force static init -> populate PropertiesByName

        var loc = new LocalizationService();
        loc.Apply("en");

        var missing = VBProperties.PropertiesByName.Keys
            .Where(name => loc.GetPropertyDescription(name) is null)
            .ToList();

        missing.Should().BeEmpty();
    }

    [Fact]
    public void RegionPacks_OnlyOverrideCanonicalKeys()
    {
        // A region/language pack must only override keys that exist in the canonical en pack —
        // an orphan key would never be reachable and signals a typo or a stale key.
        var packsDir = Path.Combine(FindHexIdeProjectDir(), "Localization", "Packs");
        var canonical = LoadEnglishKeys(Path.Combine(packsDir, "en.json"));

        foreach (var file in Directory.EnumerateFiles(packsDir, "*.json"))
        {
            if (Path.GetFileName(file).Equals("en.json", StringComparison.OrdinalIgnoreCase))
                continue;

            var orphans = LoadEnglishKeys(file).Except(canonical).ToList();
            orphans.Should().BeEmpty($"{Path.GetFileName(file)} must only override keys present in en.json");
        }
    }

    private static HashSet<string> LoadEnglishKeys(string enJsonPath)
    {
        var pack = JsonSerializer.Deserialize(File.ReadAllText(enJsonPath),
            LocalizationJsonContext.Default.LanguagePackRecord);
        return [.. pack?.Strings?.Keys ?? Enumerable.Empty<string>()];
    }

    private static string FindHexIdeProjectDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "IDE", "HexIDE", "HexIDE.csproj")))
                return Path.Combine(dir.FullName, "IDE", "HexIDE");
        }
        throw new DirectoryNotFoundException("Could not locate IDE/HexIDE from " + AppContext.BaseDirectory);
    }
}
