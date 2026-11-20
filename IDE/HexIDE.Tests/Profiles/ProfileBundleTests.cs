using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using HexIDE.IDE;
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
    [InlineData("settings.json", "{\"listMembersAsYouType\":\"false\"}", "settingsDefaults.listMembersAsYouType", "InvalidType")]
    [InlineData("settings.json", "{\"window\":{}}", "settingsDefaults.window", "DisallowedSetting")]
    [InlineData("settings.json", "{\"tabWidth\":\"four\"}", "settingsDefaults.tabWidth", "InvalidType")]
    [InlineData("settings.json", "{\"stickyScrollDepth\":\"x\"}", "settingsDefaults.stickyScrollDepth", "InvalidType")]
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
    [InlineData("editorFontFamily", "\"\"")]
    [InlineData("editorFontFamily", "\"   \"")]
    [InlineData("editorFontFamily", "42")]
    [InlineData("editorFontSize", "\"12\"")]
    [InlineData("editorFontSize", "5.5")]
    [InlineData("editorFontSize", "73")]
    [InlineData("editorFontSize", "1e400")]
    [InlineData("stickyScrollDepth", "0")]
    [InlineData("stickyScrollDepth", "11")]
    [InlineData("stickyScrollDepth", "1.5")]
    [InlineData("showInlayHints", "\"false\"")]
    [InlineData("showCodeLenses", "0")]
    [InlineData("colorBracketsByDepth", "null")]
    [InlineData("showIndentGuides", "[]")]
    [InlineData("isStickyScrollEnabled", "{}")]
    public void InvalidEditorDefaultsAreRejected(string name, string value)
    {
        WithBundle(directory =>
        {
            WriteSettings(directory, name, value);
            var error = ((Action)(() => ProfileBundleLoader.Load(directory)))
                .Should().Throw<InvalidDataException>().Which;
            error.Data["field"].Should().Be("settingsDefaults." + name);
            error.Data["code"].Should().Be("InvalidType");
        });
    }

    [Theory]
    [InlineData("loadUnsignedAddins", "true")]
    [InlineData("revocationListUrl", "\"\"")]
    [InlineData("reloadFilesChangedOutsideIde", "false")]
    [InlineData("editorColorOverrides", "{}")]
    [InlineData("serverChatTools", "{}")]
    [InlineData("window", "{}")]
    public void NonDefaultSettingsRemainDisallowed(string name, string value)
    {
        WithBundle(directory =>
        {
            WriteSettings(directory, name, value);
            var error = ((Action)(() => ProfileBundleLoader.Load(directory)))
                .Should().Throw<InvalidDataException>().Which;
            error.Data["field"].Should().Be("settingsDefaults." + name);
            error.Data["code"].Should().Be("DisallowedSetting");
        });
    }

    [Fact]
    public void EverySettableSettingIsAllowedOrExcludedWithAReason()
    {
        // Read the actual persisted names, including JsonPropertyName overrides, rather than guessing casing.
        var dataType = typeof(SettingsService).GetNestedType("SettingsData", BindingFlags.NonPublic)!;
        var defaults = JsonSerializer.SerializeToElement(Activator.CreateInstance(dataType, nonPublic: true), dataType);
        var unclassified = new List<string>();
        foreach (var property in typeof(ISettingsService).GetProperties().Where(property => property.CanWrite))
        {
            var persisted = dataType.GetProperty(property.Name);
            persisted.Should().NotBeNull("{0} must have a persisted JSON mapping", property.Name);
            var name = persisted!.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? persisted.Name;
            WithBundle(directory =>
            {
                WriteSettings(directory, name, defaults.GetProperty(name).GetRawText());
                var allowed = true;
                try { ProfileBundleLoader.Load(directory); }
                catch (InvalidDataException error)
                {
                    error.Data["code"].Should().Be("DisallowedSetting");
                    allowed = false;
                }
                if (ProfileBundleLoader.ExcludedSettings.TryGetValue(name, out var reason))
                {
                    reason.Should().NotBeNullOrWhiteSpace("{0} needs an exclusion reason", name);
                    allowed.Should().BeFalse("{0} is explicitly excluded", name);
                }
                else if (!allowed)
                    unclassified.Add(property.Name + " (" + name + ")");
            });
        }
        unclassified.Should().BeEmpty("add each setting to the profile list, or to ExcludedSettings with a reason");
    }

    private static void WriteSettings(string directory, string name, string value)
    {
        File.WriteAllText(Path.Combine(directory, "profile.json"),
            """{"schemaVersion":1,"id":"example","settingsDefaults":"settings.json"}""");
        File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"" + name + "\":" + value + "}");
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectoryReplacementBetweenReadsCannotMixGenerations(bool replaceAncestor)
    {
        WithBundle(directory =>
        {
            var bundleDirectory = replaceAncestor ? Path.Combine(directory, "bundle") : directory;
            WriteGeneration(bundleDirectory, 4);
            var old = directory + "-old";
            try
            {
                var bundle = ProfileBundleLoader.Load(bundleDirectory, beforeOpen: reference =>
                {
                    if (reference != "settings.json")
                        return;
                    Action move = () => Directory.Move(directory, old);
                    if (OperatingSystem.IsWindows())
                        move.Should().Throw<IOException>();
                    else
                    {
                        move();
                        WriteGeneration(bundleDirectory, 8);
                    }
                });
                bundle.Files["theme.json"].GetProperty("name").GetString().Should().Be("Theme4");
                bundle.Files["settings.json"].GetProperty("tabWidth").GetInt32().Should().Be(4);
            }
            finally
            {
                if (Directory.Exists(old))
                    Directory.Delete(old, true);
            }
        });
    }

    [Fact]
    public void ManifestReplacementBetweenReadsRefusesWithRetryCode()
    {
        WithBundle(directory =>
        {
            WriteGeneration(directory, 4);
            var error = ((Action)(() => ProfileBundleLoader.Load(directory, beforeOpen: reference =>
            {
                if (reference == "settings.json")
                    File.WriteAllText(Path.Combine(directory, "profile.json"),
                        """{"schemaVersion":1,"id":"example"}""");
            }))).Should().Throw<InvalidDataException>().Which;
            error.Data["code"].Should().Be("BundleChanged");
            error.Data["field"].Should().Be("$");
            error.Data["file"].Should().Be(Path.Combine(directory, "profile.json"));
        });
    }

    private static void WriteGeneration(string directory, int width)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "profile.json"),
            """{"schemaVersion":1,"id":"example","theme":"theme.json","settingsDefaults":"settings.json"}""");
        File.WriteAllText(Path.Combine(directory, "theme.json"), "{\"name\":\"Theme" + width + "\",\"colors\":{}}");
        File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"tabWidth\":" + width + "}");
    }

    private static void WithBundle(Action<string> action)
    {
        var directory = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "hexide-bundle-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, true); }
    }
}
