using System;
using System.Collections.Frozen;
using System.IO;
using System.Linq;
using System.Text.Json;
using HexIDE.IDE.Profiles;
using HexIDE.Keymaps;
using HexIDE.Themes;

namespace HexIDE.Profiles;

/// <summary>Resolves selectors and adapts captured bundles without changing session services.</summary>
internal sealed class ProfileResolver(string userDataDirectory, string invocationDirectory)
{
    public ProfileSnapshot? Resolve(string? selector)
    {
        selector ??= "vb6";
        if (selector == "none")
            return null;
        if (IsPath(selector))
        {
            try
            {
                var directory = Path.GetFullPath(HostPath(selector), invocationDirectory);
                return Load(directory, () => ProfileBundleLoader.Load(directory));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                throw new ProfileLoadException(selector, "profile.json", ProfileError.InvalidPath, ex);
            }
        }
        if (!ProfileCatalog.IsBundleId(selector))
            throw new ProfileLoadException(selector, "profile.json", ProfileError.InvalidSelector);
        if (ProfileCatalog.Builtins.Any(profile => profile.Id == selector))
            return Load("embedded:" + selector, () => ProfileBundleLoader.ResolveName(userDataDirectory, selector));
        var installed = Path.GetFullPath(Path.Combine(userDataDirectory, "profiles", selector));
        try
        {
            // Enumerate actual names so a case-insensitive filesystem cannot broaden ordinal lookup.
            if (!ProfileCatalog.DiscoverInstalled(userDataDirectory).Any(profile => profile.Id == selector))
                throw new ProfileLoadException(installed, "profile.json", ProfileError.MissingFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ProfileLoadException(installed, "profile.json", ProfileError.UnreadableFile, ex);
        }
        return Load(installed, () => ProfileBundleLoader.ResolveName(userDataDirectory, selector));
    }

    private static bool IsPath(string value) => Path.IsPathRooted(value) || value.Contains('/') || value.Contains('\\') || value.StartsWith('.');
    private static string HostPath(string value) => value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    private static ProfileSnapshot Load(string source, Func<ProfileBundle> load)
    {
        ProfileBundle bundle;
        try { bundle = load(); }
        catch (InvalidDataException ex)
        {
            var path = ex.Data["file"] as string ?? Path.Combine(source, "profile.json");
            var file = Path.GetRelativePath(source, path);
            var code = ex.Data["code"] as string;
            var error = code switch
            {
                "UnsafePath" => ProfileError.InvalidPath,
                "UnreadablePath" => System.IO.File.Exists(path) ? ProfileError.UnreadableFile : ProfileError.MissingFile,
                _ => file == "profile.json" ? ProfileError.InvalidManifest : ProfileError.InvalidPayload
            };
            throw new ProfileLoadException(source, file, error, ex);
        }
        var manifest = bundle.Manifest;
        var keymap = Payload(manifest.Keymap);
        var theme = Payload(manifest.Theme);
        // Keep adapter-level validation before any service applies the captured snapshot.
        Validate(keymap, payload => KeymapService.PrepareProfileKeymap(source, payload));
        Validate(theme, payload => ThemeService.PrepareProfileTheme(source, payload));
        return new ProfileSnapshot(manifest.SchemaVersion, manifest.Id, source, manifest.ProjectTypes,
            manifest.MenuVisibility.ToFrozenDictionary(StringComparer.Ordinal),
            manifest.ToolbarVisibility.ToFrozenDictionary(StringComparer.Ordinal), keymap, theme,
            manifest.LanguageOverrides.ToFrozenDictionary(entry => entry.Key, entry => Payload(entry.Value)!, StringComparer.Ordinal),
            Payload(manifest.SettingsDefaults));

        ProfilePayload? Payload(string? file) => file == null ? null : new ProfilePayload(file, bundle.Files[file]);

        void Validate(ProfilePayload? payload, Action<ProfilePayload> prepare)
        {
            if (payload == null)
                return;
            try { prepare(payload); }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException)
            {
                throw new ProfileLoadException(source, payload.File, ProfileError.InvalidPayload, ex);
            }
        }
    }
}
