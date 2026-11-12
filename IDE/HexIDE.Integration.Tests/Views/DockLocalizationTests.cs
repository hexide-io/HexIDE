using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Localization;

namespace HexIDE.Integration.Tests.Views;

public class DockLocalizationTests
{
    [AvaloniaFact]
    public void Dock_menu_access_keys_are_unique_after_regional_overrides()
    {
        // Dock v12.0.0.2, src/Dock.Avalonia.Themes.Fluent/Controls:
        // ToolChromeControl.axaml and ToolTabStripItem.axaml context menus.
        string[][] menus =
        [
            ["ToolChromeControlFloat", "ToolChromeControlDock", "ToolChromeControlDockAsDocument",
                "ToolChromeControlAutoHide", "ToolChromeControlClose"],
            ["ToolTabStripItemFloat", "ToolTabStripItemFloatAll", "ToolTabStripItemDock",
                "ToolTabStripItemDockAsDocument", "ToolTabStripItemAutoHide", "ToolTabStripItemClose"],
            // DocumentTabStripItem.axaml: the context menu and each of its two submenus separately.
            ["DocumentTabStripItemFloat", "DocumentTabStripItemFloatAll", "DocumentTabStripItemClose",
                "DocumentTabStripItemCloseOtherTabs", "DocumentTabStripItemCloseAllTabs",
                "DocumentTabStripItemCloseTabsLeft", "DocumentTabStripItemCloseTabsAbove",
                "DocumentTabStripItemCloseTabsRight", "DocumentTabStripItemCloseTabsBelow",
                "DocumentTabStripItemNewHorizontalDock", "DocumentTabStripItemNewVerticalDock",
                "DocumentTabStripItemTabLayout", "DocumentTabStripItemLayoutMode"],
            ["DocumentTabStripItemTabLayoutLeft", "DocumentTabStripItemTabLayoutTop", "DocumentTabStripItemTabLayoutRight"],
            ["DocumentTabStripItemLayoutModeTabbed", "DocumentTabStripItemLayoutModeMdi"],
            // ToolPinItemControl.axaml context menu.
            ["ToolPinItemControlFloat", "ToolPinItemControlShow", "ToolPinItemControlDockAsDocument", "ToolPinItemControlClose"],
        ];
        var localization = new LocalizationService();
        var collisions = new List<string>();
        foreach (var language in LanguageManifest.Packs.Where(p => p.Id is not
                     ("ar" or "bg" or "el" or "he" or "ja" or "ko" or "ru" or "th" or "uk" or "zh-Hans" or "zh-Hant")))
        {
            var ids = new[] { language.Id }.Concat(LanguageManifest.Regions.TryGetValue(language.Id, out var regions)
                ? regions.Select(r => r.Id) : []);
            foreach (var id in ids)
            {
                localization.TryResolveMap(id, out var strings, out _).Should().BeTrue(id);
                foreach (var menu in menus)
                {
                    var used = new Dictionary<char, string>();
                    foreach (var key in menu)
                    {
                        var label = strings["Str.Dock." + key];
                        var marker = label.IndexOf('_');
                        if (marker < 0) continue;
                        marker.Should().BeLessThan(label.Length - 1, $"{id}: {key} needs a letter after its marker");
                        var accessKey = char.ToUpperInvariant(label[marker + 1]);
                        if (used.TryGetValue(accessKey, out var previous))
                            collisions.Add($"{id}: {previous} and {key} share {accessKey}");
                        else
                            used.Add(accessKey, key);
                    }
                }
            }
        }
        collisions.Should().BeEmpty("siblings in a Dock menu must be invokable by distinct access keys (#3776); affected packs: {0}",
            string.Join(", ", collisions.Select(c => c.Split(':')[0]).Distinct()));
    }

