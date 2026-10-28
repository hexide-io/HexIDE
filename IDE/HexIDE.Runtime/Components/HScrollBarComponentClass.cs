using System;
using Avalonia.Controls;
using Avalonia.Layout;
using HexIDE.Runtime.BuiltinControls;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

public abstract class ScrollBarComponentClass : ComponentBaseClass
{
    protected ScrollBarComponentClass() : base([CausesValidationProperty, EnabledProperty,
        MousePointerProperty,
        SmallChangeProperty,
        LargeChangeProperty,
        MinProperty,
        ValueProperty,
        MaxProperty,
        TabStopProperty,
        TabIndexProperty], [ChangeEvent, GotFocusEvent, LostFocusEvent])
    {
    }

    protected abstract Orientation Orientation { get; }

    public static EventClass ChangeEvent = new EventClass("Change");

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        return new VBScrollBar()
        {
            Orientation = Orientation,
            Minimum = Math.Min(instance.GetPropertyOrDefault(MinProperty), instance.GetPropertyOrDefault(MaxProperty)),
            Maximum = Math.Max(instance.GetPropertyOrDefault(MinProperty), instance.GetPropertyOrDefault(MaxProperty)),
            VbMin = instance.GetPropertyOrDefault(MinProperty),
            VbMax = instance.GetPropertyOrDefault(MaxProperty),
            VbValue = instance.GetPropertyOrDefault(ValueProperty),
            LargeChange = instance.GetPropertyOrDefault(LargeChangeProperty),
            SmallChange = instance.GetPropertyOrDefault(SmallChangeProperty),
        };
    }
}

public class HScrollBarComponentClass : ScrollBarComponentClass
{
    public override string Name => "HScroll";
    public override string VBTypeName => "VB.HScrollBar";
    public override (int Width, int Height) AddedSize => (1215, 255);

    protected override Orientation Orientation => Orientation.Horizontal;

    public static ComponentBaseClass Instance { get; } = new HScrollBarComponentClass();
}