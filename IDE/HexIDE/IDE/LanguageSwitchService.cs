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

    public async Task<bool> SwitchWithGateAsync(string newId)
    {
        var previousId = localization.ActiveLanguage;
        if (newId == previousId) return true;

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
}
