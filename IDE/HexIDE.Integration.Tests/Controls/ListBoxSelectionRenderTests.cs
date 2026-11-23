using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Integration.Tests.Controls;

public class ListBoxSelectionRenderTests
{
    // vb6.exe SP6 p-code measurements: #5473, oracle PR #5502.
    [AvaloniaTheory]
    [InlineData(0, 0, false, 1)]
    [InlineData(0, 0, true, 1)]
    [InlineData(0, 1, false, 1)]
    [InlineData(0, 1, true, 1)]
    [InlineData(0, 2, false, 1)]
    [InlineData(0, 2, true, 1)]
    [InlineData(1, 0, false, 1)]
    [InlineData(1, 0, true, 1)]
    [InlineData(0, 0, false, 2)]
    [InlineData(0, 0, true, 2)]
    [InlineData(0, 2, true, 2)]
    [InlineData(1, 0, true, 2)]
    public void RunningRows_DrawSelectionAndFocus(int style, int multi, bool focused, double scaling)
    {
        var (root, canvas, _, _) = ContainerRuntimeHarness.Spawn($"""
            VERSION 5.00
            Begin VB.Form Form1
               Begin VB.ListBox List1
                  Height = 1800
                  Width = 2400
                  Style = {style}
                  MultiSelect = {multi}
               End
               Begin VB.TextBox Text1
                  Top = 2100
                  Height = 300
                  Width = 2400
               End
            End
            Attribute VB_Name = "Form1"
            """.ReplaceLineEndings("\r\n"));
        var list = canvas.Children.OfType<VBListBox>().Single();
        // Solid glyphs pin text colour; leading spaces keep background samples clear of their ink.
        foreach (var text in new[] { "     0████", "     1████", "     2████", "     3████" }) list.Items.Add(text);
        var window = new Window { Width = 240, Height = 200, Content = root };
        window.SetRenderScaling(scaling);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            if (style == 1)
            {
                list.SetSelectedFromCode(1, true);
                list.SetListIndexFromCode(3);
            }
            else if (multi != 0)
            {
                list.SetSelectedFromCode(1, true);
                list.SetSelectedFromCode(3, true);
                list.SetListIndexFromCode(2);
            }
            else list.SetListIndexFromCode(1);
            if (focused) list.Focus();
            else canvas.Children.OfType<VBTextBox>().Single().Focus();
            Dispatcher.UIThread.RunJobs();
            list.IsKeyboardFocusWithin.Should().Be(focused);
            using var frame = window.CaptureRenderedFrame();
            frame.Should().NotBeNull();
            var highlight = ((ISolidColorBrush)VBColor.FromSystemColor(VbSystemColor.Highlight).ToBrush()).Color;
            var textColor = ((ISolidColorBrush)VBColor.FromSystemColor(VbSystemColor.Highlighttext).ToBrush()).Color;
            var background = ((ISolidColorBrush)list.Background!).Color;
            int Rgb(Color c) => c.R << 16 | c.G << 8 | c.B;
            int[] Row(int index, Bitmap? image = null)
            {
                var item = list.ContainerFromIndex(index)!;
                var point = item.TranslatePoint(default, window)!.Value;
                return Pixels(image ?? frame!, new PixelRect((int)(point.X * scaling), (int)(point.Y * scaling),
                    (int)(item.Bounds.Width * scaling), (int)(item.Bounds.Height * scaling)));
            }
            var selected = style == 1 ? 3 : 1;
            list.ContainerFromIndex(selected)!.TranslatePoint(default, list)!.Value.X.Should().Be(list.BorderThickness.Left);
            var pixels = Row(selected);
            var width = (int)(list.ContainerFromIndex(selected)!.Bounds.Width * scaling);
            pixels[width * 3 + width - 2].Should().Be(Rgb(highlight));
            if (!focused)
            {
                pixels[width * 3 + width - 1].Should().Be(Rgb(highlight));
                pixels[width * 3 + (style == 1 ? (int)(15 * scaling) : 0)].Should().Be(Rgb(highlight));
            }
            pixels[width * 3 + (style == 1 ? (int)(16 * scaling) : 0)].Should().Be(Rgb(highlight));
            pixels.Where((_, i) => i % width > 20 * scaling && i / width > 2 * scaling && i / width < pixels.Length / width - 2 * scaling)
                .Should().Contain(Rgb(textColor), "selected text uses Highlight Text");
            Row(0).Should().Contain(Rgb(((ISolidColorBrush)list.Foreground!).Color));
            Row(0)[width * 3 + width - 2].Should().Be(Rgb(background));
            if (multi != 0) Row(3)[width * 3 + width - 2].Should().Be(Rgb(highlight));
            if (style == 1)
            {
                Row(1)[width * 3 + width - 2].Should().Be(Rgb(background));
                pixels[0].Should().Be(Rgb(background));
                pixels[width * 3 + (int)(14 * scaling)].Should().Be(Rgb(background));
            }
            var focusRow = Row(list.ListIndex);
            var underFocus = multi != 0 ? Rgb(background) : Rgb(highlight);
            focusRow[width - 20].Should().BeOneOf(focused ? [underFocus, underFocus ^ 0xffffff] : [underFocus]);
            for (var x = width - 20; x < width - 10; x++)
                focusRow[x].Should().Be(focused && (x - (width - 20)) % 2 != 0 ? focusRow[width - 20] ^ 0xffffff : focusRow[width - 20]);
            var height = focusRow.Length / width;
            int EdgeColour(int x, int y) => focused && (x - (width - 20) + y) % 2 != 0
                ? focusRow[width - 20] ^ 0xffffff : focusRow[width - 20];
            for (var x = width - 20; x < width - 10; x++)
                focusRow[(height - 1) * width + x].Should().Be(EdgeColour(x, height - 1));
            for (var y = 0; y < height; y++)
                focusRow[y * width + width - 1].Should().Be(EdgeColour(width - 1, y));

            var selectedItem = list.ContainerFromIndex(selected)!;
            var hoverPoint = selectedItem.TranslatePoint(new Point(width / scaling - 5, 5), window)!.Value;
            window.MouseMove(hoverPoint);
            Dispatcher.UIThread.RunJobs();
            using var hovered = window.CaptureRenderedFrame();
            Row(selected, hovered!)[width * 3 + width - 2].Should().Be(Rgb(highlight));
            window.MouseMove(list.ContainerFromIndex(0)!.TranslatePoint(new Point(width / scaling - 5, 5), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            using var plainHover = window.CaptureRenderedFrame();
            Row(0, plainHover!)[width * 3 + width - 2].Should().Be(Rgb(background));
            if (multi == 0)
            {
                window.MouseDown(hoverPoint, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                using var pressed = window.CaptureRenderedFrame();
                Row(selected, pressed!)[width * 3 + width - 2].Should().Be(Rgb(highlight));
                window.MouseUp(hoverPoint, MouseButton.Left);
            }
            canvas.Children.OfType<VBTextBox>().Single().Focus();
            Dispatcher.UIThread.RunJobs();
            using var lostFocus = window.CaptureRenderedFrame();
            Row(list.ListIndex, lostFocus!)[width - 20].Should().Be(underFocus);
            list.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();
            list.IsEnabled = true;
            Dispatcher.UIThread.RunJobs();
            using var enabledAgain = window.CaptureRenderedFrame();
            Row(selected, enabledAgain!)[width * 3 + width - 2].Should().Be(Rgb(highlight));
        }
        finally { window.Close(); }
    }

    // Regression guard: the old outer margin moves into the row, preserving today's text inset.
    [AvaloniaFact]
    public void PlainList_KeepsTheDesignerTextInset()
    {
        var runtime = new VBListBox { Width = 160, Height = 100 };
        var designer = new VBListBox { Width = 160, Height = 100 };
        designer.UseDesignerLayout();
        runtime.Items.Add("Text");
        designer.Items.Add("Text");
        var panel = new StackPanel();
        panel.Children.Add(runtime);
        panel.Children.Add(designer);
        var window = new Window { Width = 240, Height = 240, Content = panel };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            double TextInset(VBListBox list) => list.GetVisualDescendants().OfType<TextBlock>().Single()
                .TranslatePoint(default, list)!.Value.X;
            TextInset(runtime).Should().Be(TextInset(designer));
            runtime.ContainerFromIndex(0)!.Bounds.Height.Should().Be(designer.ContainerFromIndex(0)!.Bounds.Height);
        }
        finally { window.Close(); }
    }

    // Style 1 combined with MultiSelect remains unmeasured and keeps its existing plain-list path.
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public void CheckboxMultiSelect_KeepsTheExistingContainer(int multi)
    {
        var list = new VBListBox { SavedStyle = 1, MultiSelect = (VBMultiSelect)multi };
        list.Items.Add("Text");
        var window = new Window { Width = 240, Height = 120, Content = list };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            list.ContainerFromIndex(0).Should().BeOfType<ListBoxItem>();
        }
        finally { window.Close(); }
    }

    private static int[] Pixels(Bitmap bitmap, PixelRect region)
    {
        var pixels = new int[region.Width * region.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(region, handle.AddrOfPinnedObject(), pixels.Length * 4, region.Width * 4); }
        finally { handle.Free(); }
        return pixels.Select(p => bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888
            ? (p & 0xff) << 16 | (p & 0xff00) | (p >> 16 & 0xff) : p & 0xffffff).ToArray();
    }
}
