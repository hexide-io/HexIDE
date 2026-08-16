using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.BuiltinControls;

public class VBCheckBox : CheckBox
{
    public static readonly StyledProperty<VBAppearance> AppearanceProperty = AvaloniaProperty.Register<VBCheckBox, VBAppearance>(nameof(Appearance), VBAppearance._3D);

    public VBAppearance Appearance
    {
        get => GetValue(AppearanceProperty);
        set => SetValue(AppearanceProperty, value);
    }

    public static readonly StyledProperty<VBCheckValue> ValueProperty = AvaloniaProperty.Register<VBCheckBox, VBCheckValue>(nameof(Value));

    public VBCheckValue Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    protected override void OnClick()
    {
        base.OnClick();   // toggles IsChecked (user interaction only — a programmatic IsChecked/Value set skips this)
        // Mirror the new checked state back into the VB6 Value so `CheckBox.Value` reads current after a user click
        // (the ValueProperty→IsChecked sync is one-way). VB6 fires Click on EVERY user click — check AND uncheck —
        // not only when the box becomes checked.
        Value = IsChecked switch
        {
            true => VBCheckValue.Checked,
            null => VBCheckValue.Grayscale,
            _    => VBCheckValue.Unchecked,
        };
        this.ExecuteSub(ComponentBaseClass.ClickEvent);
    }

    static VBCheckBox()
    {
        AppearanceProperty.Changed.AddClassHandler<VBCheckBox>((checkBox, e) =>
        {
            if (checkBox.Appearance == VBAppearance._3D)
                checkBox.Theme = null;
            else
                checkBox.Theme = Application.Current?.FindResource("FlatVBCheckBox") as ControlTheme;
        });
        ValueProperty.Changed.AddClassHandler<VBCheckBox>((checkBox, e) =>
        {
            checkBox.IsThreeState = checkBox.Value == VBCheckValue.Grayscale;
            checkBox.IsChecked = checkBox.Value switch
            {
                VBCheckValue.Unchecked => false,
                VBCheckValue.Checked => true,
                VBCheckValue.Grayscale => null,
                _ => throw new ArgumentOutOfRangeException()
            };
        });
        ContentTemplateProperty.OverrideDefaultValue<VBCheckBox>(AccessTextDataTemplate.Access);
    }
}