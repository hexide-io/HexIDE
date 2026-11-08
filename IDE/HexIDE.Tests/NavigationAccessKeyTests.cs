using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using HexIDE.Localization;

namespace HexIDE.Tests;

public class NavigationAccessKeyTests
{
    public static IEnumerable<object[]> LatinMenus() => LanguageManifest.Packs
        .Where(p => p.Id is not ("ar" or "bg" or "el" or "he" or "ja" or "ko" or "ru" or "th" or "uk" or "zh-Hans" or "zh-Hant"))
        .Select(p => p.Id).Concat(["en-GB", "es-MX", "fr-CA", "pt-BR"])
        .SelectMany(id => new[] { new object[] { id, false }, new object[] { id, true } });

    public static IEnumerable<object[]> NewNavigationEntries() => LatinMenus()
        .SelectMany(menu => new[] { new object[] { menu[0], menu[1], "Implementation" },
            new object[] { menu[0], menu[1], "TypeDefinition" } });

    [Theory]
    [MemberData(nameof(NewNavigationEntries))]
    public void NewNavigationAccessKeyIsUniqueWhenLabelHasAFreeLetter(string packId, bool viewMenu, string entry)
    {
        var strings = PackStrings(packId);
        var root = Directory.GetParent(Directory.GetParent(PacksDirectory())!.FullName)!.FullName;
        var document = XDocument.Load(Path.Combine(root, viewMenu ? "MainView.axaml" : "Forms/Views/CodeEditorView.axaml"));
        var key = viewMenu ? "Str.Menu.View." + entry : "Str.CodeEditor.Context.GoTo" + entry;
        var item = document.Descendants().Single(e => (string?)e.Attribute("Header") == "{DynamicResource " + key + "}");
        var otherKeys = item.Parent!.Elements()
            .Where(e => e != item && e.Name.LocalName == "MenuItem")
            .Select(e => (string?)e.Attribute("Header"))
            .Where(h => h is not null && h.StartsWith("{DynamicResource Str.", StringComparison.Ordinal))
            .Select(h => strings[h![17..^1]])
            .Select(AccessKey).Where(c => c is not null).ToArray();
        var label = strings[key];
        if (label.Any(c => char.IsLetter(c) && !otherKeys.Contains(char.ToUpperInvariant(c))))
            otherKeys.Should().NotContain(AccessKey(label), $"{packId}: {key} has a free letter in its menu, counting disabled entries too (#3110)");
    }

    // VB6's own mnemonics were not measured; this guards HexIDE's collision policy (#3121).
    [Theory]
    [MemberData(nameof(LatinMenus))]
    public void MenuAccessKeysAreUniqueAndMissingOnlyWhenNoLetterIsFree(string packId, bool viewMenu)
    {
        var strings = PackStrings(packId);
        var root = Directory.GetParent(Directory.GetParent(PacksDirectory())!.FullName)!.FullName;
        var document = XDocument.Load(Path.Combine(root, viewMenu ? "MainView.axaml" : "Forms/Views/CodeEditorView.axaml"));
        var anchor = viewMenu ? "Str.Menu.View.Code" : "Str.CodeEditor.Context.Cut";
        var item = document.Descendants().Single(e => (string?)e.Attribute("Header") == "{DynamicResource " + anchor + "}");
        foreach (var menu in item.Parent!.DescendantsAndSelf().Where(e => e.Elements().Any(c => c.Name.LocalName == "MenuItem")))
        {
            var labels = menu.Elements().Where(e => e.Name.LocalName == "MenuItem")
                .Select(e => (string?)e.Attribute("Header"))
                .Where(h => h is not null && h.StartsWith("{DynamicResource Str.", StringComparison.Ordinal))
                .Select(h => strings[h![17..^1]]).ToArray();
            var used = labels.Select(AccessKey).ToHashSet();
            var seen = new HashSet<char>();
            foreach (var label in labels)
            {
                if (AccessKey(label) is { } key)
                    seen.Add(key).Should().BeTrue($"{packId}: {label} must have a unique key, including disabled siblings (#3121)");
                else
                    label.Where(char.IsLetter).Select(char.ToUpperInvariant).Should().OnlyContain(c => used.Contains(c),
                        $"{packId}: {label} may omit its key only when no letter is free (#3121)");
            }
        }
    }

