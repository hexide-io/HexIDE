using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>
/// VB6's <c>Image</c> control — a lightweight picture holder. Unlike <c>PictureBox</c> it is a windowless
/// control with no drawing methods and no ability to contain anything; the only thing it does is show a
/// picture, optionally stretched.
/// </summary>
public class VBImage : Control
{
    protected override Type StyleKeyOverride => typeof(Control);

    public static readonly StyledProperty<byte[]?> PictureProperty =
        AvaloniaProperty.Register<VBImage, byte[]?>(nameof(Picture));

    public static readonly StyledProperty<bool> StretchProperty =
        AvaloniaProperty.Register<VBImage, bool>(nameof(Stretch));

    static VBImage()
    {
        AffectsRender<VBImage>(PictureProperty, StretchProperty);
    }

    /// <summary>
    /// The picture, as the bytes the <c>.frx</c> record held. Decoded lazily and cached, because the same
    /// blob may be shared by several controls and decoding is not cheap.
    /// </summary>
    public byte[]? Picture
    {
        get => GetValue(PictureProperty);
        set => SetValue(PictureProperty, value);
    }

    /// <summary>
    /// True to scale the picture to the control, false to size the control to the picture. VB6's default
    /// is false, which is why an un-stretched Image is exactly as big as what it shows.
    /// </summary>
    public bool Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    private byte[]? decodedFrom;
    private Bitmap? decoded;

    /// <summary>
    /// Decodes the blob to something renderable, or returns null when it cannot.
    ///
    /// The bytes are a <c>StdPicture</c> stream: a 4-byte preamble of 0x0000746C, a 4-byte size, then the
    /// image itself — an .ico, .bmp or .gif exactly as an image file would hold it, which is why handing
    /// the remainder straight to a decoder works. The layout is specified in [MS-OFORMS] §2.4.5.
    ///
    /// Anything else is passed through unchanged: a caller may reasonably hand over raw image bytes, and a
    /// blob that is neither is a picture HexIDE cannot show — which draws nothing rather than throwing,
    /// because a form must still open.
    /// </summary>
    private Bitmap? Decode()
    {
        var bytes = Picture;
        if (bytes is null || bytes.Length == 0) return null;
        if (ReferenceEquals(bytes, decodedFrom)) return decoded;

        decodedFrom = bytes;
        decoded = null;
        try
        {
            // Two layers to step over, in order: the .frx record's own framing, then the StdPicture
            // preamble inside it. The bytes are held as the record verbatim so the writer can put them
            // back untouched, which means a reader has to peel both.
            var offset = FrxDeserializer.PayloadOffset(bytes);
            offset += StdPictureHeaderLength(bytes, offset);
            if (offset >= bytes.Length) return null;
            using var stream = new MemoryStream(bytes, offset, bytes.Length - offset, writable: false);
            decoded = new Bitmap(stream);
        }
        catch (Exception)
        {
            // An unsupported codec (VB6 files carry .ico and .wmf freely) or a malformed blob. The picture
            // is missing, not the form.
        }
        return decoded;
    }

    /// <summary>
    /// How many bytes to skip before the image itself: 8 for a StdPicture stream, 0 for anything else.
    /// </summary>
    internal static int StdPictureHeaderLength(byte[] bytes, int from = 0)
    {
        const uint StdPicturePreamble = 0x0000746C;
        if (bytes.Length - from < 8) return 0;
        var preamble = (uint)(bytes[from] | (bytes[from + 1] << 8) | (bytes[from + 2] << 16) | (bytes[from + 3] << 24));
        return preamble == StdPicturePreamble ? 8 : 0;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Decode() is not { } bitmap) return;

        var source = new Rect(bitmap.Size);
        // Stretch fills the control; otherwise the picture is drawn at its own size from the top-left and
        // clipped by the control, which is what VB6 does when the control is smaller than the image.
        var destination = Stretch
            ? new Rect(Bounds.Size)
            : new Rect(0, 0, Math.Min(bitmap.Size.Width, Bounds.Width), Math.Min(bitmap.Size.Height, Bounds.Height));

        if (destination.Width <= 0 || destination.Height <= 0) return;
        if (!Stretch)
            source = new Rect(0, 0, destination.Width, destination.Height);

        context.DrawImage(bitmap, source, destination);
    }
}
