using System.Collections.Generic;
using System.Threading.Tasks;
using HexIDE.Localization;

namespace HexIDE.IDE;

/// <summary>
/// Coordinates IDE-language changes that go through the live-apply + countdown-revert gate.
/// Wraps <see cref="ILocalizationService"/> (the pack mechanism) and <see cref="IWindowManager"/>
/// (to show the gate), so both the Options Language page and automation (the MCP server) drive the
/// exact same confirmation flow.
/// </summary>
public interface ILanguageSwitchService
{
    /// <summary>The selectable languages (Language combo).</summary>
    IReadOnlyList<LanguageInfo> AvailableLanguages { get; }

    /// <summary>Regions under a language (dependent Region combo); empty for none / system / pseudo.</summary>
    IReadOnlyList<RegionInfo> RegionsFor(string languageId);

    /// <summary>Id of the currently-applied language.</summary>
    string ActiveLanguage { get; }

    /// <summary>Apply a language immediately with no confirmation gate (e.g. startup, Cancel revert).</summary>
    void Apply(string id);

    /// <summary>
    /// The language whose confirmation gate is open, or null when none is. While one is open,
    /// <see cref="SwitchWithGateAsync"/> refuses another switch.
    /// </summary>
    string? PendingLanguage { get; }

    /// <summary>
    /// Apply <paramref name="newId"/> live, then show the bilingual countdown-revert gate. Returns
    /// <c>true</c> if the user kept the change; on Revert or timeout the previous language is restored
    /// and <c>false</c> is returned. A no-op (returns true) when <paramref name="newId"/> is already active.
    /// Refused, with nothing changed and <c>false</c> returned, while another gate is open
    /// (<see cref="PendingLanguage"/>).
    /// </summary>
    Task<bool> SwitchWithGateAsync(string newId);
}