    [Theory]
    [InlineData("en", "Str.Menu.View.ColorPalette", "Colo_r Palette")]
    [InlineData("en", "Str.CodeEditor.Context.ListPropertiesMethods", "List Properties/Met_hods")]
    [InlineData("sl", "Str.Menu.View.LastPosition", "_Zadnji položaj")]
    [InlineData("pt", "Str.Menu.View.WatchWindow", "_Janela de Inspeção")]
    public void LaterClashingLabelsUseFirstFreeLetterPreferringWordStarts(string packId, string key, string expected)
    {
        var strings = PackStrings(packId);
        strings[key].Should().Be(expected);
    }

    private static char? AccessKey(string label)
    {
        var index = label.IndexOf('_');
        return index >= 0 && index < label.Length - 1 ? char.ToUpperInvariant(label[index + 1]) : null;
    }

    [Theory]
    [MemberData(nameof(LatinMenus))]
    public void NavigationEntriesHaveDistinctAccessKeys(string packId, bool viewMenu)
    {
        var strings = PackStrings(packId);
        var keys = viewMenu
            ? new[] { "Str.Menu.View.Definition", "Str.Menu.View.Implementation", "Str.Menu.View.TypeDefinition", "Str.Menu.View.Declaration" }
            : new[] { "Str.CodeEditor.Context.GoToDefinition", "Str.CodeEditor.Context.GoToImplementation", "Str.CodeEditor.Context.GoToTypeDefinition", "Str.CodeEditor.Context.GoToDeclaration" };
        var accessKeys = new List<char>();
        foreach (var key in keys)
        {
            strings.Should().ContainKey(key);
            var label = strings[key];
            label.Count(c => c == '_').Should().BeLessThanOrEqualTo(1, $"{packId}: {key}");
            if (AccessKey(label) is null) continue; // The menu-wide guard checks that no letter is free.
            var index = label.IndexOf('_');
            index.Should().BeLessThan(label.Length - 1);
            accessKeys.Add(char.ToUpperInvariant(label[index + 1]));
        }
        accessKeys.Should().OnlyHaveUniqueItems($"{packId}: navigation entries in each menu have distinct access keys (#3096)");
    }

    [Theory]
    [InlineData("de", "Gehe zu Implementierung", "Gehe zu Typdefinition")]
    [InlineData("he", "עבור למימוש", "עבור להגדרת הטיפוס")]
    [InlineData("id", "Buka Implementasi", "Buka Definisi Tipe")]
    [InlineData("lt", "Pereiti prie realizacijos", "Pereiti prie tipo apibrėžties")]
    [InlineData("th", "ไปที่การนำไปใช้", "ไปที่นิยามชนิด")]
    [InlineData("pt", "Ir para a Implementação", "Ir para a Definição do Tipo")]
    public void NewLabelsKeepTheExistingNavigationPhrasing(string packId, string implementation, string typeDefinition)
    {
        var strings = PackStrings(packId);
        strings["Str.CodeEditor.Context.GoToImplementation"].Replace("_", "").Should().Be(implementation);
        strings["Str.CodeEditor.Context.GoToTypeDefinition"].Replace("_", "").Should().Be(typeDefinition);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("bg")]
    [InlineData("el")]
    [InlineData("he")]
    [InlineData("ko")]
    [InlineData("ru")]
    [InlineData("th")]
    [InlineData("uk")]
    public void NonLatinNewLabelsHaveNoAccessKeyMarkers(string packId)
    {
        // Chinese and Japanese are the exception: they append the English access key, as (_F) (#41).
        var strings = PackStrings(packId);
        foreach (var key in NavigationKeys)
        {
            strings.Should().ContainKey(key);
            strings[key].Should().NotContain("_").And.NotContain("&");
        }
    }

