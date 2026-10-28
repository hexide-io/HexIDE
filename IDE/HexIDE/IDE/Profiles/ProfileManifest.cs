using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using HexIDE.Localization;

namespace HexIDE.IDE.Profiles;

// Validates declarations only. File references remain untrusted until a bundle loader contains them.
internal sealed class ProfileManifest(string source)
{
    private static readonly ImmutableHashSet<string> Fields = ImmutableHashSet.Create(StringComparer.Ordinal,
        "schemaVersion", "id", "keymap", "theme", "languageOverrides", "projectTypes", "menuVisibility",
        "toolbarVisibility", "settingsDefaults");
    private static readonly ImmutableHashSet<string> ProjectIds = ImmutableHashSet.Create(StringComparer.Ordinal,
        "standard-exe", "activex-exe", "activex-dll", "activex-control", "application-wizard",
        "wizard-manager", "data-project", "enterprise-controls");
    private static readonly ImmutableHashSet<string> MenuIds = ImmutableHashSet.Create(StringComparer.Ordinal,
        "project.references", "tools.references");
    private static readonly ImmutableHashSet<string> ToolbarIds = ImmutableHashSet.Create(StringComparer.Ordinal,
        "standard", "edit", "debug", "form-editor");
    private static readonly ImmutableHashSet<string> LocaleIds = LanguageManifest.Packs.Select(x => x.Id)
        .Concat(LanguageManifest.Regions.Values.SelectMany(x => x.Select(region => region.Id)))
        .ToImmutableHashSet(StringComparer.Ordinal);

    public ProfileSnapshot Parse(JsonElement manifest, string? installedId)
    {
        foreach (var property in Members(manifest, "$"))
            if (!Fields.Contains(property.Name))
                throw Error(property.Name, "UnknownField");

        var version = Required(manifest, "schemaVersion");
        if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var schemaVersion))
            throw Error("schemaVersion", "InvalidType");
        if (schemaVersion != 1)
            throw Error("schemaVersion", "UnsupportedVersion");
        var id = Text(Required(manifest, "id"), "id");
        if (!ProfileCatalog.IsBundleId(id))
            throw Error("id", "InvalidId");
        if (installedId != null && !string.Equals(id, installedId, StringComparison.Ordinal))
            throw Error("id", "FolderMismatch");

        return new ProfileSnapshot(schemaVersion, id, source,
            keymap: OptionalFile(manifest, "keymap"), theme: OptionalFile(manifest, "theme"),
            settingsDefaults: OptionalFile(manifest, "settingsDefaults"),
            languageOverrides: Languages(manifest), projectTypes: Projects(manifest),
            menuVisibility: Visibility(manifest, "menuVisibility", MenuIds),
            toolbarVisibility: Visibility(manifest, "toolbarVisibility", ToolbarIds));
    }

    private JsonElement Required(JsonElement manifest, string field) =>
        manifest.TryGetProperty(field, out var value) ? value : throw Error(field, "RequiredField");

    private IEnumerable<JsonProperty> Members(JsonElement value, string field)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw Error(field, "InvalidType");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            var path = field == "$" ? property.Name : field + "." + property.Name;
            if (!seen.Add(property.Name))
                throw Error(path, "DuplicateField");
            yield return property;
        }
    }

    private string Text(JsonElement value, string field)
    {
        if (value.ValueKind != JsonValueKind.String)
            throw Error(field, "InvalidType");
        return value.GetString()!;
    }

    private string FileReference(JsonElement value, string field)
    {
        var text = Text(value, field);
        if (string.IsNullOrWhiteSpace(text))
            throw Error(field, "InvalidValue");
        if (!ProfilePaths.IsSafeReference(text))
            throw Error(field, "UnsafePath");
        return text;
    }

    private string? OptionalFile(JsonElement manifest, string field) =>
        manifest.TryGetProperty(field, out var value) ? FileReference(value, field) : null;

    private IEnumerable<KeyValuePair<string, string>> Languages(JsonElement manifest)
    {
        if (!manifest.TryGetProperty("languageOverrides", out var value))
            yield break;
        foreach (var property in Members(value, "languageOverrides"))
        {
            var field = "languageOverrides." + property.Name;
            if (!LocaleIds.Contains(property.Name))
                throw Error(field, "UnknownId");
            yield return new(property.Name, FileReference(property.Value, field));
        }
    }

    private IEnumerable<string>? Projects(JsonElement manifest)
    {
        if (!manifest.TryGetProperty("projectTypes", out var value))
            return null;
        if (value.ValueKind != JsonValueKind.Array)
            throw Error("projectTypes", "InvalidType");
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in value.EnumerateArray())
        {
            var field = $"projectTypes[{result.Count}]";
            var id = Text(entry, field);
            if (!ProjectIds.Contains(id))
                throw Error(field, "UnknownId");
            if (!seen.Add(id))
                throw Error(field, "DuplicateId");
            result.Add(id);
        }
        return result;
    }

    private IEnumerable<KeyValuePair<string, bool>> Visibility(JsonElement manifest, string field,
        ImmutableHashSet<string> ids)
    {
        if (!manifest.TryGetProperty(field, out var value))
            yield break;
        foreach (var property in Members(value, field))
        {
            var path = field + "." + property.Name;
            if (!ids.Contains(property.Name))
                throw Error(path, "UnknownId");
            if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw Error(path, "InvalidType");
            yield return new(property.Name, property.Value.GetBoolean());
        }
    }

    public void ValidatePaths(ProfileSnapshot snapshot, string directory)
    {
        Check(snapshot.Keymap, "keymap");
        Check(snapshot.Theme, "theme");
        Check(snapshot.SettingsDefaults, "settingsDefaults");
        foreach (var entry in snapshot.LanguageOverrides)
            Check(entry.Value, "languageOverrides." + entry.Key);

        void Check(string? reference, string field)
        {
            if (reference == null)
                return;
            try
            {
                if (ProfilePaths.HasLink(directory, reference))
                    throw Error(field, "UnsafePath");
            }
            catch (IOException)
            {
                throw Error(field, "UnreadablePath");
            }
            catch (UnauthorizedAccessException)
            {
                throw Error(field, "UnreadablePath");
            }
        }
    }

    // Machine-readable diagnostics; a future UI adapter must localize the code, file and field.
    private InvalidDataException Error(string field, string code)
    {
        var error = new InvalidDataException(code);
        error.Data["file"] = source;
        error.Data["field"] = field;
        error.Data["code"] = code;
        return error;
    }
}
