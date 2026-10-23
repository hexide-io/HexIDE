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
/// </list>
/// </para>
/// <para>
/// So one rule places them all: a control goes under every control of its plane already in its container. Placing a
/// form's controls in file order then stacks the first on top, and a loaded element at the bottom.
/// </para>
/// </remarks>
internal static class ControlStacking
{
    // Far enough below 0 that no container holds enough windowed controls to reach it.
    private const int WindowlessPlane = -1_000_000;

    /// <summary>Gives <paramref name="control"/> a ZIndex under every control of its plane in <paramref name="container"/>.</summary>
    public static void PlaceUnder(Control control, bool windowless, Panel container)
    {
        var lowest = windowless ? WindowlessPlane + 1 : 1;
        foreach (var child in container.Children)
            if (!ReferenceEquals(child, control) && (child.ZIndex <= WindowlessPlane) == windowless)
                lowest = Math.Min(lowest, child.ZIndex);
        control.ZIndex = lowest - 1;
    }
}
