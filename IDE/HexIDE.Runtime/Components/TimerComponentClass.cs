using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

public class TimerComponentClass : ComponentBaseClass
{
    public TimerComponentClass() : base([EnabledProperty, IntervalProperty], [TimerEvent])
    {
    }

    // A .frm holds only a Timer's Left and Top, so its size comes from here: the icon's, as VBTimer's template pins it.
    static TimerComponentClass()
    {
        WidthProperty.OverrideDefault<TimerComponentClass>(28);
        HeightProperty.OverrideDefault<TimerComponentClass>(28);
    }

    public override string Name => "Timer";
    public override string VBTypeName => "VB.Timer";

    // A VB6 Timer is invisible — it has Left/Top (designer icon) but no Width/Height/Visible.
    public override bool IsVisual => false;

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        return new VBTimer()
        {
            Interval = instance[IntervalProperty]
        };
    }

    public static EventClass TimerEvent = new EventClass("Timer");

    public static ComponentBaseClass Instance { get; } = new TimerComponentClass();
}