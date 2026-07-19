using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using HexIDE.Runtime.BuiltinControls;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

public class FrameComponentClass : ComponentBaseClass
{
    public FrameComponentClass() : base([CaptionProperty,
    BackColorProperty,
    ForeColorProperty,
    FontProperty], [ClickEvent])
    {
    }

    public override string Name => "Frame";
    public override string VBTypeName => "VB.Frame";

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        return new VBFrame()
        {
            Header = instance.GetPropertyOrDefault(CaptionProperty),
            [AttachedProperties.BackColorProperty] = instance.GetPropertyOrDefault(BackColorProperty),
            [AttachedProperties.ForeColorProperty] = instance.GetPropertyOrDefault(ForeColorProperty),
            [AttachedProperties.FontProperty] = instance.GetPropertyOrDefault(FontProperty),
        };
    }

    public static ComponentBaseClass Instance { get; } = new FrameComponentClass();
}