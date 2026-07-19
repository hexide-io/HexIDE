using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.BuiltinControls;

public class VBPictureBox : TemplatedControl
{
    public static readonly StyledProperty<byte[]?> PictureDataProperty =
        AvaloniaProperty.Register<VBPictureBox, byte[]?>(nameof(PictureData));

    public static readonly StyledProperty<IImage?> PictureProperty =
        AvaloniaProperty.Register<VBPictureBox, IImage?>(nameof(Picture));

    public byte[]? PictureData
    {
        get => GetValue(PictureDataProperty);
        set => SetValue(PictureDataProperty, value);
    }

    public IImage? Picture
    {
        get => GetValue(PictureProperty);
        set => SetValue(PictureProperty, value);
    }

    static VBPictureBox()
    {
        AttachedEvents.AttachClick<VBPictureBox>();
        PictureDataProperty.Changed.AddClassHandler<VBPictureBox>((box, _) => box.OnPictureDataChanged());
    }

    private void OnPictureDataChanged()
    {
        Bitmap? bmp = FrxImageHelper.TryDecodeToAvaloniaBitmap(PictureData);
        SetValue(PictureProperty, bmp);
    }
}