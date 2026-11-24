using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Runtime.Components;

/// <summary>
/// Draws a control with the provider's own controls, in the IDE's process, and hands back its pixels. The provider registers
/// it as what draws the designer's controls (#5591), and the designer lends each control a place while it's drawn.
/// </summary>
internal sealed class InProcessDesignerPictures(IDesignerDrawingSurface host) : IDesignerPictures
{
    public DesignerPicture Draw(ComponentInstance instance, DesignerDrawRequest request)
    {
        // The toolbox offers any class with Core's designer facts, so one that can't draw itself is refused here, in its place.
        if (instance.BaseClass is not IDesignerComponentClass drawn)
            throw new NotSupportedException($"{instance.BaseClass.Name} doesn't draw itself in the designer.");
        var visual = drawn.InstantiateForDesigner(instance);
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
            // LayoutUpdated can change row padding; lay that out before capturing the picture (#5121).
            visual.UpdateLayout();

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
