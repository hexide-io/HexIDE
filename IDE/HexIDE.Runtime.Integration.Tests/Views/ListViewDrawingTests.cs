using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using Avalonia.Media;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.AvaloniaInterop;

namespace HexIDE.Runtime.Integration.Tests.Views;

public class ListViewDrawingTests
{
    private static string Draw(VBListView list, PixelRect? region = null)
    {
        list.Measure(new Size(320, 180));
        list.Arrange(new Rect(0, 0, 320, 180));
        using var bitmap = new RenderTargetBitmap(new PixelSize(320, 180));
        bitmap.Render(list);
        if (region is { } rect)
        {
            var stride = rect.Width * 4;
            var pixels = new byte[stride * rect.Height];
            var buffer = Marshal.AllocHGlobal(pixels.Length);
            try
            {
                bitmap.CopyPixels(rect, buffer, pixels.Length, stride);
                Marshal.Copy(buffer, pixels, 0, pixels.Length);
                return Convert.ToHexString(SHA256.HashData(pixels));
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static int HeaderInkWidth(VBListView list)
    {
        Draw(list);
        using var bitmap = new RenderTargetBitmap(new PixelSize(320, 180));
        bitmap.Render(list);
        var region = new PixelRect(6, 4, 120, 10);
        var pixels = new byte[region.Width * region.Height * 4];
        var buffer = Marshal.AllocHGlobal(pixels.Length);
        try
        {
            bitmap.CopyPixels(region, buffer, pixels.Length, region.Width * 4);
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally { Marshal.FreeHGlobal(buffer); }
        var face = ((ISolidColorBrush)VBColor.FromSystemColor(VbSystemColor.Btnface).ToBrush()).Color;
        var rgba = bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888;
        var left = region.Width;
        var right = -1;
        for (var y = 0; y < region.Height; y++)
            for (var x = 0; x < region.Width; x++)
            {
                var offset = (y * region.Width + x) * 4;
                if (pixels[offset] == (rgba ? face.R : face.B) && pixels[offset + 1] == face.G
                    && pixels[offset + 2] == (rgba ? face.B : face.R))
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
            }
        return Math.Max(0, right - left + 1);
    }

    // VB6 SP6 native/p-code: docs/vb6-fidelity-oracle/in-box-controls-the-listview.md,
    // "The font: items, sub-items, headers and the label editor" (#2895, oracle PR #5240).
    private static VBListView SavedFont()
    {
        var (_, canvas, _, _) = ContainerRuntimeHarness.Spawn("""
            VERSION 5.00
            Begin VB.Form Form1
               Begin MSComctlLib.ListView ListView1
                  Height = 2700
                  Width = 4800
                  View = 3
                  BeginProperty Font {0BE35203-8F91-11CE-9DE3-00AA004BB851}
                     Name = "Courier New"
                     Size = 14.25
                     Charset = 0
                     Weight = 700
                     Italic = -1
                     Underline = -1
                     Strikethrough = -1
                  EndProperty
               End
            End
            Attribute VB_Name = "Form1"
            """);
        var list = canvas.Children.OfType<VBListView>().Single();
        canvas.Children.Remove(list);
        return list;
    }

    [AvaloniaTheory]
    [InlineData("Bold")]
    [InlineData("Italic")]
    [InlineData("Underline")]
    [InlineData("Strikethrough")]
    public void Saved_font_attributes_read_back(string member)
    {
        var list = SavedFont();
        list.TryGetProperty("Font", out var value);
        ((ICSharpPropertyBag)value.Value!).TryGetProperty(member, out var attribute);
        attribute.Value.Should().Be(true);
    }

    [AvaloniaFact]
    public async Task Saved_font_drives_drawing_measurement_and_editor()
    {
        var list = SavedFont();
        await list.Columns.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Wide"), new(2000)]);
        await list.Columns.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Detail")]);
        var item = await Add(list, "Alpha");
        item.SetSubItem([new(1)], new("Subitem"));
        var font = AttachedProperties.GetFont(list);
        font.Should().Be(new VBFont("Courier New", 14.25, bold: true, italic: true, underline: true, strikethrough: true));
        var window = new Window { Content = list, Width = 320, Height = 180 };
        try
        {
            window.Show();
            await list.CallAsync("StartLabelEdit", []);
            var editor = (TextBox)list.Child!;
            editor.FontFamily.Should().Be(font!.Value.ToFontFamily());
            editor.FontSize.Should().Be(19);
            editor.FontWeight.Should().Be(FontWeight.Bold);
            editor.FontStyle.Should().Be(FontStyle.Italic);
            Avalonia.Controls.Documents.Inline.GetTextDecorations(editor)!.Select(d => d.Location)
                .Should().BeEquivalentTo(new[] { TextDecorationLocation.Underline, TextDecorationLocation.Strikethrough });
            // Header-only pixels must use the same source as labels and the editor.
            var measured = new FormattedText("Alpha", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(font.Value.ToFontFamily(), FontStyle.Italic, FontWeight.Bold),
                19, Brushes.Black);
            list.GetItemLabelBounds(0).Width.Should().BeApproximately(measured.Width + 4, .01);
            var header = HeaderInkWidth(list);
            header.Should().BeGreaterThan(0);
            AttachedProperties.SetFont(list, new VBFont("Arial", 8.25));
            HeaderInkWidth(list).Should().NotBe(header);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public async Task Runtime_font_change_repaints_resizes_and_keeps_second_row_hit_testing(double scaling)
    {
        var list = new VBListView();
        list.TrySetProperty("View", new(3L));
        await list.Columns.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Name"), new(2000)]);
        await Add(list, "Alpha");
        var second = await Add(list, "Beta");
        var window = new Window { Content = list, Width = 320, Height = 180 };
        window.SetRenderScaling(scaling);
        try
        {
            window.Show();
            await list.CallAsync("StartLabelEdit", []);
            var editor = (TextBox)list.Child!;
            string Frame()
            {
                var context = System.Threading.ExecutionContext.Capture();
                try { Dispatcher.UIThread.RunJobs(); }
                finally { System.Threading.ExecutionContext.Restore(context!); }
                using var bitmap = window.CaptureRenderedFrame();
                bitmap.Should().NotBeNull();
                using var stream = new MemoryStream();
                bitmap!.Save(stream, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
            }
            var height = list.GetItemLabelBounds(1).Height;
            var before = Frame();
            list.TryGetProperty("Font", out var value);
            var font = (ICSharpPropertyBag)value.Value!;
            font.TrySetProperty("Name", new("Times New Roman"));
            font.TrySetProperty("Size", new(18));
            font.TrySetProperty("Bold", new(true));
            editor.FontFamily.Should().Be(new VBFont("Times New Roman", 18).ToFontFamily());
            editor.FontSize.Should().Be(24);
            editor.FontWeight.Should().Be(FontWeight.Bold);
            list.GetItemLabelBounds(1).Height.Should().BeGreaterThan(height);
            Frame().Should().NotBe(before);
            editor.RaiseEvent(new Avalonia.Input.KeyEventArgs
                { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape });
            window.UpdateLayout();
            var point = list.GetItemLabelBounds(1).Center;
            window.MouseDown(point, Avalonia.Input.MouseButton.Left);
            window.MouseUp(point, Avalonia.Input.MouseButton.Left);
            list.SelectedItem.Should().BeSameAs(second);
            await list.CallAsync("StartLabelEdit", []);
            ((TextBox)list.Child!).FontSize.Should().Be(24);
            ((TextBox)list.Child!).FontWeight.Should().Be(FontWeight.Bold);
        }
        finally { window.Close(); }
    }

    // Scope guard, not a VB6 measurement: #2895 measured report view only.
    [AvaloniaTheory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(2L)]
    public async Task Unmeasured_views_keep_their_existing_drawing_font(long view)
    {
        var list = new VBListView();
        list.TrySetProperty("View", new(view));
        AttachedProperties.SetFont(list, new VBFont("Courier New", 14.25, bold: true, italic: true,
            underline: true, strikethrough: true));
        await Add(list, "Alpha");
        var window = new Window { Content = list, Width = 320, Height = 180 };
        try
        {
            window.Show();
            await list.CallAsync("StartLabelEdit", []);
            var editor = (TextBox)list.Child!;
            editor.FontFamily.Should().Be(new FontFamily("Arial"));
            editor.FontSize.Should().Be(11);
            editor.FontWeight.Should().Be(FontWeight.Normal);
            editor.FontStyle.Should().Be(FontStyle.Normal);
            Avalonia.Controls.Documents.Inline.GetTextDecorations(editor).Should().BeNull();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Default_font_keeps_existing_report_geometry()
    {
        var list = new VBListView();
        list.TrySetProperty("View", new(3L));
        await list.Columns.CallAsync("Add", []);
        await Add(list, "Alpha");
        await Add(list, "Beta");
        Draw(list);
        var first = list.GetItemLabelBounds(0);
        var second = list.GetItemLabelBounds(1);
        first.Y.Should().Be(3 + 18 + 1);
        (second.Y - first.Y).Should().Be(18);
    }

    // Edge order and widths: docs/vb6-fidelity-oracle/in-box-controls-the-listview.md
    // (client-area origin), with standard client-edge colours as recorded in #2884.
    [AvaloniaTheory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    public void Appearance_and_BorderStyle_paint_the_measured_edge(int appearance, int border)
    {
        var (_, canvas, _, _) = ContainerRuntimeHarness.Spawn($"""
            VERSION 5.00
            Begin VB.Form Form1
               Begin MSComctlLib.ListView ListView1
                  Height = 2700
                  Width = 4800
                  Appearance = {appearance}
                  BorderStyle = {border}
                  BackColor = 255
               End
            End
            Attribute VB_Name = "Form1"
            """);
        var list = canvas.Children.OfType<VBListView>().Single();
        list.Measure(new Size(320, 180));
        list.Arrange(new Rect(0, 0, 320, 180));
        using var bitmap = new RenderTargetBitmap(new PixelSize(320, 180));
        bitmap.Render(list);
        var pixels = new byte[320 * 180 * 4];
        var buffer = Marshal.AllocHGlobal(pixels.Length);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, 320, 180), buffer, pixels.Length, 320 * 4);
            Marshal.Copy(buffer, pixels, 0, pixels.Length);
        }
        finally { Marshal.FreeHGlobal(buffer); }

        void Pixel(int x, int y, Avalonia.Media.Color expected)
        {
            var offset = (y * 320 + x) * 4;
            // macOS renders Rgba8888 where Windows and Linux render Bgra8888.
            var rgba = bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888;
            pixels[offset].Should().Be(rgba ? expected.R : expected.B);
            pixels[offset + 1].Should().Be(expected.G);
            pixels[offset + 2].Should().Be(rgba ? expected.B : expected.R);
            pixels[offset + 3].Should().Be(255);
        }

        var edge = appearance * 2 + border;
        for (var inset = 0; inset <= edge; inset++)
        {
            var top = Avalonia.Media.Colors.Red;
            var bottom = top;
            if (appearance == 1 && inset < 2)
            {
                top = ((ISolidColorBrush)VBColor.FromSystemColor(inset == 0 ? VbSystemColor.Btnshadow : VbSystemColor._3DDKSHADOW).ToBrush()).Color;
                bottom = ((ISolidColorBrush)VBColor.FromSystemColor(inset == 0 ? VbSystemColor.Btnhighlight : VbSystemColor._3DLIGHT).ToBrush()).Color;
            }
            else if (inset < edge)
                top = bottom = ((ISolidColorBrush)VBColor.FromSystemColor(VbSystemColor.Windowframe).ToBrush()).Color;
            Pixel(160, inset, top);
            Pixel(inset, 90, top);
            Pixel(160, 179 - inset, bottom);
            Pixel(319 - inset, 90, bottom);
        }
    }

    private static async Task<ListViewItem> Add(VBListView list, string text) =>
        (ListViewItem)(await list.Items.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new(text)]))!.Value.Value!;

    [AvaloniaTheory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(3L)]
    public async Task Every_view_paints_item_text(long view)
    {
        var list = new VBListView();
        list.TrySetProperty("View", new(view));
        await list.Columns.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Name")]);
        var empty = Draw(list);
        var item = await Add(list, "Alpha");
        Draw(list).Should().NotBe(empty);
        var first = Draw(list);
        item.TrySetProperty("Text", new("Changed label"));
        Draw(list).Should().NotBe(first);
        await list.Items.CallAsync("Clear", []);
        Draw(list).Should().Be(empty);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Saved_BackColor_is_painted_even_when_disabled(bool enabled)
    {
        VBListView Load(int color, int border)
        {
            var (_, canvas, _, _) = ContainerRuntimeHarness.Spawn($"""
                VERSION 5.00
                Begin VB.Form Form1
                   ClientHeight = 4500
                   ClientWidth = 6000
                   Begin MSComctlLib.ListView ListView1
                      Height = 2500
                      Width = 5000
                      BackColor = {color}
                      BorderStyle = {border}
                      Appearance = 0
                      Enabled = {(enabled ? -1 : 0)}
                   End
                End
                Attribute VB_Name = "Form1"
                """);
            return canvas.Children.OfType<VBListView>().Single();
        }
        var red = Draw(Load(255, 1));
        red.Should().NotBe(Draw(Load(12648447, 1)));
        red.Should().NotBe(Draw(Load(255, 0)));
    }

    [AvaloniaFact]
    public async Task Disabled_Report_greys_only_occupied_rows()
    {
        var list = new VBListView();
        list.TrySetProperty("View", new(3L));
        list.TrySetProperty("HideColumnHeaders", new(true));
        await list.Columns.CallAsync("Add", []);
        var item = await Add(list, "Alpha");
        item.TrySetProperty("Selected", new(false));
        var below = new PixelRect(3, 80, 96, 40);
        var row = new PixelRect(3, 3, 96, 18);
        var background = Draw(list, below);
        var enabledRow = Draw(list, row);
        list.IsEnabled = false;
        Draw(list, below).Should().Be(background);
        Draw(list, row).Should().NotBe(enabledRow);
    }

    // Disabled drawing is measured in the ListView oracle's Drawing section (#1085).
    [AvaloniaFact]
    public void Disabling_an_empty_list_keeps_its_background_and_border()
    {
        var list = new VBListView();
        var enabled = Draw(list);
        list.IsEnabled = false;
        Draw(list).Should().Be(enabled);
    }

    [AvaloniaFact]
    public async Task Report_paints_headers_and_subitems_and_hides_headers()
    {
        var list = new VBListView();
        list.TrySetProperty("View", new(3L));
        var empty = Draw(list);
        await list.Columns.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Name")]);
        await list.Columns.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Detail")]);
        Draw(list).Should().NotBe(empty);
        var item = await Add(list, "Alpha");
        var before = Draw(list);
        item.SetSubItem([new(1)], new("Cell text"));
        Draw(list).Should().NotBe(before);
        before = Draw(list);
        list.TrySetProperty("HideColumnHeaders", new(true));
        Draw(list).Should().NotBe(before);
    }

    [AvaloniaFact]
    public async Task Selection_respects_HideSelection_and_FullRowSelect()
    {
        var list = new VBListView();
        list.TrySetProperty("View", new(3L));
        await list.Columns.CallAsync("Add", []);
        var item = await Add(list, "Alpha");
        var hidden = Draw(list);
        list.TrySetProperty("HideSelection", new(false));
        var selected = Draw(list);
        selected.Should().NotBe(hidden);
        list.TrySetProperty("FullRowSelect", new(true));
        Draw(list).Should().NotBe(selected);
        item.TrySetProperty("Selected", new(false));
        Draw(list).Should().Be(hidden);
    }

    [AvaloniaFact]
    public async Task Checkbox_has_no_box_until_explicitly_set_and_paints_checked_state()
    {
        var list = new VBListView();
        await Add(list, "Alpha");
        list.TrySetProperty("Checkboxes", new(true));
        var noBox = Draw(list);
        var item = list.Items.Entries[0];
        item.TrySetProperty("Checked", new(false));
        var uncheckedBox = Draw(list);
        uncheckedBox.Should().NotBe(noBox);
        item.TrySetProperty("Checked", new(true));
        Draw(list).Should().NotBe(uncheckedBox);
        list.TrySetProperty("Checkboxes", new(false));
        Draw(list).Should().NotBe(uncheckedBox);
    }

    [AvaloniaFact]
    public async Task Views_reflow_multiple_items_and_icon_labels_wrap()
    {
        var list = new VBListView();
        for (var i = 0; i < 8; i++)
            await Add(list, "A long caption that wraps onto another line " + i);
        var icons = Draw(list);
        list.TrySetProperty("LabelWrap", new(false));
        Draw(list).Should().NotBe(icons);
        foreach (var item in list.Items.Entries)
            item.TrySetProperty("Text", new("Short caption"));
        list.TrySetProperty("View", new(1L));
        var small = Draw(list);
        small.Should().NotBe(icons);
        list.TrySetProperty("View", new(2L));
        Draw(list).Should().NotBe(small);
    }

    [AvaloniaFact]
    public async Task Report_paints_alignment_display_order_grid_and_cell_styles()
    {
        var list = new VBListView();
        list.TrySetProperty("View", new(3L));
        await list.Columns.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Name")]);
        await list.Columns.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Detail")]);
        var item = await Add(list, "Alpha");
        item.SetSubItem([new(1)], new("Cell"));
        var before = Draw(list);
        list.Columns.Entries[1].TrySetProperty("Alignment", new(1L));
        Draw(list).Should().NotBe(before);
        before = Draw(list);
        list.Columns.Entries[1].TrySetProperty("Position", new(1));
        Draw(list).Should().NotBe(before);
        before = Draw(list);
        item.SubItems.Entries[0].TrySetProperty("Bold", new(true));
        Draw(list).Should().NotBe(before);
        before = Draw(list);
        item.TrySetProperty("ForeColor", new(255L));
        Draw(list).Should().NotBe(before);
        before = Draw(list);
        item.TrySetProperty("Bold", new(true));
        Draw(list).Should().NotBe(before);
        before = Draw(list);
        list.TrySetProperty("GridLines", new(true));
        Draw(list).Should().NotBe(before);
        before = Draw(list);
        list.TrySetProperty("Enabled", new(false));
        Draw(list).Should().NotBe(before);
    }

    [AvaloniaFact]
    public async Task Model_edits_repaint_a_shown_control()
    {
        var list = new VBListView();
        list.TrySetProperty("View", new(3L));
        await list.Columns.CallAsync("Add", [Vb6Value.Missing, Vb6Value.Missing, new("Name")]);
        var item = await Add(list, "Alpha");
        var window = new Window { Content = list, Width = 320, Height = 180 };
        string Frame()
        {
            Dispatcher.UIThread.RunJobs();
            using var bitmap = window.CaptureRenderedFrame();
            bitmap.Should().NotBeNull();
            using var stream = new MemoryStream();
            bitmap!.Save(stream, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
        }
        try
        {
            window.Show();
            var before = Frame();
            item.TrySetProperty("Text", new("Updated"));
            Frame().Should().NotBe(before);
            before = Frame();
            list.Columns.Entries[0].TrySetProperty("Text", new("Updated header"));
            Frame().Should().NotBe(before);
            before = Frame();
            list.TrySetProperty("Checkboxes", new(true));
            item.TrySetProperty("Checked", new(false));
            Frame().Should().NotBe(before);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Focus_paints_selection_and_focus_rectangle()
    {
        var list = new VBListView();
        await Add(list, "Alpha");
        var other = new Button { Content = "Other" };
        var window = new Window { Content = new StackPanel { Children = { list, other } }, Width = 320, Height = 240 };
        try
        {
            window.Show();
            other.Focus();
            list.IsFocused.Should().BeFalse();
            var before = Draw(list);
            list.Focus();
            list.IsFocused.Should().BeTrue();
            Draw(list).Should().NotBe(before);
        }
        finally { window.Close(); }
    }
}