    [AvaloniaFact]
    public void Float_resource_resolves_the_localization_service_string()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Dock.ToolChromeControlFloat").Returns("_Schwebend");
        var resources = new ResourceDictionary();
        DockStrings.Populate(resources, localization);
        Application.Current!.Resources.MergedDictionaries.Add(resources);
        try
        {
            Application.Current.TryGetResource("ToolChromeControlFloatString", null, out var value).Should().BeTrue();
            value.Should().Be("_Schwebend");
        }
        finally
        {
            Application.Current.Resources.MergedDictionaries.Remove(resources);
        }
    }

    [AvaloniaTheory]
    [InlineData("DragPreviewControlDockString", "Dock")]
    [InlineData("DragPreviewControlFloatString", "Float")]
    [InlineData("DragPreviewControlNoneString", "None")]
    [InlineData("DocumentTabStripItemFloatString", "_Float")]
    [InlineData("DocumentTabStripItemFloatAllString", "Float all")]
    [InlineData("DocumentTabStripItemCloseString", "_Close")]
    [InlineData("DocumentTabStripItemCloseOtherTabsString", "Close other tabs")]
    [InlineData("DocumentTabStripItemCloseAllTabsString", "Close all tabs")]
    [InlineData("DocumentTabStripItemCloseTabsLeftString", "Close tabs to the _left")]
    [InlineData("DocumentTabStripItemCloseTabsRightString", "Close tabs to the _right")]
    [InlineData("DocumentTabStripItemCloseTabsAboveString", "Close all tabs _above")]
    [InlineData("DocumentTabStripItemCloseTabsBelowString", "Close all tabs _below")]
    [InlineData("DocumentTabStripItemNewHorizontalDockString", "New Horizontal Document Dock")]
    [InlineData("DocumentTabStripItemNewVerticalDockString", "New Vertical Document Dock")]
    [InlineData("DocumentTabStripItemTabLayoutString", "Tab Layout")]
    [InlineData("DocumentTabStripItemTabLayoutLeftString", "_Place Tabs on the Left")]
    [InlineData("DocumentTabStripItemTabLayoutTopString", "Place Tabs on the _Top")]
    [InlineData("DocumentTabStripItemTabLayoutRightString", "Place Tabs on the _Right")]
    [InlineData("DocumentTabStripItemLayoutModeString", "Layout Mode")]
    [InlineData("DocumentTabStripItemLayoutModeTabbedString", "Tabbed")]
    [InlineData("DocumentTabStripItemLayoutModeMdiString", "MDI")]
    [InlineData("ToolPinItemControlFloatString", "_Float")]
    [InlineData("ToolPinItemControlShowString", "_Show")]
    [InlineData("ToolPinItemControlDockAsDocumentString", "Dock as Tabbed Document")]
    [InlineData("ToolPinItemControlCloseString", "_Close")]
    [InlineData("ToolChromeControlFloatString", "_Float")]
    [InlineData("ToolChromeControlDockString", "_Dock")]
    [InlineData("ToolChromeControlAutoHideString", "_Auto Hide")]
    [InlineData("ToolChromeControlDockAsDocumentString", "Dock as Tabbed Document")]
    [InlineData("ToolChromeControlCloseString", "_Close")]
    [InlineData("ToolTabStripItemFloatString", "_Float")]
    [InlineData("ToolTabStripItemFloatAllString", "Float all")]
    [InlineData("ToolTabStripItemDockString", "_Dock")]
    [InlineData("ToolTabStripItemAutoHideString", "_Auto Hide")]
    [InlineData("ToolTabStripItemDockAsDocumentString", "Dock as Tabbed Document")]
    [InlineData("ToolTabStripItemCloseString", "_Close")]
    public void Dock_resources_follow_startup_language_switch_and_pseudo(string key, string english)
    {
        var localization = new LocalizationService();
        try
        {
            localization.Apply("en");
            Application.Current!.TryGetResource(key, null, out var value).Should().BeTrue();
            value.Should().Be(english);
            localization.Apply("pseudo");
            Application.Current.TryGetResource(key, null, out value).Should().BeTrue();
            value.Should().Be(localization.GetString("Str.Dock." + key[..^6]));
            value.Should().BeOfType<string>().Which.Should().Contain("⟦");
        }
        finally
        {
            localization.Apply("en");
        }
    }
}
