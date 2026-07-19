using System.Collections.Generic;

namespace HexIDE.Themes;

/// <summary>
/// Applies and tracks the active IDE chrome theme.
/// The form designer canvas is locked to Light theme via a
/// ThemeVariantScope in FormEditView.axaml and is not affected by this service.
/// </summary>
public interface IThemeService
{
    string ActiveTheme { get; }
    IReadOnlyList<string> AvailableThemes { get; }

    /// <summary>Switch the IDE chrome to <paramref name="themeName"/> at runtime. No restart required.</summary>
    void Apply(string themeName);
}
