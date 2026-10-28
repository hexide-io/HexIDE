using System.IO;
using System.Text.Json;
using HexIDE.IDE.Profiles;

namespace HexIDE.Tests.Profiles;

public class ProfileManifestTests
{
    [Theory]
    [InlineData("null", "$", "InvalidType")]
    [InlineData("[]", "$", "InvalidType")]
    [InlineData("{\"extra\":1}", "extra", "UnknownField")]
    [InlineData("{\"SchemaVersion\":1}", "SchemaVersion", "UnknownField")]
    [InlineData("{}", "schemaVersion", "RequiredField")]
    [InlineData("{\"schemaVersion\":1}", "id", "RequiredField")]
    [InlineData("{\"schemaVersion\":2,\"id\":\"example\"}", "schemaVersion", "UnsupportedVersion")]
    [InlineData("{\"schemaVersion\":\"1\",\"id\":\"example\"}", "schemaVersion", "InvalidType")]
    [InlineData("{\"schemaVersion\":1.5,\"id\":\"example\"}", "schemaVersion", "InvalidType")]
    [InlineData("{\"schemaVersion\":1,\"id\":null}", "id", "InvalidType")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"none\"}", "id", "InvalidId")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"Example\"}", "id", "InvalidId")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"other\"}", "id", "FolderMismatch")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"example\",\"id\":\"example\"}", "id", "DuplicateField")]
    public void InvalidManifestIsAttributed(string json, string field, string code)
    {
        using var document = JsonDocument.Parse(json);
        Action parse = () => ProfileCatalog.ParseManifest(document.RootElement, "bundle/profile.json", "example");
        var error = parse.Should().Throw<InvalidDataException>().Which;
        error.Data["file"].Should().Be("bundle/profile.json");
        error.Data["field"].Should().Be(field);
        error.Data["code"].Should().Be(code);
    }

    [Theory]
    [InlineData("\"keymap\":null", "keymap", "InvalidType")]
    [InlineData("\"theme\":4", "theme", "InvalidType")]
    [InlineData("\"settingsDefaults\":\"\"", "settingsDefaults", "InvalidValue")]
    [InlineData("\"languageOverrides\":[]", "languageOverrides", "InvalidType")]
    [InlineData("\"languageOverrides\":{\"zz\":\"zz.json\"}", "languageOverrides.zz", "UnknownId")]
    [InlineData("\"languageOverrides\":{\"en\":false}", "languageOverrides.en", "InvalidType")]
    [InlineData("\"languageOverrides\":{\"en\":\"a\",\"en\":\"b\"}", "languageOverrides.en", "DuplicateField")]
    [InlineData("\"projectTypes\":false", "projectTypes", "InvalidType")]
    [InlineData("\"projectTypes\":[null]", "projectTypes[0]", "InvalidType")]
    [InlineData("\"projectTypes\":[\"invented\"]", "projectTypes[0]", "UnknownId")]
    [InlineData("\"projectTypes\":[\"standard-exe\",\"standard-exe\"]", "projectTypes[1]", "DuplicateId")]
    [InlineData("\"menuVisibility\":{\"unknown\":true}", "menuVisibility.unknown", "UnknownId")]
    [InlineData("\"menuVisibility\":{\"project.references\":null}", "menuVisibility.project.references", "InvalidType")]
    [InlineData("\"toolbarVisibility\":{\"standard.open\":true}", "toolbarVisibility.standard.open", "UnknownId")]
    [InlineData("\"toolbarVisibility\":{\"standard\":true,\"standard\":false}", "toolbarVisibility.standard", "DuplicateField")]
    public void InvalidOptionalMemberIsAttributed(string member, string field, string code)
    {
        InvalidManifestIsAttributed("{\"schemaVersion\":1,\"id\":\"example\"," + member + "}", field, code);
    }

    [Fact]
    public void OptionalMembersAreCopiedWithoutOpeningAnyFile()
    {
        ProfileSnapshot profile;
        using (var document = JsonDocument.Parse("""
            {"schemaVersion":1,"id":"example","keymap":"missing-keymap.json","theme":"missing-theme.json",
             "settingsDefaults":"missing-settings.json","languageOverrides":{"en":"translations/en.json"},
             "projectTypes":["activex-dll","standard-exe"],"menuVisibility":{"tools.references":false},
             "toolbarVisibility":{"standard":true,"edit":false,"debug":true,"form-editor":false}}
            """))
            profile = ProfileCatalog.ParseManifest(document.RootElement, "bundle/profile.json", "example");
        profile.Source.Should().Be("bundle/profile.json");
        profile.Keymap.Should().Be("missing-keymap.json");
        profile.Theme.Should().Be("missing-theme.json");
        profile.SettingsDefaults.Should().Be("missing-settings.json");
        profile.LanguageOverrides["en"].Should().Be("translations/en.json");
        profile.ProjectTypes!.Value.Should().Equal("activex-dll", "standard-exe");
        profile.MenuVisibility["tools.references"].Should().BeFalse();
        profile.ToolbarVisibility.Should().HaveCount(4);
    }

    [Fact]
    public void MinimalManifestPreservesOmissionAndEmptyArrayOffersNothing()
    {
        using var minimal = JsonDocument.Parse("""{"schemaVersion":1,"id":"example"}""");
        var profile = ProfileCatalog.ParseManifest(minimal.RootElement, "source", "example");
        profile.ProjectTypes.Should().BeNull();
        profile.MenuVisibility.Should().BeEmpty();
        profile.ToolbarVisibility.Should().BeEmpty();
        profile.LanguageOverrides.Should().BeEmpty();
        using var empty = JsonDocument.Parse("""{"schemaVersion":1,"id":"example","projectTypes":[]}""");
        ProfileCatalog.ParseManifest(empty.RootElement, "source", "example").ProjectTypes!.Value.Should().BeEmpty();
    }
}
