using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

public class ToolbarImagePaintingTests
{
    [AvaloniaTheory]
    [InlineData(0, 16, 10)]
    [InlineData(1, 16, 5)]
    [InlineData(0, 32, 3)]
    public async Task Bound_buttons_keep_image_space_and_measured_offsets(int alignment, int size, int left)
    {
        var images = ToolbarImageListTests.Images();
        images.ImageWidth = images.ImageHeight = size;
        var bar = new VBToolbar();
        bar.TrySetProperty("TextAlignment", new Vb6Value(alignment));
        bar.TrySetProperty("ImageList", new Vb6Value((Control)images));
        await bar.Buttons.CallAsync("Add", [Vb6Value.Missing, new Vb6Value("one"), new Vb6Value("One"), new Vb6Value(0), new Vb6Value(1)]);
        await bar.Buttons.CallAsync("Add", [Vb6Value.Missing, new Vb6Value("none"), new Vb6Value("Four")]);
        var window = new Window { Content = bar };
        window.Show();
        try
        {
            window.UpdateLayout();
            var faces = bar.GetVisualDescendants().OfType<ToolbarImage>().ToArray();
            faces.Should().HaveCount(2);
            ToolbarImage.BoundsFor(alignment == 1 ? 56 : size == 32 ? 39 : 36, size, alignment == 0).X.Should().Be(left);
            var button = faces[0].FindAncestorOfType<Button>()!;
            var position = faces[0].TranslatePoint(faces[0].ImageBounds.Position, button)!.Value;
            position.X.Should().Be(alignment == 0 ? Math.Floor((button.Width - size) / 2) : 5);
            position.Y.Should().Be(3);
            faces[0].ImageBounds.Width.Should().Be(size);
            faces[0].SelectedPicture().Should().NotBeNull();
            faces[1].SelectedPicture().Should().BeNull();
            faces[1].Bounds.Size.Should().Be(faces[0].Bounds.Size);
        }
        finally { window.Close(); }
    }

