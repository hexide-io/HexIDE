using System.Collections.Generic;
using System.Threading.Tasks;
using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using Serilog;

namespace HexIDE.IDE;

/// <inheritdoc cref="ILanguageSwitchService"/>
public sealed class LanguageSwitchService(ILocalizationService localization, IWindowManager windowManager)
    : ILanguageSwitchService
{
    public IReadOnlyList<LanguageInfo> AvailableLanguages => localization.AvailableLanguages;

    public IReadOnlyList<RegionInfo> RegionsFor(string languageId) => localization.RegionsFor(languageId);

    public string ActiveLanguage => localization.ActiveLanguage;

    public void Apply(string id) => localization.Apply(id);

    public string? PendingLanguage { get; private set; }

    public async Task<bool> SwitchWithGateAsync(string newId)
    {
        // One gate at a time. Each gate reverts to the language active when IT opened, so a second one opened
        // on top recorded the first's unconfirmed language as the one to go back to, and the order the two
        // resolved in decided where the IDE ended up (#588). The gate is modal, so only automation could get
        // here twice; the Options page cannot be reached behind it.
        if (PendingLanguage is { } pending)
        {
            Log.Warning("LanguageSwitchService: switch to '{New}' refused; the gate for '{Pending}' is still open",
                newId, pending);
            return false;
        }

        var previousId = localization.ActiveLanguage;
        if (newId == previousId) return true;

        PendingLanguage = newId;
        try
        {
            // Apply live so the user sees the new language while deciding, then gate it.
            localization.Apply(newId);

            var gate = LanguageRevertGateViewModel.Create(localization, previousId, newId);
            var kept = await windowManager.ShowDialog(gate);

            if (!kept)
            {
                localization.Apply(previousId);
                Log.Debug("LanguageSwitchService: '{New}' reverted to '{Prev}'", newId, previousId);
            }

            return kept;
        }
        finally
        {
            PendingLanguage = null;
        }
    }
}
