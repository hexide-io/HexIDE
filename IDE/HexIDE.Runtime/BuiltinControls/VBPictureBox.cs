using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Classic.Avalonia.Theme;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.BuiltinControls;

public class VBPictureBox : TemplatedControl, IVBContainerControl
{
    /// <summary>
    /// The Canvas this PictureBox's contained controls live on — the same owned-property seam VBFrame uses,
    /// and for the same reason: children are placed before any template has been applied, so there is no
    /// part to look up at the moment they arrive.
    ///
    /// Unlike a Frame's, this host sits INSIDE the border decorator, so its origin is the client origin by
    /// construction rather than by arithmetic that could drift from what is drawn.
    /// </summary>
    public static readonly StyledProperty<Canvas?> ChildHostProperty =
        AvaloniaProperty.Register<VBPictureBox, Canvas?>(nameof(ChildHost));

    public Canvas? ChildHost
    {
        get => GetValue(ChildHostProperty);
        set => SetValue(ChildHostProperty, value);
    }

    /// <summary>
    /// How the border is drawn, translated from VB6's <c>BorderStyle</c> and <c>Appearance</c> by the
    /// component class. Both are design-time-only in VB6, so translating once at instantiation loses
    /// nothing — and the previous template hardcoded <c>Sunken</c>/<c>2</c>, which drew a 3-D border around
    /// a PictureBox the file said was flat and borderless.
    /// </summary>
    public static readonly StyledProperty<ClassicBorderStyle> ClientBorderStyleProperty =
        AvaloniaProperty.Register<VBPictureBox, ClassicBorderStyle>(nameof(ClientBorderStyle), ClassicBorderStyle.Sunken);

    public ClassicBorderStyle ClientBorderStyle
    {
        get => GetValue(ClientBorderStyleProperty);
        set => SetValue(ClientBorderStyleProperty, value);
    }

    /// <summary>
    /// The border's thickness, which is also the client inset — one number, because the decorator insets its
    /// child by exactly what it draws. That is the whole point of hosting children inside it: the offset a
    /// contained control is laid out at cannot disagree with the border the user can see.
    /// </summary>
    public static readonly StyledProperty<Thickness> ClientInsetProperty =
        AvaloniaProperty.Register<VBPictureBox, Thickness>(nameof(ClientInset), new Thickness(2));

    public Thickness ClientInset
    {
        get => GetValue(ClientInsetProperty);
        set => SetValue(ClientInsetProperty, value);
    }

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