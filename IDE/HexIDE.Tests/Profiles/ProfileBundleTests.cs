using System.IO;
using HexIDE.IDE.Profiles;

namespace HexIDE.Tests.Profiles;

public class ProfileBundleTests
{
    [Fact]
    public void CapturesEveryReferencedFileAndRetainsItAfterDiskChanges()
    {
        WithBundle(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "profile.json"), """
                {"schemaVersion":1,"id":"example","theme":"theme.json","keymap":"keys.json",
                 "settingsDefaults":"settings.json","languageOverrides":{"en":"en.json"}}
                """);
            File.WriteAllText(Path.Combine(directory, "theme.json"), """{"name":"Theme","colors":{}}""");
            File.WriteAllText(Path.Combine(directory, "keys.json"), """{"name":"Keys","bindings":{}}""");
            File.WriteAllText(Path.Combine(directory, "settings.json"), """{"tabWidth":4}""");
            File.WriteAllText(Path.Combine(directory, "en.json"), """{"name":"English","strings":{}}""");
            var bundle = ProfileBundleLoader.Load(directory, "example");
            bundle.Manifest.Source.Should().Be(Path.Combine(directory, "profile.json"));
            bundle.Files.Keys.Should().BeEquivalentTo("theme.json", "keys.json", "settings.json", "en.json");
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{}");
            bundle.Files["settings.json"].GetProperty("tabWidth").GetInt32().Should().Be(4);
        });
    }

    [Theory]
    [InlineData("profile.json", "{", "$", "InvalidJson")]
    [InlineData("theme.json", "{", "theme", "InvalidJson")]
    [InlineData("theme.json", "null", "theme", "InvalidType")]
    [InlineData("theme.json", "{\"colors\":42}", "theme", "InvalidPayload")]
    [InlineData("theme.json", null, "theme", "UnreadablePath")]
    [InlineData("settings.json", "{\"window\":{}}", "settingsDefaults.window", "DisallowedSetting")]
    [InlineData("settings.json", "{\"tabWidth\":\"four\"}", "settingsDefaults.tabWidth", "InvalidType")]
    [InlineData("settings.json", "{\"tabWidth\":4,\"tabWidth\":8}", "settingsDefaults.tabWidth", "DuplicateField")]
    public void FailedAssemblyAttributesTheFileAndField(string file, string? contents, string field, string code)
    {
        WithBundle(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "profile.json"), """
                {"schemaVersion":1,"id":"example","theme":"theme.json","settingsDefaults":"settings.json"}
                """);
            File.WriteAllText(Path.Combine(directory, "theme.json"), """{"name":"Theme","colors":{}}""");
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{}");
            if (contents == null)
                File.Delete(Path.Combine(directory, file));
            else
                File.WriteAllText(Path.Combine(directory, file), contents);
            var error = ((Action)(() => ProfileBundleLoader.Load(directory, "example")))
                .Should().Throw<InvalidDataException>().Which;
            error.Data["file"].Should().Be(Path.Combine(directory, file));
            error.Data["field"].Should().Be(field);
            error.Data["code"].Should().Be(code);
        });
    }

    [Theory]
    [InlineData("vb6")]
    [InlineData("vbaode")]
    [InlineData("vba")]
    public void BuiltinNamesCannotBeShadowedButExplicitDirectoriesCanSelectThem(string id)
    {
        WithBundle(directory =>
        {
            var installed = Path.Combine(directory, "profiles", id);
            Directory.CreateDirectory(installed);
            File.WriteAllText(Path.Combine(installed, "profile.json"), "broken installation");
            var named = ProfileBundleLoader.ResolveName(directory, id);
            named.Manifest.Source.Should().Be($"HexIDE.IDE.Profiles.Builtins.{id}.json");
            named.Files.Should().BeEmpty();
            File.WriteAllText(Path.Combine(installed, "profile.json"),
                "{\"schemaVersion\":1,\"id\":\"" + id + "\",\"projectTypes\":[]}");
            var selected = ProfileBundleLoader.Load(installed);
            selected.Manifest.Source.Should().Be(Path.Combine(installed, "profile.json"));
            selected.Manifest.ProjectTypes!.Value.Should().BeEmpty();
        });
    }

    [Theory]
    [InlineData("none")]
    [InlineData("VB6")]
    [InlineData("../outside")]
    public void InvalidNamesAreRejectedBeforeLookingOnDisk(string id)
    {
        WithBundle(directory =>
        {
            var error = ((Action)(() => ProfileBundleLoader.ResolveName(directory, id)))
                .Should().Throw<InvalidDataException>().Which;
            error.Data["code"].Should().Be("InvalidId");
            error.Data["field"].Should().Be("id");
        });
    }

    private static void WithBundle(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-bundle-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, true); }
    }
}
