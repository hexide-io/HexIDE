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

    // #5036: white-label defaults may select the installation's RDCore behaviour.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RDCoreWin64ProfileDefaultAndSavedOverride(bool savedOverride)
    {
        if (savedOverride) File.WriteAllText(SettingsPath, """{"rdcoreWin64OnlySetOnWindows":false}""");
        var settings = Create("""{"rdcoreWin64OnlySetOnWindows":true}""");
        var setting = typeof(SettingsService).GetProperty("RDCoreWin64OnlySetOnWindows");
        setting.Should().NotBeNull();
        setting!.GetValue(settings).Should().Be(!savedOverride);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ListMembersAsYouTypeProfileDefaultAndSavedOverride(bool savedOverride)
    {
        if (savedOverride) File.WriteAllText(SettingsPath, """{"listMembersAsYouType":true}""");
        var settings = Create("""{"listMembersAsYouType":false}""");
        settings.ListMembersAsYouType.Should().Be(savedOverride);
    }

    [Theory]
    [InlineData("showInlayHints", nameof(ISettingsService.ShowInlayHints), "false", "true")]
    [InlineData("showCodeLenses", nameof(ISettingsService.ShowCodeLenses), "false", "true")]
    [InlineData("colorBracketsByDepth", nameof(ISettingsService.ColorBracketsByDepth), "false", "true")]
    [InlineData("showIndentGuides", nameof(ISettingsService.ShowIndentGuides), "false", "true")]
    [InlineData("isStickyScrollEnabled", nameof(ISettingsService.IsStickyScrollEnabled), "false", "true")]
    [InlineData("stickyScrollDepth", nameof(ISettingsService.StickyScrollDepth), "1", "3")]
    [InlineData("stickyScrollDepth", nameof(ISettingsService.StickyScrollDepth), "10", "3")]
    [InlineData("editorFontFamily", nameof(ISettingsService.EditorFontFamily), "\"Example Mono\"", "\"Consolas\"")]
    [InlineData("editorFontSize", nameof(ISettingsService.EditorFontSize), "6", "12")]
    [InlineData("editorFontSize", nameof(ISettingsService.EditorFontSize), "12.5", "12")]
    [InlineData("editorFontSize", nameof(ISettingsService.EditorFontSize), "72", "12")]
    public void EditorProfileDefaultsStartNewUsersAndSavedChoicesWin(string name, string propertyName,
        string startingValue, string savedValue)
    {
        var property = typeof(ISettingsService).GetProperty(propertyName)!;
        var defaults = "{\"" + name + "\":" + startingValue + "}";
        var settings = Create(defaults);
        JsonSerializer.SerializeToElement(property.GetValue(settings)).GetRawText().Should().Be(startingValue);
        settings.ActiveLanguage = "de";
        settings.Save();
        using (var saved = JsonDocument.Parse(File.ReadAllText(SettingsPath)))
            saved.RootElement.TryGetProperty(name, out _).Should().BeFalse("unedited defaults stay in the profile");
        File.WriteAllText(SettingsPath, "{\"" + name + "\":" + savedValue + "}");
        JsonSerializer.SerializeToElement(property.GetValue(Create(defaults))).GetRawText().Should().Be(savedValue);
    }

    [Fact]
    [Trait("Issue", "4307")]
    public void ServerSettingsReplaceOneDefaultWholeAndResetWithoutTouchingOtherIdsOrProfile()
    {
        const string defaults = """{"languageServerSettings":{"one":{"rdcore":{"enabled":true,"extra":1}},"two":{"n":2}}}""";
        var settings = Create(defaults);
        settings.GetLanguageServerSettings("one")!.Value.GetProperty("rdcore").GetProperty("extra").GetInt32().Should().Be(1);
        settings.HasLanguageServerSettings("one").Should().BeFalse();
        settings.GetLanguageServerSettings("missing").Should().BeNull();
        var changes = new List<string?>();
        settings.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        using (var value = JsonDocument.Parse("""{"rdcore":{"enabled":false}}"""))
            settings.SetLanguageServerSettings("one", value.RootElement);
        File.ReadAllText(Path.Combine(root, "defaults.json")).Should().Be(defaults);
        var loaded = Create(defaults);
        loaded.HasLanguageServerSettings("one").Should().BeTrue();
        loaded.GetLanguageServerSettings("one")!.Value.GetProperty("rdcore").TryGetProperty("extra", out _).Should().BeFalse();
        loaded.GetLanguageServerSettings("two")!.Value.GetProperty("n").GetInt32().Should().Be(2);
        using (var saved = JsonDocument.Parse(File.ReadAllText(SettingsPath)))
            saved.RootElement.GetProperty("languageServerSettings").TryGetProperty("two", out _).Should().BeFalse();
        settings.SetLanguageServerSettings("other", JsonDocument.Parse("{}").RootElement);
        settings.RemoveLanguageServerSettings("one");
        changes.Should().Equal("LanguageServerSettings", "LanguageServerSettings", "LanguageServerSettings");
        File.ReadAllText(Path.Combine(root, "defaults.json")).Should().Be(defaults);
        loaded = Create(defaults);
        loaded.HasLanguageServerSettings("one").Should().BeFalse();
        loaded.GetLanguageServerSettings("one")!.Value.GetProperty("rdcore").GetProperty("enabled").GetBoolean().Should().BeTrue();
        loaded.HasLanguageServerSettings("other").Should().BeTrue();
        File.ReadAllText(Path.Combine(root, "defaults.json")).Should().Be(defaults);
    }

    [Fact]
    [Trait("Issue", "4307")]
    public void EmptyUserServerObjectOverridesDefaultAndRemovalReturnsNoneWithoutDefault()
    {
        var settings = Create("""{"languageServerSettings":{"one":{"n":1}}}""");
        settings.SetLanguageServerSettings("one", JsonDocument.Parse("{}").RootElement);
        settings.GetLanguageServerSettings("one")!.Value.EnumerateObject().Should().BeEmpty();
        settings.SetLanguageServerSettings("two", JsonDocument.Parse("{}").RootElement);
        settings.RemoveLanguageServerSettings("two");
        settings.GetLanguageServerSettings("two").Should().BeNull();
        settings.HasLanguageServerSettings("one").Should().BeTrue();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("1")]
    [Trait("Issue", "4307")]
    public void ServerSettingsRejectNonObjectsInUserAndProfileLayers(string json)
    {
        var settings = new SettingsService(SettingsPath);
        Action write = () => settings.SetLanguageServerSettings("one", JsonDocument.Parse(json).RootElement);
        write.Should().Throw<ArgumentException>();
        File.Exists(SettingsPath).Should().BeFalse();
        Action profile = () => Create("{\"languageServerSettings\":{\"one\":" + json + "}}");
        profile.Should().Throw<ProfileLoadException>();
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
