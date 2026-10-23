using Avalonia.Headless.XUnit;
using Avalonia.Media;
using SkiaSharp;

namespace HexIDE.Integration.Tests.Controls;

/// <summary>
/// The font a headless test draws in when it names none (#1578). The themes ask for "fonts:Inter#Inter, $Default", and
/// HexIDE registers no Inter, so text falls to the default family, which the platform names. A family with no Bold face
/// throws "Could not create glyphTypeface" at the first bold run, but the font collection caches a face that another path
/// found, character fallback among them, so whether a test threw depended on which tests had run before it. It did on
/// macOS, once, in a run of 1899.
/// </summary>
public class HeadlessFontTests
{
    // Asked of Skia directly, which no cache stands in front of, so the answer does not depend on the tests run before.
    [AvaloniaFact]
    public void The_default_font_family_has_a_bold_face()
    {
        var family = FontManager.Current.DefaultFontFamily.Name;

        using var bold = SKFontManager.Default.MatchFamily(family, SKFontStyle.Bold);

        bold.Should().NotBeNull($"a bold run in the default family, {family}, must find a face");
    }
}
