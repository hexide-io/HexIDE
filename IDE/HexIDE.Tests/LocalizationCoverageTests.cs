using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using HexIDE.Localization;
using HexIDE.Runtime.Components;

namespace HexIDE.Tests;

/// <summary>
/// Source-level coverage: every <c>{DynamicResource Str.*}</c> referenced in HexIDE AXAML must have a
/// key in en.json; literal C# GetString keys must also exist, or the raw key renders at runtime.
/// This is the backstop for the area-by-area string extraction (no blank chrome can ship).
/// </summary>
public class LocalizationCoverageTests
{
    [Fact]
    public void PackTopLevelContainsOnlyLoaderFields()
    {
        var fields = LocalizationJsonContext.Default.LanguagePackRecord.Properties.Select(p => p.Name).ToArray();
        var unexpected = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(FindHexIdeProjectDir(), "Localization", "Packs"), "*.json"))
        {
            using var pack = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var property in pack.RootElement.EnumerateObject())
                if (!fields.Contains(property.Name)) unexpected.Add($"{Path.GetFileName(file)}: {property.Name}");
        }
        unexpected.Should().BeEmpty("only the fields understood by the language pack loader belong at the top level");
    }

    [Fact]
    public void MenuLabels_DoNotEmbedParenthesizedGestures()
    {
        var packsDir = Path.Combine(FindHexIdeProjectDir(), "Localization", "Packs");
        var gesture = new Regex(@"\((?:(?:(?:Ctrl|Control|Alt|Shift|Cmd|Command|Meta)\+)+[^()]+|F[0-9]{1,2})\)");
        var embedded = new List<string>();

        foreach (var file in Directory.EnumerateFiles(packsDir, "*.json"))
        {
            var pack = JsonSerializer.Deserialize(File.ReadAllText(file),
                LocalizationJsonContext.Default.LanguagePackRecord);
            foreach (var (key, value) in pack!.Strings!)
            {
                if (key.StartsWith("Str.Menu.", StringComparison.Ordinal) && gesture.IsMatch(value))
                    embedded.Add($"{Path.GetFileName(file)}: {key} = {value}");
            }
        }

        embedded.Should().BeEmpty("menu shortcuts come from the live keymap, not localized labels");
    }

    [AvaloniaFact]
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
    public void EveryCSharpGetStringLiteralKey_ExistsInEnglishPack()
    {
        var hexIdeDir = FindHexIdeProjectDir();
        var enKeys = LoadEnglishKeys(Path.Combine(hexIdeDir, "Localization", "Packs", "en.json"));
        var missing = new SortedSet<string>();
        foreach (var file in Directory.EnumerateFiles(hexIdeDir, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !Path.GetRelativePath(hexIdeDir, path).Split(Path.DirectorySeparatorChar)
                         .Any(part => part is "bin" or "obj")))
        {
            foreach (var key in CSharpGetStringLiteralKeys(File.ReadAllText(file)))
                if (!enKeys.Contains(key))
                    missing.Add($"{key}  ({Path.GetRelativePath(hexIdeDir, file)})");
        }
        missing.Should().BeEmpty("every literal Str.* key passed to GetString in C# must exist in en.json");
    }

    private static IEnumerable<string> CSharpGetStringLiteralKeys(string source)
    {
        // Computed and interpolated arguments are deliberately excluded; no constant evaluation or binding.
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = call.Expression switch
            {
                MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                MemberBindingExpressionSyntax member => member.Name.Identifier.ValueText,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                _ => null
            };
            if (name == "GetString" && call.ArgumentList.Arguments.Count == 1 &&
                call.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal &&
                literal.Kind() == SyntaxKind.StringLiteralExpression &&
                literal.Token.ValueText.StartsWith("Str.", StringComparison.Ordinal))
                yield return literal.Token.ValueText;
        }
    }

    [Theory]
    [InlineData("loc.GetString(\"Str.Missing\")")]
    [InlineData("loc?.GetString(\"Str.Missing\")")]
    [InlineData("GetString(\"Str.Missing\")")]
    [InlineData("loc.GetString(@\"Str.Missing\")")]
    [InlineData("loc.GetString(\"\"\"Str.Missing\"\"\")")]
    [InlineData("loc.GetString(\"Str.\\u004dissing\")")]
    public void CSharpGuard_FindsLiteralKeys(string expression)
        => CSharpGetStringLiteralKeys("class C { object M() => " + expression + "; }")
            .Should().Equal("Str.Missing");

    [Fact]
    public void CSharpGuard_ExcludesCommentsStringsAndComputedArguments()
    {
        CSharpGetStringLiteralKeys(""""
            class C {
                // loc.GetString("Str.Comment")
                string example = "loc.GetString(\"Str.Example\")";
                void M() {
                    loc.GetString(key);
                    loc.GetString($"Str.{name}");
                    loc.GetString($"Str.Interpolated");
                    loc.GetString("Str." + name);
                    loc.GetString("Other.Key");
                    json.GetString();
                    loc.Other("Str.Other");
                }
            }
            """").Should().BeEmpty();
    }

    [AvaloniaFact]
    public void EveryVBProperty_HasAPropDescKey()
    {
        // The property grid localizes descriptions via Str.PropDesc.{name}; every VB6 property must
        // have one (else GetPropertyDescription returns null and the grid silently falls back).
        _ = VBProperties.NameProperty; // force static init -> populate PropertiesByName

        var loc = new LocalizationService();
        loc.Apply("en");

        // PropertyCategory.Internal properties never appear in the property grid — Menu.SubItems is the
        // structural child list, not something a user edits — so a localized description would be a string
        // translated into 27 packs that nothing can ever display. The rule is about what the grid shows.
        var missing = VBProperties.PropertiesByName
            .Where(kv => kv.Value.All(p => p.Category != PropertyCategory.Internal))
            .Select(kv => kv.Key)
            .Where(name => loc.GetPropertyDescription(name) is null)
            .ToList();

        missing.Should().BeEmpty();
    }

    [AvaloniaFact]
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
