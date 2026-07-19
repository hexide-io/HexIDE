using System.Collections.Generic;
using System.Text.Json.Serialization;
using Classic.CommonControls;

namespace HexIDE.Themes;

public record ThemePackRecord(
    string Name,
    string? Description,
    string? ThemeVariant,
    Dictionary<string, string>? Colors
);

[JsonSerializable(typeof(ThemePackRecord))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class ThemeJsonContext : JsonSerializerContext { }

/// <summary>
/// Maps JSON colour names from a theme pack to Avalonia resource keys.
/// Keys must exactly match what Classic.axaml declares so DynamicResource bindings update live.
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
            // Custom string-keyed brushes
            ["ActiveTitleBar"]          = ("ActiveTitleBarBrush",                        true),
            ["InactiveTitleBar"]        = ("InactiveTitleBarBrush",                      true),
            ["DockTitleBar"]            = ("DockTitleBarBrush",                          true),
            ["SelectionAdorner"]        = ("SelectionAdornerBrush",                      true),
            ["SelectionAdornerBorder"]  = ("SelectionAdornerBorderBrush",                true),
            ["FormGridPattern"]         = ("FormGridPatternBrush",                       true),
            ["ControlSpawnerPhantom"]   = ("ControlSpawnerPhantomBrush",                 true),
            ["RubberBandFill"]          = ("RubberBandFillBrush",                        true),
            ["ColorPaletteBorder"]      = ("ColorPaletteBorderBrush",                    true),
            ["ColorPaletteHighlight"]   = ("ColorPaletteHighlightBrush",                 true),
            // Accent colors stored as Color (not SolidColorBrush), matching Classic.axaml
            ["SystemAccentColor"]       = ("SystemAccentColor",                          false),
            ["SystemAccentColorLight1"] = ("SystemAccentColorLight1",                    false),
            ["SystemAccentColorLight2"] = ("SystemAccentColorLight2",                    false),
            ["SystemAccentColorDark1"]  = ("SystemAccentColorDark1",                     false),
            ["SystemAccentColorDark2"]  = ("SystemAccentColorDark2",                     false),
        };
}
