using System.Collections.Generic;
using System.IO;
using System.Linq;
using HexIDE.IDE.Profiles;

namespace HexIDE.Tests.Profiles;

public class ProfileCatalogTests
{
    [Fact]
    public void BuiltinsAreVersionedIndexedManifestsWithTheApprovedOfferings()
    {
        var profiles = ProfileCatalog.Builtins;
        profiles.Select(x => x.Id).Should().Equal("vb6", "vbaode", "vba");
        foreach (var profile in profiles)
        {
            profile.SchemaVersion.Should().Be(1);
            profile.Source.Should().Be($"HexIDE.IDE.Profiles.Builtins.{profile.Id}.json");
            profile.MenuVisibility["project.references"].Should().Be(profile.Id == "vb6");
            profile.MenuVisibility["tools.references"].Should().Be(profile.Id != "vb6");
            profile.Keymap.Should().BeNull();
            profile.Theme.Should().BeNull();
            profile.SettingsDefaults.Should().BeNull();
            profile.LanguageOverrides.Should().BeEmpty();
            profile.ToolbarVisibility.Should().BeEmpty();
        }
        profiles[0].ProjectTypes!.Value.Should().Equal("standard-exe", "activex-exe", "activex-dll",
            "activex-control", "application-wizard", "wizard-manager", "data-project", "enterprise-controls");
        profiles[1].ProjectTypes!.Value.Should().Equal("standard-exe");
        profiles[2].ProjectTypes!.Value.Should().BeEmpty();
    }

    [Fact]
    public void SnapshotCopiesInputsAndDistinguishesOmittedFromEmptyOfferings()
    {
        var types = new List<string> { "standard-exe" };
        var menus = new Dictionary<string, bool> { ["project.references"] = true };
        var languages = new Dictionary<string, string> { ["en"] = "en.json" };
        var snapshot = new ProfileSnapshot(1, "example", "source", projectTypes: types,
            menuVisibility: menus, toolbarVisibility: menus, languageOverrides: languages);
        types.Clear();
        menus.Clear();
        languages.Clear();
        snapshot.ProjectTypes!.Value.Should().Equal("standard-exe");
        snapshot.MenuVisibility["project.references"].Should().BeTrue();
        snapshot.ToolbarVisibility["project.references"].Should().BeTrue();
        snapshot.LanguageOverrides["en"].Should().Be("en.json");
        new ProfileSnapshot(1, "example", "source").ProjectTypes.Should().BeNull();
        new ProfileSnapshot(1, "example", "source", projectTypes: []).ProjectTypes!.Value.Should().BeEmpty();
    }

    [Theory]
    [InlineData("a", true)]
    [InlineData("example-2", true)]
    [InlineData("none", false)]
    [InlineData("", false)]
    [InlineData("Example", false)]
    [InlineData("2example", false)]
    [InlineData("éxample", false)]
    [InlineData("a_b", false)]
    [InlineData("../x", false)]
    public void IdGrammarIsOrdinalLowerCaseAsciiWithNoneReserved(string id, bool expected)
    {
        ProfileCatalog.IsBundleId(id).Should().Be(expected);
    }

    [Fact]
    public void DiscoveryUsesOnlyInstalledFolderNamesAndDoesNotReadUnvalidatedManifests()
    {
        var root = Path.Combine(Path.GetTempPath(), "hexide-profiles-" + Guid.NewGuid());
        try
        {
            ProfileCatalog.DiscoverInstalled(root).Should().BeEmpty();
            var profiles = Path.Combine(root, "profiles");
            foreach (var id in new[] { "z-last", "a-first", "none", "Invalid", "vb6" })
                Directory.CreateDirectory(Path.Combine(profiles, id));
            File.WriteAllText(Path.Combine(profiles, "a-first", "profile.json"), "half-written");
            File.WriteAllText(Path.Combine(profiles, "file-only"), "ignored");
            Directory.CreateDirectory(Path.Combine(root, "outside"));
            var candidates = ProfileCatalog.DiscoverInstalled(root);
            candidates.Select(x => x.Id).Should().Equal("a-first", "vb6", "z-last");
            candidates[0].DirectoryPath.Should().Be(Path.Combine(profiles, "a-first"));
            Directory.CreateDirectory(Path.Combine(profiles, "later"));
            candidates.Should().HaveCount(3);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
