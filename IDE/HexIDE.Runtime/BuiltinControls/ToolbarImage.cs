using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;

namespace HexIDE.Runtime.BuiltinControls;

internal sealed class ToolbarImage(VBToolbar bar, ToolbarItem item, Button button) : Control
{
    internal static Rect BoundsFor(double width, int size, bool below) =>
        new(below ? Math.Floor((width - size) / 2) : 5, 3, size, size);
    internal Rect ImageBounds
    {
        get
        {
            var bounds = BoundsFor(button.Width, bar.ImageList!.ImageWidth, bar.TextBelow);
            var origin = this.TranslatePoint(default, button) ?? default;
            return new Rect(bounds.X - origin.X, bounds.Y - origin.Y, bounds.Width, bounds.Height);
        }
    }

    internal VBImageList? SelectedList(bool hot)
    {
        item.TryGetProperty("Enabled", out var enabled);
        var list = !(bool)enabled.Value! ? bar.DisabledImageList
            : bar.Flat && hot ? bar.HotImageList : bar.ImageList;
        return bar.ImageList is { } normal && list is not null && list.ImageWidth == normal.ImageWidth &&
            list.ImageHeight == normal.ImageHeight ? list : null;
    }

    internal ImageListPictureObject? SelectedPicture(bool? hot = null)
    {
        var list = SelectedList(hot ?? button.IsPointerOver);
        if (list is null || item.Image.Value is int zero && zero == 0) return null;
        var normal = (ImageListImage)bar.ImageList!.Images.Item(item.Image).Value!;
        normal.TryGetProperty("Index", out var index);
        if (Convert.ToInt32(index.Value) > list.Images.Count) return null;
        var image = (ImageListImage)list.Images.Item(index).Value!;
        image.TryGetProperty("Picture", out var picture);
        return (ImageListPictureObject)picture.Value!;
    }

    internal static WriteableBitmap Decode(ImageListPictureObject picture, VBImageList list)
    {
        using var stream = new MemoryStream(picture.Payload);
        var bitmap = WriteableBitmap.Decode(stream);
        if (!list.useMaskColor) return bitmap;
        using var buffer = bitmap.Lock();
        if (buffer.Format != PixelFormat.Bgra8888 && buffer.Format != PixelFormat.Rgba8888)
        {
            bitmap.Dispose();
            throw ToolbarItems.Unmeasured();
        }
        var pixels = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        var red = buffer.Format == PixelFormat.Bgra8888 ? 2 : 0;
        for (var y = 0; y < buffer.Size.Height; y++)
        for (var x = 0; x < buffer.Size.Width; x++)
        {
            var offset = y * buffer.RowBytes + x * 4;
            if (pixels[offset + red] == (list.maskColor & 255) &&
                pixels[offset + 1] == ((list.maskColor >> 8) & 255) &&
                pixels[offset + 2 - red] == ((list.maskColor >> 16) & 255))
                Array.Clear(pixels, offset, 4);
        }
        Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        return bitmap;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (SelectedPicture() is not { } picture) return;
        using var bitmap = Decode(picture, SelectedList(button.IsPointerOver)!);
        context.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), ImageBounds);
    }
}
