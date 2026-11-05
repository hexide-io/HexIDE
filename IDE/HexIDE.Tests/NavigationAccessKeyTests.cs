using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using HexIDE.Localization;

namespace HexIDE.Tests;

public class NavigationAccessKeyTests
{
    public static IEnumerable<object[]> LatinMenus() => LanguageManifest.Packs
        .Where(p => p.Id is not ("ar" or "bg" or "el" or "he" or "ja" or "ko" or "ru" or "th" or "uk" or "zh-Hans" or "zh-Hant"))
        .SelectMany(p => new[] { new object[] { p.Id, false }, new object[] { p.Id, true } });

    public static IEnumerable<object[]> NewNavigationEntries() => LatinMenus()
        .SelectMany(menu => new[] { new object[] { menu[0], menu[1], "Implementation" },
            new object[] { menu[0], menu[1], "TypeDefinition" } });

    [Theory]
    [MemberData(nameof(NewNavigationEntries))]
    public void NewNavigationAccessKeyIsUniqueWhenLabelHasAFreeLetter(string packId, bool viewMenu, string entry)
    {
        var strings = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(PacksDirectory(), packId + ".json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!;
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

    private static char? AccessKey(string label)
    {
        var index = label.IndexOf('_');
        return index >= 0 && index < label.Length - 1 ? char.ToUpperInvariant(label[index + 1]) : null;
    }

    [Theory]
    [MemberData(nameof(LatinMenus))]
    public void NavigationEntriesHaveDistinctAccessKeys(string packId, bool viewMenu)
    {
        var strings = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(PacksDirectory(), packId + ".json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!;
        var keys = viewMenu
            ? new[] { "Str.Menu.View.Definition", "Str.Menu.View.Implementation", "Str.Menu.View.TypeDefinition", "Str.Menu.View.Declaration" }
            : new[] { "Str.CodeEditor.Context.GoToDefinition", "Str.CodeEditor.Context.GoToImplementation", "Str.CodeEditor.Context.GoToTypeDefinition", "Str.CodeEditor.Context.GoToDeclaration" };
        var accessKeys = new List<char>();
        foreach (var key in keys)
        {
            strings.Should().ContainKey(key);
            var label = strings[key];
            label.Count(c => c == '_').Should().Be(1, $"{packId}: {key}");
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
        var strings = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(PacksDirectory(), packId + ".json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!;
        strings["Str.CodeEditor.Context.GoToImplementation"].Replace("_", "").Should().Be(implementation);
        strings["Str.CodeEditor.Context.GoToTypeDefinition"].Replace("_", "").Should().Be(typeDefinition);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("bg")]
    [InlineData("el")]
    [InlineData("he")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("ru")]
    [InlineData("th")]
    [InlineData("uk")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    public void NonLatinNewLabelsHaveNoAccessKeyMarkers(string packId)
    {
        var strings = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(PacksDirectory(), packId + ".json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!;
        foreach (var key in new[] { "Str.CodeEditor.Context.GoToImplementation", "Str.CodeEditor.Context.GoToTypeDefinition",
                     "Str.Menu.View.Implementation", "Str.Menu.View.TypeDefinition" })
        {
            strings.Should().ContainKey(key);
            strings[key].Should().NotContain("_").And.NotContain("&");
        }
    }

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
        var strings = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(PacksDirectory(), packId + ".json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!;
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
