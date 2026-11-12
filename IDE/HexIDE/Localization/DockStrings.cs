using Avalonia.Controls;

namespace HexIDE.Localization;

internal static class DockStrings
{
    // Dock 12.0.0.2 ControlStrings.axaml, excluding its developer-only RootDockDebug labels.
    private static readonly string[] ResourceKeys =
    [
        "DragPreviewControlDockString",
        "DragPreviewControlFloatString",
        "DragPreviewControlNoneString",
        "DocumentTabStripItemFloatString",
        "DocumentTabStripItemFloatAllString",
        "DocumentTabStripItemCloseString",
        "DocumentTabStripItemCloseOtherTabsString",
        "DocumentTabStripItemCloseAllTabsString",
        "DocumentTabStripItemCloseTabsLeftString",
        "DocumentTabStripItemCloseTabsRightString",
        "DocumentTabStripItemCloseTabsAboveString",
        "DocumentTabStripItemCloseTabsBelowString",
        "DocumentTabStripItemNewHorizontalDockString",
        "DocumentTabStripItemNewVerticalDockString",
        "DocumentTabStripItemTabLayoutString",
        "DocumentTabStripItemTabLayoutLeftString",
        "DocumentTabStripItemTabLayoutTopString",
        "DocumentTabStripItemTabLayoutRightString",
        "DocumentTabStripItemLayoutModeString",
        "DocumentTabStripItemLayoutModeTabbedString",
        "DocumentTabStripItemLayoutModeMdiString",
        "ToolPinItemControlFloatString",
        "ToolPinItemControlShowString",
        "ToolPinItemControlDockAsDocumentString",
        "ToolPinItemControlCloseString",
        "ToolChromeControlFloatString",
        "ToolChromeControlDockString",
        "ToolChromeControlAutoHideString",
        "ToolChromeControlDockAsDocumentString",
        "ToolChromeControlCloseString",
        "ToolTabStripItemFloatString",
        "ToolTabStripItemFloatAllString",
        "ToolTabStripItemDockString",
        "ToolTabStripItemAutoHideString",
        "ToolTabStripItemDockAsDocumentString",
        "ToolTabStripItemCloseString",
    ];

    internal static void Populate(ResourceDictionary resources, ILocalizationService localization)
    {
        foreach (var key in ResourceKeys)
            resources[key] = localization.GetString("Str.Dock." + key[..^6]);
    }
}
