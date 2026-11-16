using System.IO;
using System.Xml.Linq;
using HexIDE.Localization;
using HexIDE.Menus;

namespace HexIDE.Tests;

public class DocumentPaneAccessKeyTests
{
    public static IEnumerable<object[]> Packs() => LanguageManifest.Packs.Select(p => p.Id)
        .Concat(["en-GB", "es-MX", "fr-CA", "pt-BR"]).Select(id => new object[] { id });

    [Theory]
    [MemberData(nameof(Packs))]
    public void Document_pane_access_keys_leave_the_main_menu_available(string pack)
    {
        var root = RepositoryRoot();
        var loc = new LocalizationService();
        var menuKeys = MainMenuDeclaration.Load().Select(m => AccessKey(loc.GetStringFrom(pack, m.LabelKey!)))
            .Where(k => k is not null).ToHashSet();
        // A pack whose script omits access-key markers has none on its menu bar either, so it owes Restart none.
        if (menuKeys.Count > 0)
            AccessKey(loc.GetStringFrom(pack, "Str.Tool.LanguageServers.Restart")).Should().NotBeNull(
                $"{pack}: Restart retains its access key (#4040)");
        foreach (var pane in new[] { "ObjectBrowser", "ProtocolInspector", "Diagnostics", "LanguageServers", "TranslationEditor" })
        {
            var keys = Directory.EnumerateFiles(Path.Combine(root, "IDE", "HexIDE", "Tools", pane), "*.axaml")
                .SelectMany(f => XDocument.Load(f).Descendants())
                .Where(e => e.Name.LocalName is "AccessText" or "Label" or "Button" or "CheckBox" or "ToggleButton" or "RadioButton")
                .SelectMany(e => e.Attributes().Where(a => a.Name.LocalName is "Content" or "Text" or "Header"))
                .Select(a => a.Value).Where(v => v.StartsWith("{DynamicResource Str.", StringComparison.Ordinal))
                .Select(v => v[17..^1]).Distinct().ToArray();
            var used = new HashSet<char>();
            foreach (var key in keys)
            {
                if (AccessKey(loc.GetStringFrom(pack, key)) is not { } access) continue;
                menuKeys.Should().NotContain(access, $"{pack}: {pane}'s {key} must leave the main menu available (#4040)");
                used.Add(access).Should().BeTrue($"{pack}: {pane}'s {key} must have a unique access key");
            }
        }
    }

    private static char? AccessKey(string caption)
    {
        for (var i = 0; i < caption.Length - 1; i++)
        {
            if (caption[i] != '_') continue;
            if (caption[i + 1] == '_') { i++; continue; }
            return char.ToUpperInvariant(caption[i + 1]);
        }
        return null;
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "IDE", "HexIDE", "Localization", "Packs"))) return dir.FullName;
        throw new DirectoryNotFoundException("Could not locate localization packs");
    }
}
