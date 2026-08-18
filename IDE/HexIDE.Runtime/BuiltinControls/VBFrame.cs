using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace HexIDE.Runtime.BuiltinControls;

public class VBFrame : HeaderedContentControl, IVBContainerControl
{
    // Its own style key, not HeaderedContentControl's. SimpleTheme's stock template draws no border at all
    // — a loaded Frame rendered as a bare floating caption — and puts its content presenter below the
    // header and inside the border, which would displace every contained control by an amount that depends
    // on whichever theme happens to be loaded.
    protected override Type StyleKeyOverride => typeof(VBFrame);

    /// <summary>
    /// The Canvas this Frame's contained controls live on.
    ///
    /// A styled property the component class fills in, presented by the template — deliberately not a
    /// <c>PART_</c> looked up from the template's name scope. <c>VBLoader.SpawnComponents</c> builds the
    /// whole control tree before it is assigned to the window's Content, so at the moment children are
    /// placed no template has been applied and no part exists to find. Owning the Canvas also means a
    /// re-templated Frame keeps the children it already has: the new presenter takes the same Canvas rather
    /// than leaving them orphaned in an old part.
    /// </summary>
    public static readonly StyledProperty<Canvas?> ChildHostProperty =
        AvaloniaProperty.Register<VBFrame, Canvas?>(nameof(ChildHost));

    public Canvas? ChildHost
    {
        get => GetValue(ChildHostProperty);
        set => SetValue(ChildHostProperty, value);
    }

    static VBFrame()
    {
        AttachedEvents.AttachClick<VBFrame>();
    }
}
