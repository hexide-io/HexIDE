using System.Text.Json;
using System.Xml.Linq;
using Avalonia.Media;
using HexIDE.Themes;

namespace HexIDE.Tests.Themes;

/// <summary>
/// The code window's read-only text colour in every theme the IDE ships (hexide-io/HexIDE#273 task 3.15).
/// </summary>
/// <remarks>
/// <para>
/// <b>Read from the files, not restated.</b> Each pack is held to the background it declares itself, so a pack
/// that changes its <c>Window</c> colour is checked against the new one. The existing dark-palette test names its
/// two backgrounds as literals, which a change to either pack would leave passing.
/// </para>
/// <para>
/// <b>Every shipped pack carries the key</b>, because one that omits it inherits Classic's grey, which is chosen
/// for a white editor and misses the bar on a dark one. A pack nobody ships may still omit it, as the theme-packs
/// capability allows; the cost is the inherited grey.
/// </para>
/// </remarks>
public class ShippedThemePackTests
{
    private const string Key = "ReadOnlyText";

    /// <summary>The contrast bar the dark syntax palette was tuned to: WCAG AA for body text.</summary>
    private const double Legible = 4.5;

    /// <summary>
    /// How far read-only text must stand from the pack's own text to read as different. The project's own floor,
    /// not a WCAG figure: there is no standard for telling two text colours apart, and a grey equal to the text
    /// would pass the legibility bar while greying nothing.
    /// </summary>
    private const double Distinct = 1.5;

    public static TheoryData<string> Packs => new() { "Classic", "Dark", "Abyss" };

    [Fact]
    public void ThePacksTestedHereAreTheOnesTheIdeShips()
    {
        new ThemeService().AvailableThemes.Should().BeEquivalentTo(Packs.Select(row => (string)row.Data));
    }

    [Fact]
    public void AThemePackCanNameTheKey()
    {
        // Without a mapping a pack's value is logged as unknown and dropped, and the default applies.
        ColorKeyMapping.Table.Should().ContainKey(Key);
        ColorKeyMapping.Table[Key].Should().Be(("ReadOnlyTextBrush", true));
        ClassicDefault(ColorKeyMapping.Table[Key].Key).Should().NotBeNull("Classic.axaml declares the default");
    }

    [Theory]
    [MemberData(nameof(Packs))]
    public void EveryShippedPackCarriesTheKey(string pack)
    {
        ColoursOf(pack).Should().ContainKey(Key);
    }

    [Fact]
    public void TheClassicReferencePackMatchesTheDefault()
    {
        // Classic.json is never loaded: Classic.axaml is what Classic renders. The reference copy is kept in step
        // so that it is a true account of the theme.
        Color.Parse(ColoursOf("Classic")[Key]).Should().Be(ClassicDefault("ReadOnlyTextBrush"));
    }

    [Theory]
    [MemberData(nameof(Packs))]
    public void ReadOnlyTextIsLegibleOnThePacksOwnEditor(string pack)
    {
        var (readOnly, window, _) = Resolved(pack);

        SyntaxHighlightingTheme.ContrastRatio(readOnly, window).Should().BeGreaterThanOrEqualTo(Legible,
            $"{pack}'s read-only text {readOnly} must be legible on its editor background {window}");
    }

    [Theory]
    [MemberData(nameof(Packs))]
    public void ReadOnlyTextStandsApartFromThePacksOwnText(string pack)
    {
        var (readOnly, _, text) = Resolved(pack);

        SyntaxHighlightingTheme.ContrastRatio(readOnly, text).Should().BeGreaterThanOrEqualTo(Distinct,
            $"{pack}'s read-only text {readOnly} has to read as different from its text {text}");
    }

    /// <summary>
    /// The colours a pack renders: its own where it names them, Classic's where it does not, as the pack's
    /// dictionary sits over Classic.axaml in the running IDE. Classic itself renders Classic.axaml alone.
    /// </summary>
    private static (Color ReadOnly, Color Window, Color Text) Resolved(string pack)
    {
        var colours = pack == "Classic" ? [] : ColoursOf(pack);
        Color Of(string name, string resourceKey) =>
            colours.TryGetValue(name, out var hex) ? Color.Parse(hex) : ClassicDefault(resourceKey)!.Value;

        return (Of(Key, "ReadOnlyTextBrush"), Of("Window", "WindowBrushKey"), Of("WindowText", "WindowTextBrushKey"));
    }

    private static Dictionary<string, string> ColoursOf(string pack)
    {
        var record = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(ThemesDirectory(), "Packs", pack + ".json")),
            ThemeJsonContext.Default.ThemePackRecord);
        return record?.Colors ?? [];
    }

    /// <summary>
    /// The colour Classic.axaml gives a brush key: a string key, or the name of a <c>SystemColors</c> key, which
    /// that file writes as <c>{x:Static commonControls:SystemColors.NameBrushKey}</c>.
    /// </summary>
    private static Color? ClassicDefault(object key)
    {
        var name = key.ToString()!;
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var brush in XDocument.Load(Path.Combine(ThemesDirectory(), "Classic.axaml")).Descendants()
                     .Where(e => e.Name.LocalName == "SolidColorBrush"))
        {
            var declared = (string?)brush.Attribute(x + "Key");
            if (declared == name || declared == $"{{x:Static commonControls:SystemColors.{name}}}")
                return Color.Parse((string)brush.Attribute("Color")!);
        }
        return null;
    }

    private static string ThemesDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var themes = Path.Combine(dir.FullName, "IDE", "HexIDE", "Themes");
            if (Directory.Exists(themes)) return themes;
        }

        throw new DirectoryNotFoundException("Could not locate IDE/HexIDE/Themes from " + AppContext.BaseDirectory);
    }
}
