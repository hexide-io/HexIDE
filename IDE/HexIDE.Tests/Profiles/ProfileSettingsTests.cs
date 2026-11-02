using System.Text.Json;
using HexIDE.IDE;
using HexIDE.Profiles;

namespace HexIDE.Tests.Profiles;

public sealed class ProfileSettingsTests : IDisposable
{
    private readonly string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "hexide-profile-settings-" + Guid.NewGuid());
    private string SettingsPath => Path.Combine(root, "user-settings.json");

    public ProfileSettingsTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    private SettingsService Create(string defaults)
    {
        File.WriteAllText(Path.Combine(root, "profile.json"),
            """{"schemaVersion":1,"id":"custom","settingsDefaults":"defaults.json"}""");
        File.WriteAllText(Path.Combine(root, "defaults.json"), defaults);
        var profile = new ProfileResolver(root, root).Resolve(root)!;
        return new SettingsService(SettingsPath, profile);
    }

    [Fact]
    public void OmittedSavedPropertiesRetainProfileDefaults()
    {
        File.WriteAllText(SettingsPath, """{"activeLanguage":"de"}""");
        var settings = Create("""{"tabWidth":8,"autoIndent":false}""");
        settings.TabWidth.Should().Be(8);
        settings.AutoIndent.Should().BeFalse();
        settings.ActiveLanguage.Should().Be("de");
        settings.GridWidth.Should().Be(SettingsDefaults.GridWidth);
    }

    [Fact]
    public void ExplicitSavedHostDefaultWinsWithoutPersistingProfileValues()
    {
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { tabWidth = SettingsDefaults.TabWidth }));
        var settings = Create("""{"tabWidth":8,"autoIndent":false}""");
        settings.AutoIndent.Should().BeFalse();
        settings.TabWidth.Should().Be(SettingsDefaults.TabWidth);
        settings.ActiveLanguage = "de";
        settings.Save();
        using var saved = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        saved.RootElement.TryGetProperty("autoIndent", out _).Should().BeFalse();
        saved.RootElement.GetProperty("tabWidth").GetInt32().Should().Be(SettingsDefaults.TabWidth);
    }

    [Fact]
    public void MissingSavedFileUsesSnapshotAndDoesNotWriteIt()
    {
        var settings = Create("""{"tabWidth":8,"isMinimapVisible":false}""");
        File.WriteAllText(Path.Combine(root, "defaults.json"), "{}");
        settings.Load();
        settings.TabWidth.Should().Be(8);
        settings.IsMinimapVisible.Should().BeFalse();
        settings.Save();
        File.Exists(SettingsPath).Should().BeFalse();
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(100, 32)]
    public void ProfileDefaultsUseExistingValueValidation(int input, int expected)
    {
        Create(JsonSerializer.Serialize(new { tabWidth = input })).TabWidth.Should().Be(expected);
    }

    [Theory]
    [InlineData("{\"tabWidth\":\"wide\"}")]
    [InlineData("{\"loadUnsignedAddins\":true}")]
    public void InvalidDefaultsAreRejectedBeforeSettingsAreTouched(string defaults)
    {
        File.WriteAllText(SettingsPath, "{}");
        Action load = () => Create(defaults);
        load.Should().Throw<ProfileLoadException>().Which.File.Should().Be("defaults.json");
        File.ReadAllText(SettingsPath).Should().Be("{}");
    }
}
