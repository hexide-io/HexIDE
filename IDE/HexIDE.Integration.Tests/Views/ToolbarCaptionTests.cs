using System;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

public class ToolbarCaptionTests
{
    private static float Read(ICSharpPropertyBag bag, string name)
    {
        bag.TryGetProperty(name, out var value).Should().BeTrue();
        return Convert.ToSingle(value.Value);
    }

    private static async Task<ToolbarItem> Add(VBToolbar bar, string caption) =>
        (ToolbarItem)(await bar.Buttons.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new Vb6Value(caption)]))!.Value.Value!;

    // #1088 issuecomment-6445245253: Segoe UI 9 / 96 dpi oracle extents and button widths.
    // The final two rows miss +7 by one pixel; keep that discrepancy visible, not a caption-specific correction.
    [Theory]
    [InlineData("File", 18, 25, 0)]
    [InlineData("Alpha", 31, 38, 0)]
    [InlineData("Save As", 40, 47, 0)]
    [InlineData("Alphabet", 48, 55, 0)]
    [InlineData("iiiiiiii", 24, 31, 0)]
    [InlineData("MMMMMMMM", 88, 95, 0)]
    [InlineData("A much longer caption here", 148, 155, 0)]
    [InlineData("&File", 28, 35, 0)]
    [InlineData("Tabx", 24, 32, 1)]
    [InlineData("Twolines", 45, 53, 1)]
    public void Measured_extents_follow_rule_with_two_recorded_one_pixel_discrepancies(string caption, double extent, int measured, int discrepancy)
    {
        VBToolbar.CaptionWidth(caption, extent, true).Should().Be(measured - discrepancy);
    }

    private const string OracleFontDigest = "ba32a222b23d727267cf1aba4e5296fe84ce99b9d910915103fc085d7931bc88";

    internal static string? OracleFontSkipReason(byte[] font)
    {
        var digest = Convert.ToHexStringLower(SHA256.HashData(font));
        return digest == OracleFontDigest ? null : $"Requires oracle segoeui.ttf SHA-256 {OracleFontDigest}; found {digest}";
    }

    [Fact]
    public void Different_font_build_reports_its_digest()
    {
        byte[] font = [1, 2, 3];
        OracleFontSkipReason(font).Should().Contain(Convert.ToHexStringLower(SHA256.HashData(font)))
            .And.Contain(OracleFontDigest);
    }

    [AvaloniaFact]
    public async Task Segoe_UI_nine_point_matches_console_measurements()
    {
        if (!OperatingSystem.IsWindows()) throw Xunit.Sdk.SkipException.ForSkip("Requires the oracle's Windows Segoe UI font build");
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");
        if (!File.Exists(path)) throw Xunit.Sdk.SkipException.ForSkip($"Oracle font file absent: {path}");
        var reason = OracleFontSkipReason(File.ReadAllBytes(path));
        if (reason != null) throw Xunit.Sdk.SkipException.ForSkip(reason);
        var family = new FontFamily("Segoe UI");
        (FontManager.Current.TryGetGlyphTypeface(new Typeface(family), out var glyphs) ? glyphs.FamilyName : null)
            .Should().Be("Segoe UI", "the oracle-font run must use the actual font, never a fallback");
        var bar = new VBToolbar { FontFamily = family, FontSize = 12 };
        (string Caption, int Width)[] cases =
        [
            ("", 23), ("A", 23), ("i", 23), ("W", 23), ("OK", 23), ("Ab", 23),
            ("File", 25), ("Alpha", 38), ("Save As", 47), ("Alphabet", 55),
            ("iiiiiiii", 31), ("MMMMMMMM", 95), ("A much longer caption here", 155),
            ("&File", 35), ("Tabx", 32), ("Twolines", 53),
        ];
        foreach (var (caption, measured) in cases)
        {
            await bar.Buttons.CallAsync("Clear", []);
            var item = await Add(bar, caption);
            // TextWidth's integers predict one pixel less for Tabx/Twolines. Keep the exact oracle
            // results here without caption-specific corrections.
            Read(item, "Width").Should().Be(measured * 15, "the console measured {0}", caption);
        }
    }

    [AvaloniaFact]
    public async Task Windows_caption_width_follows_installed_fonts_Gdi_extent()
    {
        if (!OperatingSystem.IsWindows()) throw Xunit.Sdk.SkipException.ForSkip("Requires Windows GDI");
        var bar = new VBToolbar { FontFamily = new FontFamily("Segoe UI"), FontSize = 12 };
        foreach (var caption in new[] { "OK", "iiiiiiii", "MMMMMMMM", "A much longer caption here", "Twolines", "&File" })
        {
            await bar.Buttons.CallAsync("Clear", []);
            var item = await Add(bar, caption);
            var extent = ToolbarGdiProbe.Measure(caption);
            Read(item, "Width").Should().Be(Math.Max(23, extent + 7) * 15);
            bar.TrySetProperty("TextAlignment", new Vb6Value(1));
            Read(item, "Width").Should().Be(Math.Max(23, extent + 7) * 15 + 300);
            bar.TrySetProperty("TextAlignment", new Vb6Value(0));
        }
    }

    private static double GlyphExtent(VBToolbar bar, string caption)
    {
        if (OperatingSystem.IsWindows())
            return ToolbarCaptionMetrics.Measure(caption, new Typeface(bar.FontFamily), bar.FontSize);
        var glyphs = new Typeface(bar.FontFamily).GlyphTypeface;
        return caption.EnumerateRunes().Sum(rune =>
        {
            glyphs.TryGetHorizontalGlyphAdvance(glyphs.CharacterToGlyphMap.GetGlyph(rune.Value), out var advance);
            return Math.Round(advance * bar.FontSize / glyphs.Metrics.DesignEmHeight);
        });
    }

    [AvaloniaFact]
    public async Task Caption_extent_rounds_each_glyph_before_summing()
    {
        var bar = new VBToolbar { FontSize = 12 };
        var glyphs = new Typeface(bar.FontFamily).GlyphTypeface;
        var character = "iMWabc&".First(c =>
        {
            glyphs.TryGetHorizontalGlyphAdvance(glyphs.CharacterToGlyphMap.GetGlyph(c), out var units);
            var advance = units * bar.FontSize / glyphs.Metrics.DesignEmHeight;
            return Math.Round(advance) > 0 && Math.Abs(Math.Round(advance) - advance) > 0.05;
        });
        var caption = new string(character, 40);
        var extent = VBToolbar.PortableCaptionExtent(caption, new Typeface(bar.FontFamily), bar.FontSize);
        using var layout = new TextLayout(caption, new Typeface(bar.FontFamily), bar.FontSize, Brushes.Black);
        extent.Should().NotBe(Math.Ceiling(layout.WidthIncludingTrailingWhitespace));
        var item = await Add(bar, caption);
        extent = GlyphExtent(bar, caption);
        Read(item, "Width").Should().Be((float)((extent + 7) * 15));
        bar.TrySetProperty("TextAlignment", new Vb6Value(1));
        Read(item, "Width").Should().Be((float)((extent + 27) * 15));
    }

    [AvaloniaFact]
    public async Task Captions_share_host_font_width_and_follow_edits_removal_and_clear()
    {
        var bar = new VBToolbar { Width = 800 };
        var a = await Add(bar, "A");
        var wide = await Add(bar, "A much longer caption here");
        var extent = GlyphExtent(bar, "A much longer caption here");
        var expected = (float)(Math.Max(23, extent + 7) * 15);
        Read(a, "Width").Should().Be(expected);
        Read(wide, "Left").Should().Be(expected);
        Read(bar, "ButtonHeight").Should().Be(38 * 15);
        wide.TrySetProperty("Caption", new Vb6Value("A"));
        Read(a, "Width").Should().Be(23 * 15);
        wide.TrySetProperty("Caption", new Vb6Value("A much longer caption here"));
        await bar.Buttons.CallAsync("Remove", [new Vb6Value(2)]);
        Read(a, "Width").Should().Be(23 * 15);
        await bar.Buttons.CallAsync("Clear", []);
        Read(bar, "ButtonWidth").Should().Be(23 * 15);
        Read(bar, "ButtonHeight").Should().Be(38 * 15);
        await Add(bar, "");
        Read(bar, "ButtonHeight").Should().Be(22 * 15);
        bar.TrySetProperty("TextAlignment", new Vb6Value(1));
        Read(bar, "ButtonWidth").Should().Be(43 * 15);
        var right = await Add(bar, "A much longer caption here");
        Read(right, "Width").Should().Be(expected + 20 * 15);
        Read(right, "Top").Should().Be(30);
        Read(right, "Height").Should().Be(22 * 15);
    }

    [AvaloniaFact]
    public async Task Ampersand_counts_in_extent_and_draws_as_a_mnemonic_and_flat_dropdown_adds_fifteen()
    {
        var bar = new VBToolbar { Width = 800 };
        var item = await Add(bar, "&File");
        var extent = GlyphExtent(bar, "&File");
        Read(item, "Width").Should().Be((float)(Math.Max(23, extent + 7) * 15));
        var window = new Window { Content = bar };
        try
        {
            window.Show();
            window.UpdateLayout();
            var access = bar.GetVisualDescendants().OfType<AccessText>().Single();
            access.Text.Should().Be("_File");
            access.ShowAccessKey.Should().BeTrue();
            bar.TrySetProperty("Style", new Vb6Value(1));
            var normalWidth = Read(item, "Width");
            item.TrySetProperty("Style", new Vb6Value(5));
            Read(item, "Width").Should().Be(normalWidth + 225);
            item.TrySetProperty("Style", new Vb6Value(1));
            Read(item, "Width").Should().Be(normalWidth);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Tabx", "Tab\tx", 8)]
    [InlineData("Twolines", "Two\r\nlines", 9)]
    [InlineData("Twolines", "Two\nlines", 8)]
    [InlineData("Twolines", "Two\rlines", 1)]
    public async Task Control_characters_add_measured_width_but_draw_on_one_line(string plain, string caption, int extra)
    {
        var bar = new VBToolbar();
        var item = await Add(bar, plain);
        var before = Read(item, "Width");
        item.TrySetProperty("Caption", new Vb6Value(caption));
        Read(item, "Width").Should().Be(before + extra * 15);
        var window = new Window { Content = bar };
        try
        {
            window.Show();
            window.UpdateLayout();
            bar.GetVisualDescendants().OfType<Button>().Single().Content.Should().Be(plain);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Narrow_bars_wrap_and_unwrap_in_model_and_visuals()
    {
        var bar = new VBToolbar { Width = 50 };
        var items = new ToolbarItem[6];
        for (var i = 0; i < items.Length; i++) items[i] = await Add(bar, "A");
        Read(items[2], "Left").Should().Be(0);
        Read(items[2], "Top").Should().Be(40 * 15);
        Read(bar, "Height").Should().Be(118 * 15);
        var window = new Window { Content = bar };
        try
        {
            window.Show();
            window.UpdateLayout();
            var buttons = bar.GetVisualDescendants().OfType<Button>().ToArray();
            buttons[2].Bounds.Y.Should().Be(40);
            buttons[2].Bounds.Width.Should().Be(23);
            bar.TrySetProperty("Wrappable", new Vb6Value(false));
            Read(items[5], "Left").Should().Be(115 * 15);
            Read(bar, "Height").Should().Be(42 * 15);
            bar.TrySetProperty("Style", new Vb6Value(1));
            Read(items[0], "Top").Should().Be(0);
            Read(bar, "Height").Should().Be(38 * 15);
        }
        finally { window.Close(); }
    }
}
