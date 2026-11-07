using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Utils;
using HexIDE.IDE;

namespace HexIDE.Controls;

public class PropertyFontBox : TemplatedControl
{
    public static readonly StyledProperty<VBFont> FontProperty = AvaloniaProperty.Register<PropertyFontBox, VBFont>("Font", defaultBindingMode: BindingMode.TwoWay, defaultValue: VBFont.Default);
    public static readonly DirectProperty<PropertyFontBox, string?> FontNameProperty = AvaloniaProperty.RegisterDirect<PropertyFontBox, string?>("FontName", o => o.FontName);

    public string? FontName
    {
        get => Font.FontFamilyName;
    }

    public VBFont Font
    {
        get => GetValue(FontProperty);
        set => SetValue(FontProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        var openFontWindowButton = e.NameScope.Get<Button>("PART_OpenFontWindowButton");
        openFontWindowButton.Click += OnButtonClick;
    }

    internal FontDialogResult InitialFont => new(new FontFamily(Font.FontFamilyName), Font.ToFontStyle(), Font.ToFontWeight(), Font.Size);

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        async Task OpenFontWindow()
        {
            var result = await Static.RootViewModel.WindowManager.ShowFontDialog(InitialFont);
            if (result != null)
                SetCurrentValue(FontProperty, Chosen(Font, result));
        }

        OpenFontWindow().ListenErrors();
    }

    /// <summary>
    /// The font the dialog's answer makes of <paramref name="font"/> (#910). The size is kept whole, 8.25 and 9.6 included,
    /// where a cast to int made them 8 and 9. The dialog has no say over underline and strikethrough, so they are kept; nor
    /// over a weight it shows as neither bold nor regular, so that is kept unless Bold was changed; nor over the charset,
    /// which is kept while the family is, and a new family starts at 0, as it did before.
    /// </summary>
    internal static VBFont Chosen(VBFont font, FontDialogResult result)
    {
        var bold = result.Weight >= FontWeight.Bold;
        var family = result.Family.Name;
        var sameFamily = string.Equals(family, font.FontFamilyName, System.StringComparison.OrdinalIgnoreCase);
        return new VBFont(family, result.Size,
            italic: result.Style == FontStyle.Italic,
            charset: sameFamily ? font.Charset : 0,
            underline: font.Underline,
            strikethrough: font.Strikethrough,
            weight: bold == font.Bold ? font.Weight : bold ? VBFont.BoldWeight : VBFont.NormalWeight);
    }

    static PropertyFontBox()
    {
        FontProperty.Changed.AddClassHandler<PropertyFontBox>((box, e) =>
        {
            box.RaisePropertyChanged(FontNameProperty, null, box.FontName);
        });
    }
}