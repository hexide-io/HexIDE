using System.Diagnostics.CodeAnalysis;
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
            ChildHost = CreateChildHost(),
            [AttachedProperties.BackColorProperty] = instance.GetPropertyOrDefault(BackColorProperty),
            [AttachedProperties.ForeColorProperty] = instance.GetPropertyOrDefault(ForeColorProperty),
            [AttachedProperties.FontProperty] = instance.GetPropertyOrDefault(FontProperty),
        };
    }

    /// <summary>
    /// A Frame's host fills its OUTER bounds — no inset. A Frame has no <c>Scale*</c> of its own in the
    /// .frm, and its children's Left/Top are measured from the control's own top-left rather than from
    /// inside the etched border. PictureBox is the one that insets, by the width of the border it draws.
    /// </summary>
    public override bool TryGetChildHost(Control control, [NotNullWhen(true)] out Canvas? host)
    {
        host = (control as VBFrame)?.ChildHost;
        return host is not null;
    }

    public static ComponentBaseClass Instance { get; } = new FrameComponentClass();
}