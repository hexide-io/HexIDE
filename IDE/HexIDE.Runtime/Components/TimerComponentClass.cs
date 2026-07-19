using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

public class TimerComponentClass : ComponentBaseClass
{
    public TimerComponentClass() : base([EnabledProperty, IntervalProperty], [TimerEvent])
    {
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