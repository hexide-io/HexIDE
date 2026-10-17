using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Runtime.BuiltinControls;

public class AttachedProperties
{
    public static readonly AttachedProperty<VBColor?> BackColorProperty = AvaloniaProperty.RegisterAttached<VBProps, TemplatedControl, VBColor?>("BackColor", null);
    public static VBColor? GetBackColor(AvaloniaObject element) => element.GetValue(BackColorProperty);
    public static void SetBackColor(AvaloniaObject element, VBColor? value) => element.SetValue(BackColorProperty, value);

    public static readonly AttachedProperty<VBColor?> ForeColorProperty = AvaloniaProperty.RegisterAttached<VBProps, TemplatedControl, VBColor?>("ForeColor", null);
    public static VBColor? GetForeColor(AvaloniaObject element) => element.GetValue(ForeColorProperty);
    public static void SetForeColor(AvaloniaObject element, VBColor? value) => element.SetValue(ForeColorProperty, value);

    public static readonly AttachedProperty<VBFont?> FontProperty = AvaloniaProperty.RegisterAttached<VBProps, TemplatedControl, VBFont?>("Font", null);
    public static VBFont? GetFont(AvaloniaObject element) => element.GetValue(FontProperty);
    public static void SetFont(AvaloniaObject element, VBFont? value) => element.SetValue(FontProperty, value);

    static AttachedProperties()
    {
        BackColorProperty.Changed.AddClassHandler<TemplatedControl>((control, e) =>
        {
            if (e.NewValue is VBColor color)
                control.Background = color.ToBrush();
            else
                control.Background = Brushes.Transparent;
        });
        ForeColorProperty.Changed.AddClassHandler<TemplatedControl>((control, e) =>
        {
            if (e.NewValue is VBColor color)
                control.Foreground = color.ToBrush();
            else
                control.Foreground = Brushes.Transparent;
        });
        FontProperty.Changed.AddClassHandler<TemplatedControl>((control, e) =>
        {
            if (e.NewValue is VBFont font)
            {
                control.FontFamily = font.ToFontFamily();
                control.FontWeight = font.ToFontWeight();
                control.FontSize = font.Size;
                control.FontStyle = font.ToFontStyle();
                // Only on the controls that draw a caption in their own font. It is inherited, so set on a form it would
                // reach the form's menus, which VB6 draws in the menu font whatever the form's is.
                if (control is VBLabel or VBCommandButton or VBCheckBox or VBOptionButton)
                    SymbolFontText.SetSymbolGlyphs(control, SymbolFontText.UsesSymbolGlyphs(font));
            }
        });
    }
}