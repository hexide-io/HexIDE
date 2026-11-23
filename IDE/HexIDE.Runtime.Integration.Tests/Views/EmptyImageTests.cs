using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.Integration.Tests.Views;

public class EmptyImageTests
{
    // VB6 SP6: docs/vb6-fidelity-oracle/an-empty-listbox-an-empty-image-and-a-label-narrower-than-its-caption.md (#860).
    [AvaloniaFact]
    public void An_empty_Image_draws_nothing_in_a_run()
    {
        var image = (VBImage)ImageComponentClass.Instance.Instantiate(new ComponentInstance(ImageComponentClass.Instance, "Image1"));
        image.Width = 81;
        image.Height = 65;
        image.Measure(new Size(81, 65));
        image.Arrange(new Rect(0, 0, 81, 65));
        using var bitmap = new RenderTargetBitmap(new PixelSize(81, 65), new Vector(96, 96));
        bitmap.Render(image);
        var pixels = new byte[81 * 65 * 4];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(0, 0, 81, 65), pin.AddrOfPinnedObject(), pixels.Length, 81 * 4); }
        finally { pin.Free(); }
        pixels.Should().OnlyContain(value => value == 0);
    }
}
