using Dock.Model.Core;

namespace HexIDE.Localization;

/// <summary>
/// Helpers for localizing the dock-tab titles of tool windows / document tabs. A dockable's
/// <c>Title</c> is a code-set string (not AXAML), so it is localized here rather than via
/// <c>{DynamicResource}</c>.
/// </summary>
public static class LocalizationToolExtensions
{
    /// <summary>
    /// Sets the dockable's tab <see cref="IDockable.Title"/> from the language pack and re-applies it on
    /// every language change, so the tab header updates live alongside the rest of the chrome.
    /// </summary>
    public static void BindTitle(this ILocalizationService localization, IDockable dockable, string key)
    {
        dockable.Title = localization.GetString(key);
        localization.LanguageChanged += () => dockable.Title = localization.GetString(key);
    }
}