    [Theory]
    [InlineData("ja")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    public void CjkNewLabelsAppendTheEnglishAccessKey(string packId)
    {
        var english = PackStrings("en");
        var strings = PackStrings(packId);
        foreach (var key in NavigationKeys)
        {
            var source = english[key];
            var marker = source.IndexOf('_');
            marker.Should().BeGreaterThanOrEqualTo(0, $"en's {key} carries an access key");
            strings[key].Should().EndWith($"(_{char.ToUpperInvariant(source[marker + 1])})", $"{packId}: {key}")
                .And.NotContain("&");
            strings[key].Count(c => c == '_').Should().Be(1, $"{packId}: {key}");
        }
    }

    private static readonly string[] NavigationKeys =
    [
        "Str.CodeEditor.Context.GoToImplementation", "Str.CodeEditor.Context.GoToTypeDefinition",
        "Str.Menu.View.Implementation", "Str.Menu.View.TypeDefinition",
    ];

    [Fact]
    public void ViewMenuUsesItsOwnNavigationLabels()
    {
        var strings = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(PacksDirectory(), "en.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!;
        strings.Should().ContainKeys("Str.Menu.View.Implementation", "Str.Menu.View.TypeDefinition");
        strings["Str.Menu.View.Implementation"].Replace("_", "").Should().Be("Implementation");
        strings["Str.Menu.View.TypeDefinition"].Replace("_", "").Should().Be("Type Definition");
        var root = Directory.GetParent(Directory.GetParent(PacksDirectory())!.FullName)!.FullName;
        var view = File.ReadAllText(Path.Combine(root, "MainView.axaml"));
        foreach (var command in new[] { "Implementation", "TypeDefinition" })
            view.Should().Contain($"Header=\"{{DynamicResource Str.Menu.View.{command}}}\" Command=\"{{x:Static vb:ApplicationCommands.GoTo{command}Command}}\"");
    }

    [Theory]
    [MemberData(nameof(LatinMenus))]
    public void CallHierarchyHasAnUnusedMenuAccessKey(string packId, bool editMenu)
    {
        var strings = PackStrings(packId);
        var key = editMenu ? "Str.Menu.Edit.CallHierarchy" : "Str.CodeEditor.Context.CallHierarchy";
        strings.Should().ContainKey(key);
        var label = strings[key];
        label.Count(c => c == '_').Should().Be(1);
        var mnemonic = char.ToUpperInvariant(label[label.IndexOf('_') + 1]);
        var root = Directory.GetParent(Directory.GetParent(PacksDirectory())!.FullName)!.FullName;
        XNamespace ns = "https://github.com/avaloniaui";
        var xml = XDocument.Load(Path.Combine(root, editMenu ? "MainView.axaml" : "Forms/Views/CodeEditorView.axaml"));
        var menu = editMenu
            ? xml.Descendants(ns + "MenuItem").Single(e => (string?)e.Attribute("Header") == "{DynamicResource Str.Menu.Edit}")
            : xml.Descendants(ns + "MenuFlyout").Single();
        menu.Elements(ns + "MenuItem").Single(e => (string?)e.Attribute("Command") ==
            "{x:Static vb:ApplicationCommands.CallHierarchyCommand}").Attribute("Header")!.Value
            .Should().Be("{DynamicResource " + key + "}");
        var menuKeys = menu.Elements(ns + "MenuItem").Select(e => (string?)e.Attribute("Header"))
            .Where(h => h?.StartsWith("{DynamicResource Str.", StringComparison.Ordinal) == true)
            .Select(h => h![17..^1]).Where(k => k != key).ToList();
        if (editMenu) menuKeys.AddRange(["Str.Menu.Edit.Undo", "Str.Menu.Edit.Redo"]);
        var used = menuKeys.Select(k => strings[k]).Where(v => v.Contains('_'))
            .Select(v => char.ToUpperInvariant(v[v.IndexOf('_') + 1]));
        used.Should().NotContain(mnemonic, $"{packId}: Call Hierarchy should use a free menu letter");
    }

    // These menus use their own collision guard instead of English marker-count parity.
    internal static readonly HashSet<string> CollisionPolicyKeys = LatinMenuKeys().ToHashSet();

    private static IEnumerable<string> LatinMenuKeys()
    {
        var root = Directory.GetParent(Directory.GetParent(PacksDirectory())!.FullName)!.FullName;
        foreach (var viewMenu in new[] { true, false })
        {
            var document = XDocument.Load(Path.Combine(root, viewMenu ? "MainView.axaml" : "Forms/Views/CodeEditorView.axaml"));
            var anchor = viewMenu ? "Str.Menu.View.Code" : "Str.CodeEditor.Context.Cut";
            var item = document.Descendants().Single(e => (string?)e.Attribute("Header") == "{DynamicResource " + anchor + "}");
            foreach (var header in item.Parent!.Descendants().Where(e => e.Name.LocalName == "MenuItem")
                         .Select(e => (string?)e.Attribute("Header"))
                         .Where(h => h?.StartsWith("{DynamicResource Str.", StringComparison.Ordinal) == true))
                yield return header![17..^1];
        }
    }

    private static Dictionary<string, string> PackStrings(string packId)
    {
        var strings = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(PacksDirectory(), packId + ".json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!;
        if (packId is not ("en-GB" or "es-MX" or "fr-CA" or "pt-BR")) return strings;
        var neutral = PackStrings(packId.Split('-')[0]);
        foreach (var (key, value) in strings) neutral[key] = value;
        return neutral;
    }

    private static string PacksDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var packs = Path.Combine(dir.FullName, "IDE", "HexIDE", "Localization", "Packs");
            if (Directory.Exists(packs)) return packs;
        }
        throw new DirectoryNotFoundException("Could not locate localization packs");
    }
}
