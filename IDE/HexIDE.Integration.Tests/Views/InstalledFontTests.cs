using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A VB6 font is drawn in the family it names when that family is installed (#1132): Courier New in Courier New, not in
/// the one sans serif every form used to get. The family is looked up among the fonts this machine has, so the test picks
/// one of them rather than assuming any particular font exists on every platform the suite runs on.
/// </summary>
public class InstalledFontTests
{
    private static string Resolved(FontFamily family) =>
        FontManager.Current.TryGetGlyphTypeface(new Typeface(family), out var typeface)
            ? typeface.FamilyName
            : throw new InvalidOperationException($"'{family}' resolves to nothing at all");

    [AvaloniaFact]
    public void A_font_the_machine_has_is_drawn_in_that_font()
    {
        var fallback = Resolved(new VBFont("No Such Font Anywhere", 12).ToFontFamily());
        var installed = FontManager.Current.SystemFonts.Select(f => f.Name)
            .Where(name => !string.Equals(name, fallback, StringComparison.OrdinalIgnoreCase))
            .Where(name => FontManager.Current.TryGetGlyphTypeface(new Typeface(name), out var t)
                           && string.Equals(t.FamilyName, name, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();
        installed.Should().NotBeEmpty("the machine has fonts other than the fallback, or this test proves nothing");

        foreach (var name in installed)
            Resolved(new VBFont(name, 12).ToFontFamily()).Should().BeEquivalentTo(name,
                "a form that names an installed family draws in it, as VB6 does");
    }

    [AvaloniaFact]
    public void A_font_the_machine_lacks_still_draws_in_a_fallback()
    {
        // VB6's own default, MS Sans Serif, is the case that matters: proprietary, not shipped, and a bitmap font where
        // it is installed. A form on it must still draw.
        Resolved(new VBFont("No Such Font Anywhere", 12).ToFontFamily()).Should().NotBeNullOrEmpty();
        Resolved(VBFont.Default.ToFontFamily()).Should().NotBeNullOrEmpty();
    }
}
