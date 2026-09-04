using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Shapes = Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit.Highlighting;
using Avalonia.VisualTree;
using Classic.Avalonia.Theme;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Themes;

namespace HexIDE.Integration.Tests.Controls;

/// <summary>
/// Guards two render defects that shipped invisibly.
///
/// <para><b>The option button killed the process.</b> Classic.Avalonia.Theme 11.3.0.3 was compiled
/// against Avalonia 11, and its ClassicBorderDecorator.DrawRadioButtonBorder called
/// StreamGeometryContext.ArcTo(Point, Size, double, bool, SweepDirection) — an overload Avalonia 12
/// replaced. Every VB6 option button therefore threw MissingMethodException on the render thread, in
/// the designer and at runtime. It is NOT catchable in the app, and nothing reached the log, so the
/// assertion here is simply "a frame came back and we are still running".</para>
///
/// <para><b>The check box was always ticked.</b> SimpleTheme's CheckBox theme drives a part it calls
/// Path#checkMark; HexIDE's copied template names its tick CheckMarkPath, so the visibility rules
/// never matched and the tick painted in every state.</para>
///
/// <para>These are a class of bug — a precompiled-against-Avalonia-11 assembly failing only at render
/// time — that compiles cleanly and cannot be caught by unit tests of view models. Rendering for real
/// under Skia is the only thing that finds them.</para>
/// </summary>
public class ClassicRenderTests
{
    /// <summary>
    /// A window carrying the VB6 control themes plus the SystemColors brushes they resolve. The bare
    /// SimpleTheme test app has neither, exactly as noted in TranslationEditorViewIntegrationTests.
    /// </summary>
    private static Window BuildHost(Control content)
    {
        var window = new Window { Width = 240, Height = 120, Background = Brushes.White };

        window.Resources.MergedDictionaries.Add(
            new ResourceInclude(new Uri("avares://HexIDE.Integration.Tests/"))
            {
                Source = new Uri("avares://HexIDE.Runtime/BuiltinControls/Resources.axaml"),
            });

        window.Resources[Classic.CommonControls.SystemColors.WindowBrushKey] = new SolidColorBrush(Colors.White);
        window.Resources[Classic.CommonControls.SystemColors.WindowTextBrushKey] = new SolidColorBrush(Colors.Black);
        window.Resources[Classic.CommonControls.SystemColors.GrayTextBrushKey] = new SolidColorBrush(Color.Parse("#808080"));

        window.Content = content;
        return window;
    }

    private static Avalonia.Media.Imaging.WriteableBitmap? RenderOnce(Control content)
    {
        var window = BuildHost(content);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        window.Close();
        return frame;
    }

