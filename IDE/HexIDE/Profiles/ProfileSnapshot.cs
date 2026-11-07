using System;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using HexIDE.Localization;

namespace HexIDE.Profiles;

// JsonElement clones own their data and expose no mutation; adapters never reopen bundle files.
internal sealed record ProfilePayload(string File, JsonElement Content);

internal sealed record ProfileSnapshot(
    int SchemaVersion,
    string Id,
    string Source,
    ImmutableArray<string>? ProjectTypes,
    FrozenDictionary<string, bool> MenuVisibility,
    FrozenDictionary<string, bool> ToolbarVisibility,
    ProfilePayload? Keymap,
    ProfilePayload? Theme,
    FrozenDictionary<string, ProfilePayload> LanguageOverrides,
    ProfilePayload? SettingsDefaults);

internal enum ProfileError
{
    InvalidSelector,
    InvalidManifest,
    InvalidPayload,
    InvalidPath,
    MissingFile,
    UnreadableFile
}

// Structured attribution is retained for the startup adapter; no raw exception text is shown to users.
internal sealed class ProfileLoadException(string source, string file, ProfileError error, Exception? inner = null)
    : Exception(error.ToString(), inner)
{
    public string SourceId { get; } = source;
    public string File { get; } = file;
    public ProfileError Error { get; } = error;

    public string GetLocalizedMessage(ILocalizationService localization) => string.Format(
        localization.GetString("Str.Profiles.LoadError"), SourceId, File);
}
