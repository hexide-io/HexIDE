using System;
using System.Collections.Generic;
using Avalonia.Controls.Primitives;
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
    }

    public VBScrollBar()
    {
        ValueChanged += OnValueChanged;
    }

    private void OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        this.ExecuteSub(ScrollBarComponentClass.ChangeEvent);
    }
}