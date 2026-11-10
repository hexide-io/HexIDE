using System.Collections.Generic;
using System.Text.Json.Serialization;
using Classic.CommonControls;

namespace HexIDE.Themes;

public record ThemePackRecord(
    string Name,
    string? Description,
    string? ThemeVariant,
    Dictionary<string, string>? Colors,
    double? PlaceholderOpacity = null,
    double? TabSelectionBorderThickness = null,
    double? ChromeBorderThickness = null
);

[JsonSerializable(typeof(ThemePackRecord))]
[JsonSerializable(typeof(string[]))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class ThemeJsonContext : JsonSerializerContext { }

/// <summary>
/// Maps JSON colour names from a theme pack to Avalonia resource keys.
/// Keys match Classic.axaml or the base themes so DynamicResource bindings update live.
/// </summary>
internal static class ColorKeyMapping
{
    // (resourceKey, isBrush): isBrush=true stores SolidColorBrush; false stores Color (for accent keys)
    internal static readonly IReadOnlyDictionary<string, (object Key, bool IsBrush)> Table =
        new Dictionary<string, (object Key, bool IsBrush)>
        {
            // SystemColors brush keys (Classic.CommonControls static properties)
            ["AppWorkspace"]            = (SystemColors.AppWorkspaceBrushKey,           true),
            ["Window"]                  = (SystemColors.WindowBrushKey,                 true),
            ["WindowText"]              = (SystemColors.WindowTextBrushKey,              true),
            ["ActiveCaptionText"]       = (SystemColors.ActiveCaptionTextBrushKey,       true),
            ["Control"]                 = (SystemColors.ControlBrushKey,                true),
            ["ControlText"]             = (SystemColors.ControlTextBrushKey,             true),
            ["ControlDark"]             = (SystemColors.ControlDarkBrushKey,             true),
            ["ControlLight"]            = (SystemColors.ControlLightBrushKey,            true),
            ["ControlLightLight"]       = (SystemColors.ControlLightLightBrushKey,       true),
            ["ControlDarkDark"]         = (SystemColors.ControlDarkDarkBrushKey,         true),
            ["Menu"]                    = (SystemColors.MenuBrushKey,                    true),
            ["MenuText"]                = (SystemColors.MenuTextBrushKey,                true),
            ["Highlight"]               = (SystemColors.HighlightBrushKey,              true),
            ["HighlightText"]           = (SystemColors.HighlightTextBrushKey,           true),
            ["Desktop"]                 = (SystemColors.DesktopBrushKey,                true),
            ["GrayText"]                = (SystemColors.GrayTextBrushKey,               true),
            ["InactiveCaption"]         = (SystemColors.InactiveCaptionBrushKey,         true),
            ["InactiveCaptionText"]     = (SystemColors.InactiveCaptionTextBrushKey,     true),
            ["WindowFrame"]             = (SystemColors.WindowFrameBrushKey,             true),
            ["ScrollBar"]               = (SystemColors.ScrollBarBrushKey,               true),
            ["ActiveCaption"]           = (SystemColors.ActiveCaptionBrushKey,           true),
            ["ActiveBorder"]            = (SystemColors.ActiveBorderBrushKey,            true),
            ["InactiveBorder"]          = (SystemColors.InactiveBorderBrushKey,          true),
            ["InfoText"]                = (SystemColors.InfoTextBrushKey,                true),
            ["Info"]                    = (SystemColors.InfoBrushKey,                    true),
            // Custom string-keyed brushes
            ["ActiveTitleBar"]          = ("ActiveTitleBarBrush",                        true),
            ["InactiveTitleBar"]        = ("InactiveTitleBarBrush",                      true),
            ["DockTitleBar"]            = ("DockTitleBarBrush",                          true),
            ["SelectionAdorner"]        = ("SelectionAdornerBrush",                      true),
            ["SelectionAdornerBorder"]  = ("SelectionAdornerBorderBrush",                true),
            ["DesignerHiddenControlMask"] = ("DesignerHiddenControlMaskBrush",             true),
            ["FormGridPattern"]         = ("FormGridPatternBrush",                       true),
            ["ControlSpawnerPhantom"]   = ("ControlSpawnerPhantomBrush",                 true),
            ["RubberBandFill"]          = ("RubberBandFillBrush",                        true),
            ["ColorPaletteBorder"]      = ("ColorPaletteBorderBrush",                    true),
            ["ColorPaletteHighlight"]   = ("ColorPaletteHighlightBrush",                 true),
            // Added to Classic.axaml after this table was written, so no theme pack could override them:
            // a light-amber banner and red validation text sat on #1E1E1E chrome in Dark and Abyss. The
            // read-only banner makes that far more visible, so it is fixed here rather than inherited.
            ["WarningBannerBackground"] = ("WarningBannerBackgroundBrush",                true),
            ["WarningBannerBorder"]     = ("WarningBannerBorderBrush",                    true),
            ["WarningBannerForeground"] = ("WarningBannerForegroundBrush",                true),
            ["ValidationError"]         = ("ValidationErrorBrush",                        true),
            ["LinkText"]                = ("LinkTextBrush",                               true),
            ["InactiveCodeText"]        = ("InactiveCodeTextBrush",                        true),
            // The code window's read-only text and fold labels (#273 task 3.15). Every shipped pack carries it,
            // because the light default misses the contrast bar on a dark editor; ShippedThemePackTests checks.
            ["ReadOnlyText"]            = ("ReadOnlyTextBrush",                           true),
            ["IndentGuide"] = ("IndentGuideBrush", true),
            ["ActiveIndentGuide"] = ("ActiveIndentGuideBrush", true),
            ["BracketDepth1"] = ("BracketDepth1Brush", true),
            ["BracketDepth2"] = ("BracketDepth2Brush", true),
            ["BracketDepth3"] = ("BracketDepth3Brush", true),
            ["BracketUnmatched"] = ("BracketUnmatchedBrush", true),
            // Base-theme and Dock resources used by chrome outside the Classic palette.
            ["ThemeBackgroundBrush"] = ("ThemeBackgroundBrush", true),
            ["ThemeForegroundBrush"] = ("ThemeForegroundBrush", true),
            ["ThemeForegroundLowBrush"] = ("ThemeForegroundLowBrush", true),
            ["ThemeControlMidBrush"] = ("ThemeControlMidBrush", true),
            ["ThemeControlMidHighBrush"] = ("ThemeControlMidHighBrush", true),
            ["ThemeControlHighlightMidBrush"] = ("ThemeControlHighlightMidBrush", true),
            ["ThemeAccentBrush2"] = ("ThemeAccentBrush2", true),
            ["ThemeAccentBrush3"] = ("ThemeAccentBrush3", true),
            ["ThemeAccentBrush4"] = ("ThemeAccentBrush4", true),
            ["DockThemeBackgroundBrush"] = ("DockThemeBackgroundBrush", true),
            ["DockThemeControlBackgroundBrush"] = ("DockThemeControlBackgroundBrush", true),
            ["DockDocumentTabStripBackgroundBrush"] = ("DockDocumentTabStripBackgroundBrush", true),
            ["DockSurfacePanelBrush"] = ("DockSurfacePanelBrush", true),
            ["DockSurfaceHeaderBrush"] = ("DockSurfaceHeaderBrush", true),
            ["DockSurfaceSidebarBrush"] = ("DockSurfaceSidebarBrush", true),
            ["DockTabBackgroundBrush"] = ("DockTabBackgroundBrush", true),
            ["DockTabActiveBackgroundBrush"] = ("DockTabActiveBackgroundBrush", true),
            ["DockTabForegroundBrush"] = ("DockTabForegroundBrush", true),
            ["DockTabSelectedForegroundBrush"] = ("DockTabSelectedForegroundBrush", true),
            ["DockTabActiveForegroundBrush"] = ("DockTabActiveForegroundBrush", true),
            ["LineNumbersForeground"] = ("LineNumbersForegroundBrush", true),
            // Accent colors stored as Color (not SolidColorBrush), matching Classic.axaml
            ["SystemAccentColor"]       = ("SystemAccentColor",                          false),
            ["SystemAccentColorLight1"] = ("SystemAccentColorLight1",                    false),
            ["SystemAccentColorLight2"] = ("SystemAccentColorLight2",                    false),
            ["SystemAccentColorDark1"]  = ("SystemAccentColorDark1",                     false),
            ["SystemAccentColorDark2"]  = ("SystemAccentColorDark2",                     false),
        };
}
