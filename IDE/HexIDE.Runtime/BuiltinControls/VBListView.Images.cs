using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

public sealed partial class VBListView
{
    internal readonly Dictionary<string, string> SavedImageLists = new();
    internal static bool IsImageListMember(string name) => name.Equals("Icons", StringComparison.OrdinalIgnoreCase)
        || name.Equals("SmallIcons", StringComparison.OrdinalIgnoreCase) || name.Equals("ColumnHeaderIcons", StringComparison.OrdinalIgnoreCase);
    private VBImageList? Images(string member) => properties[member].Value as VBImageList;
    internal bool UsesImages(VBImageList images) => new[] { "Icons", "SmallIcons", "ColumnHeaderIcons" }.Any(member => ReferenceEquals(Images(member), images));

    private void BindImages(string member, Vb6Value value)
    {
        if (value.Type != Vb6Value.ValueType.Object && value.Type != Vb6Value.ValueType.Control
            && value.Type != Vb6Value.ValueType.CSharpProxyObject)
            throw new NotImplementedException("ListView ImageList binding conversion is unmeasured (#1085)");
        if (value.Value is not null && value.Value is not VBImageList)
            throw ListViewItems.Error(35610);
        var images = value.Value as VBImageList;
        if (images is not null && (images.Images.Count == 0 || images.ImageWidth != 16 || images.ImageHeight != 16))
            throw new NotImplementedException("ListView empty or non-16-pixel ImageList binding is unmeasured (#1085)");
        if (Images(member) is { } previous && images is not null && !ReferenceEquals(previous, images)
            || images is null && (Images(member) is null || !member.Equals("Icons", StringComparison.OrdinalIgnoreCase)))
            throw new NotImplementedException($"ListView.{member} assignment replacing or clearing this binding is unmeasured (#1085)");
        properties[member] = images is null ? Vb6Value.Nothing : new Vb6Value((Control)images);
        if (images is not null)
        {
            if (!images.BoundListViews.Any(reference => reference.TryGetTarget(out var list) && ReferenceEquals(list, this)))
                images.BoundListViews.Add(new WeakReference<VBListView>(this));
        }
        else
            foreach (var item in Items.Entries) item.Icon = new Vb6Value(0);
        InvalidateVisual();
    }

    internal Vb6Value ValidateImage(string member, Vb6Value value)
    {
        var images = Images(member) ?? throw ListViewItems.Error(35613);
        if (value.Type != Vb6Value.ValueType.Integer && value.Type != Vb6Value.ValueType.String)
            throw new NotImplementedException("ListView image selector conversion is unmeasured (#1085)");
        if (value.Type == Vb6Value.ValueType.Integer && (int)value.Value! < 1)
            throw new NotImplementedException("ListView clearing an image selector is unmeasured (#1085)");
        if (value.Type == Vb6Value.ValueType.Integer && (int)value.Value! > images.Images.Count)
            throw ListViewItems.Error(35600);
        if (value.Type == Vb6Value.ValueType.String)
        {
            try { _ = images.Images.Item(value); }
            catch (VBRunTimeException) { throw new NotImplementedException("ListView missing image key is unmeasured (#1085)"); }
        }
        return value;
    }

    private void DrawImage(DrawingContext context, string member, Vb6Value selector, Rect bounds, bool selected = false, bool ghosted = false)
    {
        if (Images(member) is not { } images || selector.Type == Vb6Value.ValueType.EmptyVariant || selector.Value is int zero && zero == 0)
            return;
        var image = (ImageListImage)images.Images.Item(selector).Value!;
        image.TryGetProperty("Picture", out var picture);
        using var bitmap = ToolbarImage.Decode((ImageListPictureObject)picture.Value!, images);
        if (selected || ghosted)
        {
            using var buffer = bitmap.Lock();
            if (buffer.Format != PixelFormat.Bgra8888 && buffer.Format != PixelFormat.Rgba8888)
                throw new NotImplementedException("ListView image pixel format is not implemented (#1085)");
            var pixels = new byte[buffer.RowBytes * buffer.Size.Height];
            Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
            var highlight = ((ISolidColorBrush)SelectionBrush()).Color;
            var red = buffer.Format == PixelFormat.Bgra8888 ? 2 : 0;
            for (var y = 0; y < buffer.Size.Height; y++)
            for (var x = 0; x < buffer.Size.Width; x++)
            {
                var offset = y * buffer.RowBytes + x * 4;
                if (ghosted && (x + y) % 2 == 0)
                    Array.Clear(pixels, offset, 4);
                else if (selected)
                {
                    pixels[offset + red] = (byte)((pixels[offset + red] + highlight.R * pixels[offset + 3] / 255) / 2);
                    pixels[offset + 1] = (byte)((pixels[offset + 1] + highlight.G * pixels[offset + 3] / 255) / 2);
                    pixels[offset + 2 - red] = (byte)((pixels[offset + 2 - red] + highlight.B * pixels[offset + 3] / 255) / 2);
                }
            }
            Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        }
        context.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), bounds);
    }

    internal Rect GetItemIconBounds(int index)
    {
        foreach (var (item, cell, _) in ItemCells())
            if (ReferenceEquals(item, Items.Entries[index]))
                return ItemIconBounds(cell);
        return default;
    }

    private Rect ItemIconBounds(Rect cell) => Number("View") == 0
        ? new Rect(cell.X + 21, cell.Y + 2, 16, 16)
        : new Rect(cell.X + 2 + (Checkboxes ? 16 : 0), cell.Y, 16, 16);
}
