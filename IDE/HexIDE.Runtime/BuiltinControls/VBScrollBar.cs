using System;
using System.Collections.Generic;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

public class VBScrollBar : ScrollBar
{
    protected override Type StyleKeyOverride => typeof(ScrollBar);

    public int VbMin { get; init; }
    public int VbMax { get; init; }

    // Keep Avalonia's range ascending; a reversed VB6 bar maps each thumb position the other way (#787).
    public int VbValue
    {
        get => (int)(VbMin > VbMax ? (double)VbMin + VbMax - Value : Value);
        set => Value = VbMin > VbMax ? (double)VbMin + VbMax - value : value;
    }

    static VBScrollBar()
    {
        // A scroll bar takes the focus, as VB6's does (measured, #1101).
        FocusableProperty.OverrideDefaultValue<VBScrollBar>(true);
        AttachedEvents.AttachFocusEvents<VBScrollBar>();
        ControlKeyEvents.Attach<VBScrollBar>();
    }

    public VBScrollBar()
    {
        ValueChanged += OnValueChanged;
    }

    internal void MoveFromKey(Key key)
    {
        // #4575 / oracle PR #4574: arrows and pages follow Min/Max, Home/End the numeric ends.
        var direction = VbMin > VbMax ? -1 : 1;
        var value = key switch
        {
            Key.Left or Key.Up => VbValue - direction * SmallChange,
            Key.Right or Key.Down => VbValue + direction * SmallChange,
            Key.PageUp => VbValue - direction * LargeChange,
            Key.PageDown => VbValue + direction * LargeChange,
            Key.Home => Math.Min(VbMin, VbMax),
            Key.End => Math.Max(VbMin, VbMax),
            _ => VbValue,
        };
        VbValue = (int)Math.Clamp(value, Math.Min(VbMin, VbMax), Math.Max(VbMin, VbMax));
    }

    private void OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        this.ExecuteSub(ScrollBarComponentClass.ChangeEvent);
    }
}