    private static ImageListPictureObject Picture(byte blue = 0xc0, byte green = 0xc0, byte red = 0xc0)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0x4d42);
        writer.Write(54 + 16 * 16 * 3);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(16);
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write((ushort)24);
        for (var i = 0; i < 6; i++) writer.Write(0);
        for (var i = 0; i < 16 * 16; i++)
        {
            writer.Write(blue);
            writer.Write(green);
            writer.Write(red);
        }
        var payload = stream.ToArray();
        using var record = new MemoryStream();
        using var framing = new BinaryWriter(record);
        framing.Write(payload.Length + 24);
        framing.Write(new Guid("0BE35204-8F91-11CE-9DE3-00AA004BB851").ToByteArray());
        framing.Write(0x746c);
        framing.Write(payload.Length);
        framing.Write(payload);
        return new ImageListPictureObject(record.ToArray());
    }

    // #2334, #2490 issuecomment-6462915346: the disabled list's navy stays navy.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabled_image_is_opaque_and_caption_keeps_its_previous_pixels(bool compareCaption)
    {
        var images = ToolbarImageListTests.Images();
        var disabled = ToolbarImageListTests.Images();
        await disabled.Images.CallAsync("Clear", []);
        await disabled.Images.CallAsync("Add", [Vb6Value.Missing, new Vb6Value("navy"), new Vb6Value(Picture(128, 0, 0))]);
        var bar = new VBToolbar();
        bar.TrySetProperty("ImageList", new Vb6Value((Control)images));
        bar.TrySetProperty("DisabledImageList", new Vb6Value((Control)disabled));
        await bar.Buttons.CallAsync("Add", [Vb6Value.Missing, new Vb6Value("one"), new Vb6Value("One"), new Vb6Value(0), new Vb6Value(1)]);
        var item = (ToolbarItem)bar.Buttons.Item([new Vb6Value(1)]).Value!;
        item.TrySetProperty("Enabled", new Vb6Value(false));
        var window = new Window { Content = bar, Background = Avalonia.Media.Brushes.Red };
        window.Show();
        try
        {
            window.UpdateLayout();
            var image = bar.GetVisualDescendants().OfType<ToolbarImage>().Single();
            var button = bar.GetVisualDescendants().OfType<Button>().Single();
            byte[] Pixels(PixelRect rect)
            {
                using var frame = new RenderTargetBitmap(new PixelSize((int)bar.Bounds.Width, (int)bar.Bounds.Height));
                frame.Render(bar);
                var pixels = new byte[rect.Width * rect.Height * 4];
                var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try { frame.CopyPixels(rect, pin.AddrOfPinnedObject(), pixels.Length, rect.Width * 4); }
                finally { pin.Free(); }
                if (frame.Format == PixelFormat.Rgba8888)
                    for (var i = 0; i < pixels.Length; i += 4)
                        (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
                return pixels;
            }
            var origin = button.TranslatePoint(default, bar)!.Value;
            if (!compareCaption)
            {
                var bounds = ToolbarImage.BoundsFor(button.Width, 16, true);
                Pixels(new PixelRect((int)(origin.X + bounds.X + 4), (int)(origin.Y + bounds.Y + 4), 1, 1))
                    .Should().Equal(128, 0, 0, 255);
            }
            else
            {
                var captionRect = new PixelRect((int)origin.X, (int)origin.Y + 23, (int)button.Width, 15);
                var current = Pixels(captionRect);
                // Recreate the old disabled Button independently, so changing the caption's
                // parent or the production Button's opacity cannot change the reference too.
                var caption = new TextBlock { Text = "One" };
                var face = new Canvas { Width = button.Width, Height = button.Height };
                face.SetValue(Avalonia.Controls.Documents.TextElement.FontFamilyProperty, bar.FontFamily);
                face.SetValue(Avalonia.Controls.Documents.TextElement.FontSizeProperty, bar.FontSize);
                face.Children.Add(caption);
                caption.Measure(Size.Infinity);
                Canvas.SetLeft(caption, (face.Width - caption.DesiredSize.Width) / 2);
                Canvas.SetTop(caption, 23);
                var reference = new Button
                {
                    Content = face, Width = button.Width, Height = button.Height,
                    Padding = new Thickness(0), MinWidth = 0, MinHeight = 0, IsEnabled = false
                };
                var strip = (Canvas)button.Parent!;
                ((Panel)image.Parent!).Children.Remove(image);
                strip.Children.Remove(button);
                Canvas.SetLeft(reference, origin.X);
                Canvas.SetTop(reference, origin.Y);
                strip.Children.Add(reference);
                window.UpdateLayout();
                current.Should().Equal(Pixels(captionRect));
                current.Where((_, i) => i % 4 != 3).Distinct().Count().Should().BeGreaterThan(2,
                    "the comparison must include the rendered caption");
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Paint_reads_masking_again_after_the_list_changes()
    {
        var images = ToolbarImageListTests.Images();
        await images.Images.CallAsync("Clear", []);
        await images.Images.CallAsync("Add", [Vb6Value.Missing, new Vb6Value("mask"), new Vb6Value(Picture())]);
        images.TrySetProperty("MaskColor", new Vb6Value(0xc0c0c0));
        images.TrySetProperty("UseMaskColor", new Vb6Value(true));
        var bar = new VBToolbar();
        bar.TrySetProperty("ImageList", new Vb6Value((Control)images));
        await bar.Buttons.CallAsync("Add", [Vb6Value.Missing, new Vb6Value("one"), new Vb6Value("One"), new Vb6Value(0), new Vb6Value("mask")]);
        var window = new Window { Content = bar };
        window.Show();
        try
        {
            window.UpdateLayout();
            var face = bar.GetVisualDescendants().OfType<ToolbarImage>().Single();
            byte Alpha()
            {
                using var frame = new RenderTargetBitmap(new PixelSize(36, 38));
                frame.Render(face);
                var pixel = new byte[4];
                var pin = GCHandle.Alloc(pixel, GCHandleType.Pinned);
                try { frame.CopyPixels(new PixelRect((int)face.ImageBounds.X + 1, 4, 1, 1), pin.AddrOfPinnedObject(), 4, 4); }
                finally { pin.Free(); }
                return pixel[3];
            }
            Alpha().Should().Be(0);
            images.TrySetProperty("UseMaskColor", new Vb6Value(false));
            Alpha().Should().Be(255);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Alternate_lists_select_by_position_and_missing_images_draw_nothing()
    {
        var images = ToolbarImageListTests.Images();
        var alternate = ToolbarImageListTests.Images();
        var bar = new VBToolbar();
        bar.TrySetProperty("ImageList", new Vb6Value((Control)images));
        await bar.Buttons.CallAsync("Add", [Vb6Value.Missing, new Vb6Value("one"), new Vb6Value("One"), new Vb6Value(0), new Vb6Value("ico")]);
        var window = new Window { Content = bar };
        window.Show();
        try
        {
            window.UpdateLayout();
            bar.TrySetProperty("HotImageList", new Vb6Value((Control)alternate));
            bar.TrySetProperty("DisabledImageList", new Vb6Value((Control)alternate));
            window.UpdateLayout();
            var face = bar.GetVisualDescendants().OfType<ToolbarImage>().Single();
            face.SelectedList(true).Should().BeSameAs(images);
            bar.TrySetProperty("Style", new Vb6Value(1));
            face.SelectedList(true).Should().BeSameAs(alternate);
            var item = (ToolbarItem)bar.Buttons.Item([new Vb6Value(1)]).Value!;
            item.TrySetProperty("Enabled", new Vb6Value(false));
            window.UpdateLayout();
            face = bar.GetVisualDescendants().OfType<ToolbarImage>().Single();
            face.SelectedList(false).Should().BeSameAs(alternate);
            // The alternate list can lack the normal selector; no normal-image fallback.
            var shortList = ToolbarImageListTests.Images();
            await shortList.Images.CallAsync("Remove", [new Vb6Value(3)]);
            bar.TrySetProperty("DisabledImageList", new Vb6Value((Control)shortList));
            window.UpdateLayout();
            face = bar.GetVisualDescendants().OfType<ToolbarImage>().Single();
            face.SelectedPicture().Should().BeNull();
            bar.TrySetProperty("HotImageList", new Vb6Value((Control)shortList));
            item.TrySetProperty("Enabled", new Vb6Value(true));
            window.UpdateLayout();
            face = bar.GetVisualDescendants().OfType<ToolbarImage>().Single();
            face.SelectedPicture(true).Should().BeNull();
            face.SelectedPicture(false).Should().NotBeNull();
        }
        finally { window.Close(); }
    }
}
