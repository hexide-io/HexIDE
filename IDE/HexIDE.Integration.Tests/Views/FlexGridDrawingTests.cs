using System;
using System.Linq;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

public class FlexGridDrawingTests
{
    private static void Set(VBMSFlexGrid grid, string name, int value) =>
        FlexGridMembers.Write(grid, name, [], new Vb6Value(value));

    private static VBMSFlexGrid Grid()
    {
        var grid = new VBMSFlexGrid { Width = 260, Height = 140 };
        grid.Model.Rows = 3;
        grid.Model.Cols = 3;
        Set(grid, "BackColor", 0xffffff);
        Set(grid, "ForeColor", 0);
        Set(grid, "BackColorFixed", 0x00ff00);
        Set(grid, "BackColorBkg", 0x808080);
        Set(grid, "BackColorSel", 0xff0000);
        Set(grid, "GridLines", 0);
        Set(grid, "GridLinesFixed", 0);
        Set(grid, "FocusRect", 0);
        return grid;
    }

    private static int[,] Draw(VBMSFlexGrid grid)
    {
        grid.Measure(new Size(grid.Width, grid.Height));
        grid.Arrange(new Rect(0, 0, grid.Width, grid.Height));
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)grid.Width, (int)grid.Height));
        bitmap.Render(grid);
        var bytes = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(default, bitmap.PixelSize), handle.AddrOfPinnedObject(), bytes.Length, bitmap.PixelSize.Width * 4); }
        finally { handle.Free(); }
        var result = new int[bitmap.PixelSize.Width, bitmap.PixelSize.Height];
        var rgba = bitmap.Format == PixelFormats.Rgba8888;
        for (var y = 0; y < bitmap.PixelSize.Height; y++)
            for (var x = 0; x < bitmap.PixelSize.Width; x++)
            {
                var i = (y * bitmap.PixelSize.Width + x) * 4;
                result[x, y] = rgba ? bytes[i] << 16 | bytes[i + 1] << 8 | bytes[i + 2]
                    : bytes[i + 2] << 16 | bytes[i + 1] << 8 | bytes[i];
            }
        return result;
    }

    private static Rect Ink(int[,] pixels, int left = 64, int top = 16, int width = 64, int height = 16)
    {
        var points = (from y in Enumerable.Range(top, height)
                      from x in Enumerable.Range(left, width)
                      where pixels[x, y] < 0x808080
                      select new Point(x - left, y - top)).ToArray();
        points.Should().NotBeEmpty("the cell must actually draw its text");
        return new Rect(new Point(points.Min(p => p.X), points.Min(p => p.Y)),
            new Point(points.Max(p => p.X) + 1, points.Max(p => p.Y) + 1));
    }

    [AvaloniaFact]
    public void Default_grid_draws_cells_and_text_instead_of_a_blank_control()
    {
        var grid = new VBMSFlexGrid { Width = 260, Height = 140 };
        grid.Model.Text = "visible";
        var pixels = Draw(grid);
        pixels[84, 24].Should().NotBe(pixels[240, 120]);
        var colours = (from y in Enumerable.Range(17, 14)
                       from x in Enumerable.Range(65, 62) select pixels[x, y]).Distinct().Count();
        colours.Should().BeGreaterThan(2, "glyphs draw inside the body cell");
    }

    [AvaloniaTheory]
    [InlineData("CellFontBold")]
    [InlineData("CellFontItalic")]
    [InlineData("CellFontUnderline")]
    [InlineData("CellFontStrikeThrough")]
    public void Cell_font_flags_change_the_rendered_text(string member)
    {
        var grid = Grid();
        grid.Model.Text = "Text";
        var before = Draw(grid).Cast<int>().ToArray();
        FlexGridMembers.Write(grid, member, [], new Vb6Value(true));
        Draw(grid).Cast<int>().Should().NotEqual(before);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Underline_stays_inside_a_cell_shorter_than_its_font_metrics(int alignment)
    {
        var grid = Grid();
        grid.Model.Text = "Text";
        const double size = 18;
        var typeface = new Typeface(FontFamily.Default);
        FlexGridMembers.Write(grid, "CellFontName", [], new Vb6Value(typeface.FontFamily.Name));
        FlexGridMembers.Write(grid, "CellFontSize", [], new Vb6Value(size * 72 / 96));
        grid.Model.SetColAlignment(1, alignment);
        var metrics = typeface.GlyphTypeface.Metrics;
        var scale = size / metrics.DesignEmHeight;
        var formatted = new FormattedText("Text", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, size, Brushes.Black);
        var underlineTop = formatted.Baseline + (metrics.UnderlinePosition - metrics.UnderlineThickness / 2.0) * scale;
        var textHeight = (int)Math.Floor(underlineTop) - 1;
        grid.Model.SetRowHeight(1, (textHeight + 4) * 15);
        var before = Draw(grid);
        Ink(before, height: textHeight + 4);
        FlexGridMembers.Write(grid, "CellFontUnderline", [], new Vb6Value(true));
        var after = Draw(grid);
        var changed = (from y in Enumerable.Range(0, (int)grid.Height)
                       from x in Enumerable.Range(0, (int)grid.Width)
                       where before[x, y] != after[x, y]
                       select new Point(x, y)).ToArray();
        changed.Should().NotBeEmpty("the font's natural underline falls entirely below the text clip");
        changed.Should().OnlyContain(p => p.X >= 66 && p.X < 126 && p.Y >= 18 && p.Y < 18 + textHeight,
            "the underline must stay inside the current cell's text area");
    }

    [AvaloniaFact]
    public void Gridlines_and_focus_rectangle_can_be_hidden_and_thickened()
    {
        var grid = Grid();
        var plain = Draw(grid).Cast<int>().ToArray();
        Set(grid, "GridLines", 1);
        var lined = Draw(grid).Cast<int>().ToArray();
        lined.Should().NotEqual(plain);
        foreach (var style in new[] { 2, 3 })
        {
            Set(grid, "GridLines", style);
            Draw(grid).Cast<int>().Should().NotEqual(lined);
        }
        Set(grid, "GridLines", 1);
        foreach (var invalid in new[] { 0, -1 })
        {
            var assign = () => Set(grid, "GridLineWidth", invalid);
            assign.Should().Throw<HexIDE.Runtime.Interpreter.VBRunTimeException>()
                .Where(error => error.Error.ErrNo == 380);
        }
        Set(grid, "GridLineWidth", 3);
        Draw(grid).Cast<int>().Should().NotEqual(lined);
        Set(grid, "GridLines", 0);
        Set(grid, "FocusRect", 2);
        Draw(grid).Cast<int>().Should().NotEqual(plain);
        Set(grid, "HighLight", 2);
        Draw(grid).Cast<int>().Should().Equal(plain, "without focus neither selection nor focus rectangle draws");
    }

    [AvaloniaFact]
    public void Cell_colours_follow_FillStyle_including_fixed_cells()
    {
        var grid = Grid();
        grid.Model.RowSel = 0;
        grid.Model.ColSel = 0;
        grid.Model.FillStyle = 1;
        Set(grid, "CellBackColor", 0x0000ff);
        Set(grid, "CellForeColor", 0x00ff00);
        Set(grid, "HighLight", 0);
        var pixels = Draw(grid);
        pixels[20, 8].Should().Be(0xff0000);
        pixels[84, 24].Should().Be(0xff0000);
        FlexGridMembers.Read(grid, "CellBackColor", []).Value.Should().Be(0x0000ffL);
        grid.Model.Row = 2;
        FlexGridMembers.Read(grid, "CellBackColor", []).Value.Should().Be(0L);
        Draw(grid)[84, 40].Should().Be(0xffffff);
    }

    [AvaloniaFact]
    public void Fixed_cells_body_and_unused_area_have_their_own_backgrounds()
    {
        var grid = Grid();
        var pixels = Draw(grid);
        pixels[20, 8].Should().Be(0x00ff00);
        pixels[20, 24].Should().Be(0x00ff00);
        pixels[84, 24].Should().Be(0xffffff);
        pixels[240, 120].Should().Be(0x808080);
    }

    [AvaloniaTheory]
    [InlineData("123", true)]
    [InlineData("-4.5", true)]
    [InlineData("1,5", true)]
    [InlineData("1e3", true)]
    [InlineData("12abc", true)]
    [InlineData("abc", false)]
    [InlineData("$3", false)]
    [InlineData(" 7", false)]
    [InlineData("#1/1/2000#", false)]
    public void General_alignment_tests_the_first_character(string text, bool right)
    {
        var grid = Grid();
        grid.Model.Text = text;
        var ink = Ink(Draw(grid));
        if (right) ink.Right.Should().BeGreaterThan(55);
        else ink.Left.Should().BeLessThan(12);
    }

    [AvaloniaTheory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void Alignment_runs_top_middle_bottom_with_left_centre_right_groups(int alignment)
    {
        var grid = Grid();
        grid.Model.SetRowHeight(1, 900);
        grid.Model.Text = "x";
        grid.Model.SetColAlignment(1, alignment);
        var ink = Ink(Draw(grid), height: 60);
        if (alignment / 3 == 0) ink.Left.Should().BeLessThan(10);
        if (alignment / 3 == 1) ink.Center.X.Should().BeInRange(27, 37);
        if (alignment / 3 == 2) ink.Right.Should().BeGreaterThan(55);
        if (alignment % 3 == 0) ink.Top.Should().BeLessThan(15);
        if (alignment % 3 == 1) ink.Center.Y.Should().BeInRange(23, 37);
        if (alignment % 3 == 2) ink.Bottom.Should().BeGreaterThan(48);
    }

    [AvaloniaFact]
    public void Current_cell_is_normal_while_the_rest_of_a_backwards_selection_is_highlighted()
    {
        var grid = Grid();
        grid.Model.Row = 2;
        grid.Model.Col = 2;
        grid.Model.RowSel = 1;
        grid.Model.ColSel = 1;
        var pixels = Draw(grid);
        pixels[148, 40].Should().Be(0xffffff);
        pixels[84, 24].Should().Be(0x0000ff);
        Set(grid, "HighLight", 2);
        Draw(grid)[84, 24].Should().Be(0xffffff, "focus is elsewhere");
    }

    [AvaloniaFact]
    public void Fixed_alignment_general_follows_the_column_and_cell_alignment_overrides_it()
    {
        var grid = Grid();
        Set(grid, "ForeColorFixed", 0);
        grid.Model.SetText(0, 1, "x");
        grid.Model.SetColAlignment(1, 6);
        Ink(Draw(grid), top: 0).Right.Should().BeGreaterThan(55);
        grid.Model.Row = 0;
        grid.Model.SetCellFormat("cellalignment", 0);
        Ink(Draw(grid), top: 0).Left.Should().BeLessThan(10);
    }

    [AvaloniaFact]
    public void Long_text_is_clipped_and_wordwrap_adds_lines_inside_the_cell()
    {
        var grid = Grid();
        grid.Model.SetRowHeight(1, 900);
        grid.Model.Text = "one two three four five six seven eight nine ten";
        var plain = Draw(grid);
        Ink(plain, height: 60);
        for (var x = 128; x < 190; x++)
            for (var y = 17; y < 75; y++) plain[x, y].Should().Be(0xffffff);
        FlexGridMembers.Write(grid, "WordWrap", [], new Vb6Value(true));
        var wrapped = Ink(Draw(grid), height: 60);
        wrapped.Height.Should().BeGreaterThan(Ink(plain, height: 60).Height);
        wrapped.Bottom.Should().BeLessThanOrEqualTo(60);
    }

    [AvaloniaFact]
    public void Cell_font_size_changes_drawing_without_growing_the_row()
    {
        var grid = Grid();
        grid.Model.Text = "x";
        var small = Draw(grid).Cast<int>().ToArray();
        FlexGridMembers.Write(grid, "CellFontSize", [], new Vb6Value(12.0));
        Draw(grid).Cast<int>().Should().NotEqual(small);
        grid.Model.GetRowHeight(1).Should().Be(240);
    }

    [AvaloniaFact]
    public void Scrollbars_only_show_when_cells_do_not_fit_and_enums_ignore_invalid_values()
    {
        var grid = Grid();
        Draw(grid);
        var bars = Avalonia.VisualTree.VisualExtensions.GetVisualChildren(grid)
            .OfType<Avalonia.Controls.Primitives.ScrollBar>().ToArray();
        bars.Should().HaveCount(2);
        bars.Should().OnlyContain(bar => !bar.IsVisible);
        grid.Model.Rows = 30;
        grid.Model.Cols = 10;
        grid.InvalidateArrange();
        Draw(grid);
        bars.Should().OnlyContain(bar => bar.IsVisible);
        Set(grid, "ScrollBars", -1);
        Set(grid, "ScrollBars", 5);
        FlexGridMembers.Read(grid, "ScrollBars", []).Value.Should().Be(3L);
        Set(grid, "ScrollBars", 0);
        Draw(grid);
        bars.Should().OnlyContain(bar => !bar.IsVisible);
        grid.Model.Rows = 0;
        grid.Model.Cols = 0;
        foreach (var member in new[] { "TopRow", "LeftCol" })
        {
            var read = () => FlexGridMembers.Read(grid, member, []);
            read.Should().Throw<NotImplementedException>();
        }
    }

    [AvaloniaFact]
    public void Moving_current_cell_scrolls_it_into_view_and_keeps_headings_fixed()
    {
        var grid = Grid();
        grid.Model.Rows = 30;
        grid.Model.Cols = 6;
        Draw(grid);
        Set(grid, "Row", 29);
        Set(grid, "Col", 5);
        ((int)FlexGridMembers.Read(grid, "TopRow", []).Value!).Should().BeGreaterThan(1);
        ((int)FlexGridMembers.Read(grid, "LeftCol", []).Value!).Should().BeGreaterThan(1);
        var pixels = Draw(grid);
        pixels[20, 8].Should().Be(0x00ff00);
        pixels[20, 24].Should().Be(0x00ff00);
        Set(grid, "Row", 1);
        Set(grid, "Col", 1);
        FlexGridMembers.Read(grid, "TopRow", []).Value.Should().Be(1);
        FlexGridMembers.Read(grid, "LeftCol", []).Value.Should().Be(1);
    }
}
