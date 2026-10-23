using System;
using Avalonia.Controls;

namespace HexIDE.Runtime;

/// <summary>
/// Where a running form stacks a control among the others in its container (#1497), by its <c>ZIndex</c>, so the order
/// of a container's children stays the order the file holds them in.
/// </summary>
/// <remarks>
/// <para>
/// Measured in VB6 at run time (docs/vb6-fidelity-oracle.md, "Where VB6's designer puts a control's block", and "How a
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
/// The two planes are two bands of ZIndex: windowed from 0 down, windowless from <see cref="WindowlessStart"/> down, each
/// with room above it for a control brought to the front. A control's band says which plane it is in.
/// </para>
/// </remarks>
internal static class ControlStacking
{
    // Every windowless control is at or below this, every windowed one above it.
    private const int Boundary = -1_000_000;

    // Where the windowless plane starts, a million below the boundary, so ZOrder 0 has room to raise one.
    private const int WindowlessStart = -2_000_000;

    /// <summary>Whether a control placed here is in the windowless plane.</summary>
    public static bool IsWindowless(Control control) => control.ZIndex <= Boundary;

    /// <summary>Gives <paramref name="control"/> a ZIndex under every control of its plane in <paramref name="container"/>.</summary>
    public static void PlaceUnder(Control control, bool windowless, Panel container)
    {
        var lowest = windowless ? WindowlessStart + 1 : 1;
        foreach (var child in container.Children)
            if (!ReferenceEquals(child, control) && IsWindowless(child) == windowless)
                lowest = Math.Min(lowest, child.ZIndex);
        control.ZIndex = lowest - 1;
        // A ZIndex changed on its own does not redraw the container: a control brought over another stayed under it in the
        // next frame until the container was invalidated.
        container.InvalidateVisual();
    }

    /// <summary>Gives <paramref name="control"/> a ZIndex over every control of its plane in <paramref name="container"/>.</summary>
    public static void PlaceOver(Control control, bool windowless, Panel container)
    {
        var highest = windowless ? WindowlessStart - 1 : -1;
        foreach (var child in container.Children)
            if (!ReferenceEquals(child, control) && IsWindowless(child) == windowless)
                highest = Math.Max(highest, child.ZIndex);
        control.ZIndex = highest + 1;
        container.InvalidateVisual();
    }
}
