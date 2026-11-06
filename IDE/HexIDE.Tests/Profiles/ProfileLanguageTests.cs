using System.Text.Json;
using HexIDE.Localization;
using HexIDE.Profiles;

namespace HexIDE.Tests.Profiles;

public sealed class ProfileLanguageTests : IDisposable
{
    private readonly string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "hexide-profile-language-" + Guid.NewGuid());

    public ProfileLanguageTests() => Directory.CreateDirectory(root);

    private ProfileSnapshot Profile(Dictionary<string, Dictionary<string, string>> locales)
    {
        var files = new Dictionary<string, string>();
        foreach (var (id, strings) in locales)
        {
            files[id] = id + ".json";
            File.WriteAllText(Path.Combine(root, files[id]), JsonSerializer.Serialize(
                new LanguagePackRecord(id, null, strings), LocalizationJsonContext.Default.LanguagePackRecord));
        }
        File.WriteAllText(Path.Combine(root, "profile.json"),
            "{\"schemaVersion\":1,\"id\":\"custom\",\"languageOverrides\":" + JsonSerializer.Serialize(files) + "}");
        return new ProfileResolver(root, root).Resolve(root)!;
    }

    [AvaloniaFact]
    public void SnapshotLayersNeutralAndRegionWithoutChangingLocaleInventory()
    {
        var profile = Profile(new()
        {
            ["fr"] = new() { ["Str.Menu.File"] = "Profile file", ["Str.Menu.Edit"] = "Profile edit" },
            ["fr-CA"] = new() { ["Str.Menu.File"] = "Regional file" },
        });
        File.WriteAllText(Path.Combine(root, "fr-CA.json"), "{}");
        var localization = new LocalizationService(profile);
        localization.Apply("fr-CA");
        localization.ActiveLanguage.Should().Be("fr-CA");
        localization.GetString("Str.Menu.File").Should().Be("Regional file");
        localization.GetString("Str.Menu.Edit").Should().Be("Profile edit");
        localization.GetString("Str.Menu.View").Should().Be(new LocalizationService().GetStringFrom("fr", "Str.Menu.View"));
        localization.AvailableLanguages.Should().Equal(new LocalizationService().AvailableLanguages);
        localization.GetStringFrom("fr", "Str.Menu.File").Should().Be("Profile file");
    }

    [AvaloniaFact]
    public void CanonicalOverridesApplyOnlyInTheirLocaleChain()
    {
        var profile = Profile(new() { ["en"] = new() { ["Str.Menu.File"] = "Profile English" } });
        var localization = new LocalizationService(profile);
        localization.Apply("en");
        localization.GetString("Str.Menu.File").Should().Be("Profile English");
        localization.GetStringFrom("en-GB", "Str.Menu.File").Should().Be("Profile English");
        localization.GetStringFrom("de", "Str.Menu.File").Should().Be(new LocalizationService().GetStringFrom("de", "Str.Menu.File"));
    }

    [AvaloniaFact]
    public void ChineseRegionsUseTheirScriptNeutral()
    {
        var profile = Profile(new()
        {
            ["zh-Hans"] = new() { ["Str.Menu.File"] = "Simplified profile" },
            ["zh-Hant"] = new() { ["Str.Menu.File"] = "Traditional profile" },
        });
        var localization = new LocalizationService(profile);
        localization.GetStringFrom("zh-CN", "Str.Menu.File").Should().Be("Simplified profile");
        localization.GetStringFrom("zh-TW", "Str.Menu.File").Should().Be("Traditional profile");
    }

    [AvaloniaFact]
    public void UserOverridesWinAndClearingThemRestoresProfile()
    {
        var profile = Profile(new()
        {
            ["fr"] = new() { ["Str.Menu.File"] = "Profile file" },
            ["fr-CA"] = new() { ["Str.Menu.File"] = "Regional file" },
        });
        var users = new UserTranslationsService(Path.Combine(root, "user"));
        users.SetOverride("fr", "Str.Menu.File", "User neutral").Should().BeTrue();
        var localization = new LocalizationService(profile, users);
        localization.Apply("fr-CA");
        localization.GetString("Str.Menu.File").Should().Be("User neutral");
        users.SetOverride("fr-CA", "Str.Menu.File", "User region").Should().BeTrue();
        localization.GetString("Str.Menu.File").Should().Be("User region");
        users.ClearOverride("fr-CA", "Str.Menu.File");
        localization.GetString("Str.Menu.File").Should().Be("User neutral");
        users.ClearOverride("fr", "Str.Menu.File");
        localization.GetString("Str.Menu.File").Should().Be("Regional file");
        File.Exists(Path.Combine(root, "user", "fr.json")).Should().BeFalse();
    }

    [AvaloniaFact]
    public void InvalidOverridesAreSkippedUsingExistingPlaceholderRules()
    {
        const string formatKey = "Str.Profiles.LoadError";
        var profile = Profile(new() { ["en"] = new()
        {
            ["Str.Menu.File"] = "Valid profile file",
            ["Str.Menu.Edit"] = " ",
            ["Str.Menu.View"] = "Stray {",
            ["Str.NotACanonicalKey"] = "Orphan",
            [formatKey] = "Missing placeholders",
        } });
        var localization = new LocalizationService(profile);
        localization.Apply("en");
        localization.GetString("Str.Menu.File").Should().Be("Valid profile file");
        var baseline = new LocalizationService();
        foreach (var key in new[] { "Str.Menu.Edit", "Str.Menu.View", formatKey })
            localization.GetString(key).Should().Be(baseline.GetString(key));
        localization.GetString("Str.NotACanonicalKey").Should().Be("Str.NotACanonicalKey");
    }

    [AvaloniaFact]
    public void ValidFormattedOverridePreservesPlaceholderIndices()
    {
        var profile = Profile(new() { ["en"] = new()
        {
            ["Str.Profiles.LoadError"] = "File {1}: bundle {0}. {{literal}}",
        } });
        var localization = new LocalizationService(profile);
        localization.GetStringFrom("en", "Str.Profiles.LoadError").Should().Be("File {1}: bundle {0}. {{literal}}");
    }

    [AvaloniaFact]
    public void MissingPayloadRetainsExistingResolution()
    {
        var profile = Profile(new());
        var localization = new LocalizationService(profile);
        localization.GetStringFrom("fr-CA", "Str.Menu.File").Should().Be(new LocalizationService().GetStringFrom("fr-CA", "Str.Menu.File"));
    }

    public void Dispose() => Directory.Delete(root, true);
}
