using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;

namespace HexIDE.Controls;

/// <summary>Where a control being drawn sits for the moment, which is what gives it its styles and resources.</summary>
internal interface IDesignerDrawingHost
{
    void Attach(Control visual);
    void Detach(Control visual);
    void UpdateLayout();
}

/// <summary>Draws a control with the engine's own controls, in this process, and hands back its pixels.</summary>
internal sealed class InProcessDesignerPictures(IDesignerDrawingHost host) : IDesignerPictures
{
    public DesignerPicture Draw(ComponentInstance instance, DesignerDrawRequest request)
    {
        var visual = ((IDesignerComponentClass)instance.BaseClass).InstantiateForDesigner(instance);
        using var target = new RenderTargetBitmap(new PixelSize(request.PixelWidth, request.PixelHeight), new Vector(96, 96));
        // A Line draws itself from its endpoints in its container's scale, which only the form knows (#163).
        if (visual is VBLine line)
            line.Resolve(request.LineTwipsPerUnit);
        host.Attach(visual);
        try
        {
            visual.Width = target.PixelSize.Width;
            visual.Height = target.PixelSize.Height;
            visual.Measure(new Size(target.PixelSize.Width, target.PixelSize.Height));
            visual.Arrange(new Rect(0, 0, target.PixelSize.Width, target.PixelSize.Height));

            host.UpdateLayout();

            TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Antialias);
            target.Render(visual);
            return new DesignerPicture(target.PixelSize.Width, target.PixelSize.Height, PixelsOf(target),
                visual.MinWidth, visual.MinHeight, visual.MaxWidth, visual.MaxHeight);
        }
        finally
        {
            host.Detach(visual);
        }
    }

    private static byte[] PixelsOf(RenderTargetBitmap target)
    {
        var stride = target.PixelSize.Width * 4;
        var pixels = new byte[stride * target.PixelSize.Height];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            target.CopyPixels(new PixelRect(target.PixelSize), pin.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            pin.Free();
        }
        return target.Format == PixelFormat.Rgba8888 ? SwapRedAndBlue(pixels) : pixels;
    }

    // Skia's native order differs by platform, and the picture's doesn't.
    internal static byte[] SwapRedAndBlue(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 4)
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        return pixels;
    }
}
