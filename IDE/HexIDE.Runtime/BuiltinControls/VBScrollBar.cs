using System;
using System.Collections.Generic;
using Avalonia.Controls.Primitives;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

public class VBScrollBar : ScrollBar
{
    protected override Type StyleKeyOverride => typeof(ScrollBar);

    /// <summary>
    /// VB6's Min and Max, as the form set them. They can differ from <see cref="RangeBase.Minimum"/> and
    /// <see cref="RangeBase.Maximum"/>: VB6 lets Min exceed Max, and Avalonia raises Maximum to Minimum (#787).
    /// </summary>
    public int VbMin { get; init; }

    /// <inheritdoc cref="VbMin"/>
    public int VbMax { get; init; }

    public VBScrollBar()
    {
        ValueChanged += OnValueChanged;
    }

    private void OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        this.ExecuteSub(ScrollBarComponentClass.ChangeEvent);
    }
}