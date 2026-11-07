using System.Collections.Generic;
using System.Collections.Immutable;

namespace HexIDE.IDE.Profiles;

// File references are declarations, not permission to open a file. The loader must validate them first.
internal sealed class ProfileSnapshot
{
    public int SchemaVersion { get; }
    public string Id { get; }
    public string Source { get; }
    public string? Keymap { get; }
    public string? Theme { get; }
    public string? SettingsDefaults { get; }
    public ImmutableDictionary<string, string> LanguageOverrides { get; }
    public ImmutableArray<string>? ProjectTypes { get; }
    public ImmutableDictionary<string, bool> MenuVisibility { get; }
    public ImmutableDictionary<string, bool> ToolbarVisibility { get; }

    public ProfileSnapshot(int schemaVersion, string id, string source,
        string? keymap = null, string? theme = null, string? settingsDefaults = null,
        IEnumerable<KeyValuePair<string, string>>? languageOverrides = null,
        IEnumerable<string>? projectTypes = null,
        IEnumerable<KeyValuePair<string, bool>>? menuVisibility = null,
        IEnumerable<KeyValuePair<string, bool>>? toolbarVisibility = null)
    {
        SchemaVersion = schemaVersion;
        Id = id;
        Source = source;
        Keymap = keymap;
        Theme = theme;
        SettingsDefaults = settingsDefaults;
        LanguageOverrides = languageOverrides?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, string>.Empty;
        ProjectTypes = projectTypes?.ToImmutableArray();
        MenuVisibility = menuVisibility?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, bool>.Empty;
        ToolbarVisibility = toolbarVisibility?.ToImmutableDictionary(StringComparer.Ordinal)
            ?? ImmutableDictionary<string, bool>.Empty;
    }
}
