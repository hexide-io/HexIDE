using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace HexIDE.Runtime;

/// <summary>
/// Where a running form stacks a control among the others in its container (#1497), by its <c>ZIndex</c>, so the order
/// of a container's children stays the order the file holds them in.
/// </summary>
/// <remarks>
/// <para>
/// Measured in VB6 at run time (docs/vb6-fidelity-oracle/where-vb6s-designer-puts-a-controls-block.md, "Where VB6's designer puts a control's block", and "How a
/// running form stacks its controls"):
/// <list type="bullet">
/// <item>The first block in a container is its top control.</item>
/// <item>A windowless control (Label, Shape, Line, Image) is drawn under every windowed control in its container, whatever
/// their order. Among the windowless ones, the first block is on top, whatever the class.</item>
/// <item>A control-array element loaded at run time goes under every control of its plane already there.</item>
/// <item><c>ZOrder 0</c> puts a control over every control of its plane in its container, and <c>ZOrder 1</c> under
/// them; neither takes it out of its plane (#1503).</item>
/// </list>
/// </para>
/// <para>
/// So one rule places them all: a control goes under every control of its plane already in its container. Placing a
/// form's controls in file order then stacks the first on top, and a loaded element at the bottom.
/// </para>
/// <para>
/// A control's plane is recorded as it is placed (<see cref="WindowlessProperty"/>), and every placement numbers its plane
/// afresh, top first: windowed from 0 down, windowless from <see cref="WindowlessTop"/> down. So a plane's numbers are
/// always those of its controls, however often they are restacked. They used to go one past the plane's top or bottom at
/// each placement, and the plane was read back from the number: two controls brought forward in turn climbed by one a
/// call, and after about a million crossed into the other plane.
/// </para>
/// </remarks>
internal sealed class ControlStacking
{
    private ControlStacking()
    {
    }

    // The windowless plane's top. A container would need a million windowed controls to reach it.
    private const int WindowlessTop = -1_000_000;

    /// <summary>Whether a control is in the windowless plane, as it was placed.</summary>
    public static readonly AttachedProperty<bool> WindowlessProperty =
        AvaloniaProperty.RegisterAttached<ControlStacking, Control, bool>("Windowless");

    /// <summary>Whether a control placed here is in the windowless plane.</summary>
    public static bool IsWindowless(Control control) => control.GetValue(WindowlessProperty);

    /// <summary>Gives <paramref name="control"/> a ZIndex under every control of its plane in <paramref name="container"/>.</summary>
    public static void PlaceUnder(Control control, bool windowless, Panel container) => Place(control, windowless, container, top: false);

    /// <summary>Gives <paramref name="control"/> a ZIndex over every control of its plane in <paramref name="container"/>.</summary>
    public static void PlaceOver(Control control, bool windowless, Panel container) => Place(control, windowless, container, top: true);

    private static void Place(Control control, bool windowless, Panel container, bool top)
    {
        control.SetValue(WindowlessProperty, windowless);
        // The rest of its plane as it stands, top first, with the control at the top or the bottom, numbered down from the
        // plane's top. The control may not be among the children yet: the loader places one before adding it.
        var plane = container.Children
            .Where(child => !ReferenceEquals(child, control) && IsWindowless(child) == windowless)
            .OrderByDescending(child => child.ZIndex)
            .ToList();
        plane.Insert(top ? 0 : plane.Count, control);
        var number = windowless ? WindowlessTop : 0;
        foreach (var member in plane)
            member.ZIndex = number--;
        // A ZIndex changed on its own does not redraw the container: a control brought over another stayed under it in the
        // next frame until the container was invalidated.
        container.InvalidateVisual();
    }
}
