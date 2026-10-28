using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using HexIDE.Keymaps;
using HexIDE.Localization;
using HexIDE.Themes;

namespace HexIDE.IDE.Profiles;

// Captured JSON is detached from its documents. Pack/default adapters remain a separate delivery.
// This assembler is not an atomic filesystem snapshot and must not be wired to consumers yet.
internal sealed record ProfileBundle(ProfileSnapshot Manifest, ImmutableDictionary<string, JsonElement> Files);

internal static class ProfileBundleLoader
{
    public static ProfileBundle ResolveName(string userDataDirectory, string id)
    {
        var root = Path.Combine(Path.GetFullPath(userDataDirectory), "profiles");
        if (!ProfileCatalog.IsBundleId(id))
            throw Error(root, "id", "InvalidId");
        var builtin = ProfileCatalog.Builtins.FirstOrDefault(profile => profile.Id == id);
        return builtin == null
            ? Load(Path.Combine(root, id), id)
            : new ProfileBundle(builtin, ImmutableDictionary<string, JsonElement>.Empty);
    }

    public static ProfileBundle Load(string directory, string? installedId = null)
    {
        directory = Path.GetFullPath(directory);
        var manifestJson = Read("profile.json", "$");
        var manifest = ProfileCatalog.ParseDirectoryManifest(manifestJson, directory, installedId);
        var files = ImmutableDictionary.CreateBuilder<string, JsonElement>(StringComparer.Ordinal);
        Capture(manifest.Keymap, "keymap");
        Capture(manifest.Theme, "theme");
        Capture(manifest.SettingsDefaults, "settingsDefaults");
        foreach (var entry in manifest.LanguageOverrides)
            Capture(entry.Value, "languageOverrides." + entry.Key);
        return new ProfileBundle(manifest, files.ToImmutable());

        JsonElement Read(string reference, string field)
        {
            var file = Path.Combine(directory, Path.Combine(ProfilePaths.Components(reference)));
            byte[] bytes;
            try { bytes = ProfileFileReader.Read(directory, reference); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                throw Error(file, field, error.Message is "UnsafePath" or "TooLarge" ? error.Message : "UnreadablePath");
            }
            try
            {
                using var document = JsonDocument.Parse(bytes);
                return document.RootElement.Clone();
            }
            catch (JsonException) { throw Error(file, field, "InvalidJson"); }
        }

        void Capture(string? reference, string field)
        {
            if (reference == null)
                return;
            var file = Path.Combine(directory, Path.Combine(ProfilePaths.Components(reference)));
            var json = files.TryGetValue(reference, out var existing) ? existing : Read(reference, field);
            if (json.ValueKind != JsonValueKind.Object)
                throw Error(file, field, "InvalidType");
            if (field == "settingsDefaults")
                ValidateSettings(json, file);
            else
            {
                try
                {
                    // Use the existing record formats; per-entry warn-and-skip rules belong to adapters.
                    if (field == "keymap")
                        _ = json.Deserialize(KeymapJsonContext.Default.KeymapPackRecord);
                    else if (field == "theme")
                        _ = json.Deserialize(ThemeJsonContext.Default.ThemePackRecord);
                    else
                        _ = json.Deserialize(LocalizationJsonContext.Default.LanguagePackRecord);
                }
                catch (JsonException) { throw Error(file, field, "InvalidPayload"); }
            }
            files[reference] = json;
        }
    }

    private static void ValidateSettings(JsonElement json, string file)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in json.EnumerateObject())
        {
            var field = "settingsDefaults." + property.Name;
            if (!seen.Add(property.Name))
                throw Error(file, field, "DuplicateField");
            var kind = property.Value.ValueKind;
            var valid = property.Name switch
            {
                "tabWidth" or "gridWidth" or "gridHeight" =>
                    kind == JsonValueKind.Number && property.Value.TryGetInt32(out _),
                "activeTheme" or "activeKeymap" or "activeLanguage" => kind == JsonValueKind.String,
                "requireVariableDeclaration" or "autoListMembers" or "autoQuickInfo" or "autoIndent"
                    or "formatOnSave" or "showGrid" or "alignToGrid" or "promptForProjectOnStartup"
                    or "isStandardToolbarVisible" or "isEditToolbarVisible" or "isDebugToolbarVisible"
                    or "isFormEditorToolbarVisible" or "isMinimapVisible" =>
                    kind is JsonValueKind.True or JsonValueKind.False,
                _ => throw Error(file, field, "DisallowedSetting")
            };
            if (!valid)
                throw Error(file, field, "InvalidType");
        }
    }

    private static InvalidDataException Error(string file, string field, string code)
    {
        var error = new InvalidDataException(code);
        error.Data["file"] = file;
        error.Data["field"] = field;
        error.Data["code"] = code;
        return error;
    }
}
