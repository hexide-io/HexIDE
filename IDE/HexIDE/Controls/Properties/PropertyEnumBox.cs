using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Controls;

public class PropertyEnumBox : TemplatedControl
{
    private ComboBox? comboBox;
    private bool syncing;

    public static readonly StyledProperty<object?> SelectedValueProperty = AvaloniaProperty.Register<PropertyEnumBox, object?>("SelectedValue");
    public static readonly StyledProperty<PropertyEnumViewModel?> SelectedViewModelProperty = AvaloniaProperty.Register<PropertyEnumBox, PropertyEnumViewModel?>("SelectedViewModel");
    public static readonly StyledProperty<Type?> PropertyTypeProperty = AvaloniaProperty.Register<PropertyEnumBox, Type?>("PropertyType");

    public static readonly StyledProperty<List<PropertyEnumViewModel>?> OptionsProperty = AvaloniaProperty.Register<PropertyEnumBox, List<PropertyEnumViewModel>?>("Options");

    public Type? PropertyType
    {
        get => GetValue(PropertyTypeProperty);
        set => SetValue(PropertyTypeProperty, value);
    }

    public List<PropertyEnumViewModel>? Options
    {
        get => GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public PropertyEnumViewModel? SelectedViewModel
    {
        get => GetValue(SelectedViewModelProperty);
        set => SetValue(SelectedViewModelProperty, value);
    }

    public object? SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    static PropertyEnumBox()
    {
        PropertyTypeProperty.Changed.AddClassHandler<PropertyEnumBox>((box, e) =>
        {
            box.UpdateOptions();
        });
        SelectedValueProperty.Changed.AddClassHandler<PropertyEnumBox>((box, e) =>
        {
            if (box.syncing)
                return;
            box.syncing = true;
            if (box.Options != null && box.PropertyType != null && box.SelectedValue != null)
            {
                if (box.PropertyType.IsEnum)
                {
                    var selectedUnderlyingValue = Convert.ChangeType(box.SelectedValue, box.PropertyType.GetEnumUnderlyingType());
                    box.SetCurrentValue(SelectedViewModelProperty, box.Options.FirstOrDefault(opt => Equals(opt.UnderlyingValue, selectedUnderlyingValue)));
                }
                else if (box.PropertyType == typeof(bool))
                {
                    box.SetCurrentValue(SelectedViewModelProperty, box.Options.FirstOrDefault(opt => Equals(opt.UnderlyingValue, box.SelectedValue)));
                }
                else
                    throw new Exception("this should never happen!");
            }
            box.syncing = false;
        });
        SelectedViewModelProperty.Changed.AddClassHandler<PropertyEnumBox>((box, e) =>
        {
            if (box.syncing)
                return;
            box.syncing = true;
            if (box.SelectedViewModel == null || box.PropertyType == null)
                box.SetCurrentValue(SelectedValueProperty, null);
            else if (box.PropertyType.IsEnum)
               box.SetCurrentValue(SelectedValueProperty, Enum.ToObject(box.PropertyType, box.SelectedViewModel.UnderlyingValue));
            else if (box.PropertyType == typeof(bool))
                box.SetCurrentValue(SelectedValueProperty, box.SelectedViewModel.UnderlyingValue);
            else
                throw new Exception($"This should never happen.");
            box.syncing = false;
        });
    }

    private void UpdateOptions()
    {
        if (PropertyType != null && PropertyType.IsEnum)
        {
            // VB6's name for the value, not the C# member's. They are not the same string for 33 of the
            // values shown here, and one enum was worse than a spelling difference: VBAlign's members are
            // named vbAlignTop / vbAlignBottom, so a Frame's Align property offered "1 - vbAlignTop" where
            // VB6 offers "1 - Align Top".
            //
            // Vb6EnumNames is the serializer's source for the comment it writes beside an enum in the .frm
            // (`Align = 1  'Align Top`), so reading it here is what stops HexIDE's two halves disagreeing
            // about what one value is called — pick "Grayscale" in the designer, and the file said "Grayed".
            //
            // The fallback is the old behaviour, and stays deliberately: For() returns null for a member
            // with no attribute, and a C# name is a better guess than no name at all in a dropdown. That is
            // the opposite of the serializer's choice, which writes NO comment rather than one VB6 would not
            // recognise — a dropdown has to show the user something, a file does not.
            var newOptions = new List<PropertyEnumViewModel>();
            foreach (var value in Enum.GetValues(PropertyType))
            {
                var underlying = Convert.ChangeType(value, PropertyType.GetEnumUnderlyingType());
                var name = Vb6EnumNames.For(value) ?? value.ToString()!.TrimStart('_');
                newOptions.Add(new PropertyEnumViewModel(underlying, $"{underlying} - {name}"));
            }
            SetCurrentValue(OptionsProperty, newOptions);
            if (SelectedValue != null)
            {
                var selectedUnderlyingValue = Convert.ChangeType(SelectedValue, PropertyType.GetEnumUnderlyingType());
                SetCurrentValue(SelectedViewModelProperty, newOptions.FirstOrDefault(opt => Equals(opt.UnderlyingValue, selectedUnderlyingValue)));
            }
            else
            {
                SetCurrentValue(SelectedViewModelProperty, null);
            }
        }
        else if (PropertyType == typeof(bool))
        {
            var newOptions = new List<PropertyEnumViewModel>();
            newOptions.Add(new PropertyEnumViewModel(false, "False"));
            newOptions.Add(new PropertyEnumViewModel(true, "True"));
            SetCurrentValue(OptionsProperty, newOptions);
            if (SelectedValue is bool b)
            {
                SetCurrentValue(SelectedViewModelProperty, newOptions.FirstOrDefault(opt => Equals(opt.UnderlyingValue, b)));
            }
            else
            {
                SetCurrentValue(SelectedViewModelProperty, null);
            }
        }
        else
        {
            SetCurrentValue(OptionsProperty, []);
            SetCurrentValue(SelectedViewModelProperty, null);
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        comboBox = e.NameScope.Get<ComboBox>("PART_ComboBox");
    }
}

public class PropertyEnumViewModel
{
    public object UnderlyingValue { get; }
    private string toString;

    public string Text => toString;

    public PropertyEnumViewModel(object underlyingValue,
        string name)
    {
        UnderlyingValue = underlyingValue;
        toString = name;
    }

    public override string ToString() => toString;
}