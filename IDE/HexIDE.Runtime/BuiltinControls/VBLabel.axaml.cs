using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using Classic.Avalonia.Theme;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

public class VBLabel : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<VBLabel, string?>(nameof(Text));

    public static readonly StyledProperty<VBColor> BackColorProperty = AvaloniaProperty.Register<VBLabel, VBColor>(nameof(BackColor));

    public static readonly StyledProperty<BackStyles> BackStyleProperty = AvaloniaProperty.Register<VBLabel, BackStyles>(nameof(BackStyle));

    public static readonly StyledProperty<VBAppearance> AppearanceProperty = AvaloniaProperty.Register<VBLabel, VBAppearance>(nameof(Appearance));

    public static readonly StyledProperty<VBBorder> BorderStyleProperty = AvaloniaProperty.Register<VBLabel, VBBorder>(nameof(BorderStyle));

    public static readonly StyledProperty<VBTextAlignment> AlignmentProperty = AvaloniaProperty.Register<VBLabel, VBTextAlignment>(nameof(Alignment));

    public static readonly StyledProperty<bool> WordWrapProperty = AvaloniaProperty.Register<VBLabel, bool>(nameof(WordWrap));

    public static readonly StyledProperty<bool> RecognizesAccessKeyProperty = AvaloniaProperty.Register<VBLabel, bool>(nameof(RecognizesAccessKey));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public VBColor BackColor
    {
        get => GetValue(BackColorProperty);
        set => SetValue(BackColorProperty, value);
    }

    public BackStyles BackStyle
    {
        get => GetValue(BackStyleProperty);
        set => SetValue(BackStyleProperty, value);
    }

    public VBAppearance Appearance
    {
        get => GetValue(AppearanceProperty);
        set => SetValue(AppearanceProperty, value);
    }

    public VBBorder BorderStyle
    {
        get => GetValue(BorderStyleProperty);
        set => SetValue(BorderStyleProperty, value);
    }

    public VBTextAlignment Alignment
    {
        get => GetValue(AlignmentProperty);
        set => SetValue(AlignmentProperty, value);
    }

    public bool WordWrap
    {
        get => GetValue(WordWrapProperty);
        set => SetValue(WordWrapProperty, value);
    }

    public bool RecognizesAccessKey
    {
        get => GetValue(RecognizesAccessKeyProperty);
        set => SetValue(RecognizesAccessKeyProperty, value);
    }

    private ClassicBorderDecorator? decorator;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        decorator = e.NameScope.Get<ClassicBorderDecorator>("PART_Border");
        UpdateStyle();
    }

    // Named as its caption reads, the access key's & dropped only where UseMnemonic marks one (#954).
    private void UpdateAutomationName()
    {
        var shown = Text is { } text && RecognizesAccessKey ? VBCaption.AsShown(text) : Text;
        if (shown is { Length: > 0 })
            SetValue(Avalonia.Automation.AutomationProperties.NameProperty, shown);
        else
            ClearValue(Avalonia.Automation.AutomationProperties.NameProperty);
    }

    static VBLabel()
    {
        AffectsRender<VBLabel>(BackColorProperty, BackStyleProperty);
        AppearanceProperty.Changed.AddClassHandler<VBLabel>((label, e) => label.UpdateStyle());
        BorderStyleProperty.Changed.AddClassHandler<VBLabel>((label, e) => label.UpdateStyle());
        AlignmentProperty.Changed.AddClassHandler<VBLabel>((label, e) => label.UpdateStyle());
        WordWrapProperty.Changed.AddClassHandler<VBLabel>((label, e) => label.UpdateStyle());
        RecognizesAccessKeyProperty.Changed.AddClassHandler<VBLabel>((label, e) => { label.UpdateStyle(); label.UpdateAutomationName(); });
        TextProperty.Changed.AddClassHandler<VBLabel>((label, e) => { label.UpdateStyle(); label.UpdateAutomationName(); });
        SymbolFontText.SymbolGlyphsProperty.Changed.AddClassHandler<VBLabel>((label, e) => label.UpdateStyle());

        // Before Click, so MouseUp comes first, as VB6 raises them (#974).
        AttachedEvents.AttachMouse<VBLabel>();
        AttachedEvents.AttachClick<VBLabel>();
    }

    private void UpdateStyle()
    {
        if (decorator == null)
            return;

        // Measured with vb6.exe (#1077): Fixed Single is a sunken edge two pixels deep when 3D, and one pixel of the window
        // frame colour when Flat. The 3D shades come from the template.
        if (BorderStyle == VBBorder.None)
        {
            decorator.BorderStyle = ClassicBorderStyle.None;
            decorator.BorderThickness = new Thickness(0);
        }
        else if (BorderStyle == VBBorder.FixedSingle)
        {
            if (Appearance == VBAppearance.Flat)
            {
                decorator.BorderStyle = ClassicBorderStyle.Thin;
                decorator.BorderThickness = new Thickness(1);
                decorator[!ClassicBorderDecorator.BorderBrushProperty] =
                    decorator.GetResourceObservable(VbSystemColor.Windowframe.GetKey()).ToBinding();
            }
            else if (Appearance == VBAppearance._3D)
            {
                decorator.BorderStyle = ClassicBorderStyle.Sunken;
                decorator.BorderThickness = new Thickness(2);
            }
        }

        if (RecognizesAccessKey && (decorator.Child == null || decorator.Child is TextBlock))
        {
            decorator.Child = new AccessText();
        }
        else if (!RecognizesAccessKey && (decorator.Child == null || decorator.Child is AccessText))
        {
            decorator.Child = new TextBlock();
        }

        var textblock = (decorator.Child as TextBlock)!;
        // In a symbol font, the glyphs GDI draws for the caption's bytes (#1133); the caption itself is unchanged.
        var text = Text is { } caption && SymbolFontText.GetSymbolGlyphs(this) ? SymbolFontText.ToSymbolGlyphs(caption) : Text;
        textblock.Text = RecognizesAccessKey && text != null && text.Contains('&') ? text.Replace("&", "_") : text;
        textblock.TextWrapping = WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        textblock.TextAlignment = Alignment switch
        {
            VBTextAlignment.LeftJustify => TextAlignment.Left,
            VBTextAlignment.RightJustify => TextAlignment.Right,
            VBTextAlignment.Center => TextAlignment.Center,
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public override void Render(DrawingContext context)
    {
        if (BackStyle == BackStyles.Transparent)
            return;

        if (BackStyle == BackStyles.Opaque)
            context.FillRectangle(BackColor.ToBrush(), new Rect(0, 0, Bounds.Width, Bounds.Height));
    }
}