    // ── The crash ────────────────────────────────────────────────────────────────────────────────

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void OptionButton_Renders_WithoutKillingTheProcess(bool? isChecked)
    {
        // Before the package bump this did not fail an assertion — it terminated the test host.
        var frame = RenderOnce(new VBOptionButton { Content = "Standard delivery", IsChecked = isChecked });
        frame.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void OptionButton_Flat_Renders()
    {
        // The Flat appearance swaps to the FlatVBOptionButton theme, which is BasedOn the crashing one.
        var frame = RenderOnce(new VBOptionButton { Content = "Express", Appearance = VBAppearance.Flat });
        frame.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void CheckBox_Renders_WithoutKillingTheProcess()
    {
        var frame = RenderOnce(new VBCheckBox { Content = "Signature required" });
        frame.Should().NotBeNull();
    }

    // ── The always-ticked check box ──────────────────────────────────────────────────────────────

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(null, true)]   // VB6 Value = 2 (Greyed): tick shown, in grey
    public void CheckBox_TickVisibility_FollowsState(bool? isChecked, bool expectedVisible)
    {
        var box = new VBCheckBox { Content = "Priority handling", IsChecked = isChecked };
        var window = BuildHost(box);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tick = box.GetVisualDescendants().OfType<Shapes.Path>()
            .FirstOrDefault(p => p.Name == "CheckMarkPath");

        tick.Should().NotBeNull("the check box template must still contain CheckMarkPath");
        // Before the fix this was true in every state — an unticked box rendered ticked.
        tick!.IsVisible.Should().Be(expectedVisible);

        window.Close();
    }

    [AvaloniaFact]
    public void CheckBox_Glyph_StaysThirteenSquare_WhenTickHidden()
    {
        // Hiding the tick collapsed the decorator to its border, because it was measured from its
        // child. The explicit 13x13 is what keeps an unticked box the same size as a ticked one.
        var box = new VBCheckBox { Content = "Priority handling", IsChecked = false };
        var window = BuildHost(box);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var glyph = box.GetVisualDescendants().OfType<ClassicBorderDecorator>()
            .FirstOrDefault(d => d.Name == "CheckMark");

        glyph.Should().NotBeNull();
        glyph!.Bounds.Width.Should().Be(13);
        glyph.Bounds.Height.Should().Be(13);

        window.Close();
    }

    // ── The dark palette ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One test, not several: the highlighting definition is process-wide static, so parallel facts
    /// mutating the variant would race each other.
    /// </summary>
    [AvaloniaFact]
    public void SyntaxPalette_SwapsWithThemeVariant_AndSwapsBack()
    {
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        var definition = SyntaxHighlightingTheme.Definition;

        static Color ForegroundOf(AvaloniaEdit.Highlighting.IHighlightingDefinition d, string name)
        {
            var brush = d.GetNamedColor(name)!.Foreground!.GetBrush(null!);
            return ((ISolidColorBrush)brush).Color;
        }

        try
        {
            app.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();
            ForegroundOf(definition, "Keyword").Should().Be(Color.Parse("#0000B0"),
                "the light palette is the xshd's own and must survive a round trip");

            app.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();

            // #0000B0 on the Dark pack's editor background (#252526) is ~1.1:1 — invisible.
            ForegroundOf(definition, "Keyword").Should().Be(Color.Parse("#7AA6F0"));
            ForegroundOf(definition, "Comment").Should().Be(Color.Parse("#6AAE6A"));
            ForegroundOf(definition, "String").Should().Be(Color.Parse("#E08A8A"));

            app.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();
            ForegroundOf(definition, "Keyword").Should().Be(Color.Parse("#0000B0"));
        }
        finally
        {
            app.RequestedThemeVariant = original;
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// The guard that would have caught the original bug before it shipped: every named colour must
    /// clear WCAG AA against the editor background of both dark packs (Dark #252526, Abyss #000C18).
    /// The editor background is SystemColors.WindowBrushKey — see CodeEditorView.axaml.
    /// </summary>
    [AvaloniaFact]
    public void DarkPalette_ClearsContrastAA_OnBothDarkPacks()
    {
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        var definition = SyntaxHighlightingTheme.Definition;

        var names = new[]
        {
            "Comment", "String", "Number", "Constant",
            "Keyword", "IOKeyword", "StorageModifier", "Type", "Function",
        };

        try
        {
            app.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();

            foreach (var background in new[] { Color.Parse("#252526"), Color.Parse("#000C18") })
            {
                foreach (var name in names)
                {
                    var brush = definition.GetNamedColor(name)?.Foreground?.GetBrush(null!);
                    if (brush is not ISolidColorBrush solid)
                        continue;

                    ContrastRatio(solid.Color, background).Should().BeGreaterThanOrEqualTo(4.5,
                        $"'{name}' must be legible on {background}");
                }
            }
        }
        finally
        {
            app.RequestedThemeVariant = original;
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void AnAdoptedBundledDefinitionIsLegibleOnADarkBackground()
    {
        // The VB6 definition gets a hand-tuned dark palette. A definition resolved by file extension from
        // AvaloniaEdit's bundle cannot: the set is open-ended, so its dark colours are DERIVED. This is the
        // parallel guarantee — a derived palette must clear the same bar the hand-tuned one was built to.
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        var background = Color.Parse("#252526");

        var definition = HighlightingManager.Instance.GetDefinitionByExtension(".md");
        definition.Should().NotBeNull("AvaloniaEdit is expected to bundle a Markdown definition");

        // THE CONTROL, and it has to come first. Every assertion below is "contrast is sufficient", which
        // would pass trivially against a definition that happened to be light already — proving nothing
        // about the derivation. So: establish that the STOCK palette genuinely fails on a dark background
        // before adopting it. If this ever stops failing, the test below has stopped testing anything.
        app.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        var stockFailures = definition!.NamedHighlightingColors
            .Select(c => c.Foreground?.GetBrush(null!) as ISolidColorBrush)
            .Where(b => b is not null)
            .Count(b => ContrastRatio(b!.Color, background) < 4.5);
        stockFailures.Should().BeGreaterThan(0,
            "the stock bundled palette is authored for a light background — if it were already legible "
          + "on dark, this test would prove nothing about the derived palette");

        try
        {
            SyntaxHighlightingTheme.Adopt(definition);
            app.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();

            foreach (var color in definition.NamedHighlightingColors)
            {
                if (color.Foreground?.GetBrush(null!) is not ISolidColorBrush solid)
                    continue;

                ContrastRatio(solid.Color, background).Should().BeGreaterThanOrEqualTo(4.5,
                    $"'{color.Name}' must be legible on {background} after adoption");
            }
        }
        finally
        {
            app.RequestedThemeVariant = original;
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void AdoptionPreservesHueSoColoursStillMeanWhatTheyMeant()
    {
        // Lightness is lifted; hue is not touched. That matters because hue carries the meaning a reader
        // has learned — blue is a link, green is a comment — and a re-tint that scrambles it would trade
        // one legibility problem for a worse one.
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;
        var definition = HighlightingManager.Instance.GetDefinitionByExtension(".md")!;

        try
        {
            app.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();
            var lightHues = definition.NamedHighlightingColors
                .Where(c => c.Name is not null && c.Foreground?.GetBrush(null!) is ISolidColorBrush)
                .ToDictionary(
                    c => c.Name!,
                    c => ((ISolidColorBrush)c.Foreground!.GetBrush(null!)!).Color.ToHsl());

            SyntaxHighlightingTheme.Adopt(definition);
            app.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();

            foreach (var color in definition.NamedHighlightingColors)
            {
                if (color.Name is null || !lightHues.TryGetValue(color.Name, out var light)) continue;
                if (color.Foreground?.GetBrush(null!) is not ISolidColorBrush solid) continue;
                // Greys have no meaningful hue, so exclude them rather than assert nonsense about them.
                if (light.S < 0.05) continue;

                solid.Color.ToHsl().H.Should().BeApproximately(light.H, 1.0,
                    $"'{color.Name}' should keep its hue — only its lightness is lifted");
            }
        }
        finally
        {
            app.RequestedThemeVariant = original;
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>WCAG 2.x relative-luminance contrast ratio.</summary>
    private static double ContrastRatio(Color a, Color b)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color c) =>
            0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

        var (l1, l2) = (Luminance(a), Luminance(b));
        var (hi, lo) = l1 >= l2 ? (l1, l2) : (l2, l1);
        return (hi + 0.05) / (lo + 0.05);
    }
}
