using System;
using System.Collections.Generic;

namespace HexIDE.Localization;

/// <summary>
/// The per-user <b>override tier</b> that merges last in localization resolution
/// (<c>en ← shipped-neutral ← shipped-region ← user(neutral) ← user(region)</c>). Overrides are stored
/// per-locale under <c>%AppData%/HexIDE/translations/{id}.json</c> (never in the shipped packs) and are
/// validated on load and on write so a placeholder-breaking or empty override can never reach resolution.
/// <para>
/// Kept deliberately free of <see cref="ILocalizationService"/> (it loads <c>en.json</c> itself to obtain
/// the canonical map) so there is no DI cycle: <c>LocalizationService</c> depends on this, not vice versa.
/// </para>
/// </summary>
public interface IUserTranslationsService
{
    /// <summary>(Re)scans the translations directory, dropping orphan/empty/placeholder-breaking entries.
    /// Also invoked from the constructor so startup overrides apply before the first resolve.</summary>
    void LoadAll();

    /// <summary>The validated overrides for one locale id (empty if none).</summary>
    IReadOnlyDictionary<string, string> GetOverrides(string id);

    /// <summary>
    /// Sets (or, for a null/whitespace value, clears) an override for <paramref name="key"/> on locale
    /// <paramref name="id"/>. Returns <c>false</c> without persisting if the value breaks the canonical
    /// key's placeholder set; <c>true</c> on a successful set or clear. Persists and raises
    /// <see cref="OverridesChanged"/> on success.
    /// </summary>
    bool SetOverride(string id, string key, string? value);

    /// <summary>Removes one override key (deleting the locale file if it empties) and raises
    /// <see cref="OverridesChanged"/>.</summary>
    void ClearOverride(string id, string key);

    /// <summary>Removes all overrides for a locale (and its file) and raises
    /// <see cref="OverridesChanged"/>.</summary>
    void ResetAll(string id);

    /// <summary>The locale ids that currently carry at least one override.</summary>
    IReadOnlyCollection<string> OverriddenIds { get; }

    /// <summary>
    /// True if <paramref name="candidate"/> preserves the integer-index placeholder set of the canonical
    /// (<c>en</c>) value for <paramref name="canonicalKey"/>. A key absent from <c>en</c> imposes no
    /// placeholder requirement.
    /// </summary>
    bool PreservesPlaceholders(string canonicalKey, string candidate);

    /// <summary>Raised (with the affected locale id) after any override mutation persists.</summary>
    event Action<string>? OverridesChanged;
}
