using System.Text.Json;
using HexIDE.Profiles;

namespace HexIDE.Tests.Profiles;

public sealed class ProfileResolverTests : IDisposable
{
    private readonly string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "hexide-profiles-" + Guid.NewGuid());
    private string UserData => Path.Combine(root, "user");
    private ProfileResolver Resolver => new(UserData, root);

    public ProfileResolverTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    private string Bundle(string manifest, string name = "custom")
    {
        var directory = Path.Combine(root, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "profile.json"), manifest);
        return directory;
    }

    [Theory]
    [InlineData(null, "vb6", 8, true)]
    [InlineData("vb6", "vb6", 8, true)]
    [InlineData("vbaode", "vbaode", 1, false)]
    [InlineData("vba", "vba", 0, false)]
    public void EmbeddedIndexPreservesTheApprovedMigrationTable(string? selector, string id, int count, bool projectReferences)
    {
        var profile = Resolver.Resolve(selector)!;
        profile.Id.Should().Be(id);
        profile.Source.Should().Be("embedded:" + id);
        profile.SchemaVersion.Should().Be(1);
        profile.ProjectTypes!.Value.Length.Should().Be(count);
        profile.MenuVisibility["project.references"].Should().Be(projectReferences);
        profile.MenuVisibility["tools.references"].Should().Be(!projectReferences);
        profile.ToolbarVisibility.Should().BeEmpty();
        profile.Keymap.Should().BeNull();
        profile.Theme.Should().BeNull();
        profile.LanguageOverrides.Should().BeEmpty();
        profile.SettingsDefaults.Should().BeNull();
        if (count == 8)
            profile.ProjectTypes.Value.Should().Equal("standard-exe", "activex-exe", "activex-dll", "activex-control",
                "application-wizard", "wizard-manager", "data-project", "enterprise-controls");
        if (count == 1)
            profile.ProjectTypes.Value.Should().Equal("standard-exe");
    }

    [Fact]
    public void NoneDoesNotReadAnyBundle() => Resolver.Resolve("none").Should().BeNull();

    [Fact]
    public void ReservedNamesWinOverInstalledBundlesButPathsSelectExactly()
    {
        var directory = Path.Combine(UserData, "profiles", "vb6");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "profile.json"), """{"schemaVersion":1,"id":"vb6","projectTypes":[]}""");
        Resolver.Resolve("vb6")!.ProjectTypes!.Value.Length.Should().Be(8);
        Resolver.Resolve(directory)!.ProjectTypes!.Value.Should().BeEmpty();
        Resolver.Resolve(directory)!.Source.Should().Be(directory);
    }

    [Fact]
    public void InstalledNamesUseOnlyTheSuppliedUserDataRoot()
    {
        var directory = Path.Combine(UserData, "profiles", "custom");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "profile.json"), """{"schemaVersion":1,"id":"custom"}""");
        Resolver.Resolve("custom")!.Source.Should().Be(directory);
        var differentCase = Path.Combine(UserData, "profiles", "Other");
        Directory.CreateDirectory(differentCase);
        File.WriteAllText(Path.Combine(differentCase, "profile.json"), """{"schemaVersion":1,"id":"other"}""");
        Resolver.Invoking(r => r.Resolve("other")).Should().Throw<ProfileLoadException>()
            .Which.Error.Should().Be(ProfileError.MissingFile);
        new ProfileResolver(Path.Combine(root, "other-user"), root).Invoking(r => r.Resolve("custom"))
            .Should().Throw<ProfileLoadException>().Which.Error.Should().Be(ProfileError.MissingFile);
    }

    [Theory]
    [InlineData("./custom")]
    [InlineData(@".\custom")]
    public void RelativePathsUseInvocationDirectory(string selector)
    {
        Bundle("""{"schemaVersion":1,"id":"custom"}""");
        Resolver.Resolve(selector)!.Source.Should().Be(Path.Combine(root, "custom"));
    }

    [Theory]
    [InlineData("VB6")]
    [InlineData("")]
    [InlineData("with space")]
    [InlineData("123")]
    public void InvalidSelectorsAreRefused(string selector) =>
        Resolver.Invoking(r => r.Resolve(selector)).Should().Throw<ProfileLoadException>()
            .Which.Error.Should().Be(ProfileError.InvalidSelector);

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{broken")]
    [InlineData("""{"schemaVersion":2,"id":"custom"}""")]
    [InlineData("""{"schemaVersion":1,"id":"none"}""")]
    [InlineData("""{"schemaVersion":1,"id":"Custom"}""")]
    [InlineData("""{"schemaVersion":1,"id":"custom","code":"plugin.dll"}""")]
    [InlineData("""{"schemaVersion":1,"id":"custom","id":"other"}""")]
    [InlineData("""{"schemaVersion":1,"id":"custom","projectTypes":["made-up"]}""")]
    [InlineData("""{"schemaVersion":1,"id":"custom","projectTypes":null}""")]
    [InlineData("""{"schemaVersion":1,"id":"custom","menuVisibility":{"file":true}}""")]
    [InlineData("""{"schemaVersion":1,"id":"custom","menuVisibility":{"tools.references":"yes"}}""")]
    [InlineData("""{"schemaVersion":1,"id":"custom","toolbarVisibility":{"standard.SaveCommand":false}}""")]
    [InlineData("""{"schemaVersion":1,"id":"custom","languageOverrides":{"xx":"xx.json"}}""")]
    public void InvalidManifestIsAttributedAndNeverFallsBack(string manifest)
    {
        var directory = Bundle(manifest);
        var error = Resolver.Invoking(r => r.Resolve(directory)).Should().Throw<ProfileLoadException>().Which;
        error.SourceId.Should().Be(directory);
        error.File.Should().Be("profile.json");
        error.Error.Should().Be(ProfileError.InvalidManifest);
    }

    [Fact]
    public void InstalledIdMustMatchTheDirectoryName()
    {
        var directory = Path.Combine(UserData, "profiles", "custom");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "profile.json"), """{"schemaVersion":1,"id":"other"}""");
        Resolver.Invoking(r => r.Resolve("custom")).Should().Throw<ProfileLoadException>()
            .Which.Error.Should().Be(ProfileError.InvalidManifest);
    }

    [Fact]
    public void OmissionAndEmptyOfferingsRemainDistinct()
    {
        var directory = Bundle("""{"schemaVersion":1,"id":"custom"}""");
        Resolver.Resolve(directory)!.ProjectTypes.Should().BeNull();
        File.WriteAllText(Path.Combine(directory, "profile.json"), """{"schemaVersion":1,"id":"custom","projectTypes":[]}""");
        Resolver.Resolve(directory)!.ProjectTypes!.Value.Should().BeEmpty();
    }

    [Fact]
    public void SnapshotOwnsAllPayloadsWithoutApplyingOrReopeningThem()
    {
        var directory = Bundle("""
            {"schemaVersion":1,"id":"custom","keymap":"keys.json","theme":"theme.json",
             "languageOverrides":{"en":"en.json"},"settingsDefaults":"settings.json",
             "projectTypes":["activex-dll","standard-exe"],"toolbarVisibility":{"debug":false}}
            """);
        File.WriteAllText(Path.Combine(directory, "keys.json"), """{"name":"Keys","bindings":{"UnknownCommand":"F9"}}""");
        File.WriteAllText(Path.Combine(directory, "theme.json"), """{"name":"Theme","colors":{"UnknownColor":"red"}}""");
        File.WriteAllText(Path.Combine(directory, "en.json"), """{"name":"English","strings":{"UnknownKey":"text"}}""");
        File.WriteAllText(Path.Combine(directory, "settings.json"), """{"tabWidth":7,"autoIndent":false}""");
        var profile = Resolver.Resolve(directory)!;
        profile.Keymap!.File.Should().Be("keys.json");
        profile.LanguageOverrides["en"].File.Should().Be("en.json");
        Directory.Delete(directory, true);
        profile.Keymap!.Content.GetProperty("name").GetString().Should().Be("Keys");
        profile.Theme!.Content.GetProperty("name").GetString().Should().Be("Theme");
        profile.LanguageOverrides["en"].Content.GetProperty("strings").GetProperty("UnknownKey").GetString().Should().Be("text");
        profile.SettingsDefaults!.Content.GetProperty("tabWidth").GetInt32().Should().Be(7);
        profile.SettingsDefaults.Content.TryGetProperty("activeTheme", out _).Should().BeFalse();
        profile.ProjectTypes!.Value.Should().Equal("activex-dll", "standard-exe");
        profile.ToolbarVisibility["debug"].Should().BeFalse();
    }

    [Theory]
    [InlineData("../outside.json")]
    [InlineData(@"..\outside.json")]
    [InlineData("/outside.json")]
    [InlineData(@"C:\outside.json")]
    [InlineData(@"C:outside.json")]
    [InlineData(@"\\server\outside.json")]
    [InlineData("https://example.test/payload.json")]
    public void EscapingReferencesAreRejectedOnEveryHost(string path)
    {
        var manifest = "{\"schemaVersion\":1,\"id\":\"custom\",\"keymap\":" + JsonSerializer.Serialize(path) + "}";
        var directory = Bundle(manifest);
        Resolver.Invoking(r => r.Resolve(directory)).Should().Throw<ProfileLoadException>()
            .Which.Error.Should().Be(ProfileError.InvalidPath);
    }

    [Theory]
    [InlineData("keymap", "missing.json", (int)ProfileError.MissingFile)]
    [InlineData("keymap", "null", (int)ProfileError.InvalidPayload)]
    [InlineData("keymap", "{broken", (int)ProfileError.InvalidPayload)]
    [InlineData("keymap", "{\"bindings\":[]}", (int)ProfileError.InvalidPayload)]
    [InlineData("theme", "{\"colors\":42}", (int)ProfileError.InvalidPayload)]
    [InlineData("languageOverrides", "{\"strings\":[]}", (int)ProfileError.InvalidPayload)]
    [InlineData("settingsDefaults", "{\"window\":{}}", (int)ProfileError.InvalidPayload)]
    [InlineData("settingsDefaults", "{\"loadUnsignedAddins\":true}", (int)ProfileError.InvalidPayload)]
    [InlineData("settingsDefaults", "{\"tabWidth\":\"four\"}", (int)ProfileError.InvalidPayload)]
    [InlineData("settingsDefaults", "{\"autoIndent\":null}", (int)ProfileError.InvalidPayload)]
    public void PayloadFailuresAreAttributed(string surface, string payload, int expected)
    {
        var entry = surface == "languageOverrides" ? "\"languageOverrides\":{\"en\":\"payload.json\"}" : $"\"{surface}\":\"payload.json\"";
        var directory = Bundle("{\"schemaVersion\":1,\"id\":\"custom\"," + entry + "}");
        if (expected != (int)ProfileError.MissingFile)
            File.WriteAllText(Path.Combine(directory, "payload.json"), payload);
        var error = Resolver.Invoking(r => r.Resolve(directory)).Should().Throw<ProfileLoadException>().Which;
        error.SourceId.Should().Be(directory);
        error.File.Should().Be("payload.json");
        error.Error.Should().Be((ProfileError)expected);
    }

    [Fact]
    public void FailureUsesLocalizedTextWithBundleAndFileAttribution()
    {
        var directory = Bundle("""{"schemaVersion":1,"id":"custom","theme":"missing.json"}""");
        var error = Resolver.Invoking(r => r.Resolve(directory)).Should().Throw<ProfileLoadException>().Which;
        var localization = Substitute.For<HexIDE.Localization.ILocalizationService>();
        localization.GetString("Str.Profiles.LoadError").Returns("localized {0} / {1}");
        error.GetLocalizedMessage(localization).Should().Be($"localized {directory} / missing.json");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContainedLinksAreRefusedByTheSharedReader(bool manifestLink)
    {
        var directory = Bundle("""{"schemaVersion":1,"id":"custom","keymap":"keys.json"}""");
        var target = Path.Combine(directory, "target.json");
        File.WriteAllText(target, manifestLink
            ? """{"schemaVersion":1,"id":"custom"}"""
            : """{"name":"Keys","bindings":{}}""");
        var link = Path.Combine(directory, manifestLink ? "profile.json" : "keys.json");
        if (manifestLink)
            File.Delete(link);
        File.CreateSymbolicLink(link, target);
        var error = Resolver.Invoking(r => r.Resolve(directory)).Should().Throw<ProfileLoadException>().Which;
        error.File.Should().Be(Path.GetFileName(link));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OversizedFilesAreRefusedByTheSharedReader(bool manifest)
    {
        var directory = Bundle("""{"schemaVersion":1,"id":"custom","keymap":"keys.json"}""");
        File.WriteAllText(Path.Combine(directory, manifest ? "profile.json" : "keys.json"),
            (manifest ? """{"schemaVersion":1,"id":"custom"}""" : """{"bindings":{}}""")
            + new string(' ', 1024 * 1024));
        var error = Resolver.Invoking(r => r.Resolve(directory)).Should().Throw<ProfileLoadException>().Which;
        error.SourceId.Should().Be(directory);
        error.File.Should().Be(manifest ? "profile.json" : "keys.json");
    }

    [Fact]
    public void FailedLoadCannotMutateAnEarlierSnapshot()
    {
        var directory = Bundle("""{"schemaVersion":1,"id":"custom","menuVisibility":{"tools.references":true}}""");
        var first = Resolver.Resolve(directory)!;
        File.WriteAllText(Path.Combine(directory, "profile.json"), """{"schemaVersion":1,"id":"custom","menuVisibility":{"tools.references":false},"theme":"missing.json"}""");
        Resolver.Invoking(r => r.Resolve(directory)).Should().Throw<ProfileLoadException>();
        first.MenuVisibility["tools.references"].Should().BeTrue();
    }
}
