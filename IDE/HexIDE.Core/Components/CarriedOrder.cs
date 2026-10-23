using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Components;

/// <summary>
/// Where a container's carried blocks go among its modelled children: the controls HexIDE does not model, kept as the
/// text they were read as (<see cref="ComponentInstance.PreservedSubtree"/>). The writer and the designer both place them
/// through here, so a form loaded and saved untouched puts each block back exactly where it was read.
/// </summary>
/// <remarks>
/// A carried control is a control to VB6, so it stacks with the rest: a control added in front of one is written before
/// it, and one sent to the back after it (measured, #1498). The designer keeps that as a count on each modelled child,
/// how many of its container's carried blocks are drawn under it, and turns the counts back into positions here.
/// </remarks>
public static class CarriedOrder
{
    /// <summary>
    /// For each carried block, how many of the container's modelled children are written before it: at its
    /// <see cref="ComponentInstance.PreservedSubtree.Ordinal"/>, its place among all the children, or as soon after as the
    /// children allow. A block whose place is past the end goes at the end, so nothing is dropped.
    /// </summary>
    public static int[] ChildrenBefore(int childCount, IReadOnlyList<ComponentInstance.PreservedSubtree> blocks)
    {
        var before = new int[blocks.Count];
        var child = 0;
        var block = 0;
        for (var position = 0; position < childCount + blocks.Count; position++)
        {
            if (block < blocks.Count && blocks[block].Ordinal <= position)
                before[block++] = child;
            else if (child < childCount)
                child++;
        }
        for (; block < blocks.Count; block++)
            before[block] = childCount;
        return before;
    }

    /// <summary>For each modelled child, in the order written, how many of the carried blocks are written after it.</summary>
    public static int[] CarriedBelow(int childCount, IReadOnlyList<ComponentInstance.PreservedSubtree> blocks)
    {
        var before = ChildrenBefore(childCount, blocks);
        var below = new int[childCount];
        for (var child = 0; child < childCount; child++)
            foreach (var count in before)
                if (count > child)
                    below[child]++;
        return below;
    }

    /// <summary>
    /// The ordinals that put <paramref name="blockCount"/> carried blocks among the modelled children, given how many of
    /// them each child, in the order written, has under it. The blocks keep their own order. A count that would put a child
    /// above a block an earlier child is under is taken as no higher than that, so the result is always a placement.
    /// </summary>
    public static int[] Ordinals(IReadOnlyList<int> carriedBelow, int blockCount)
    {
        var ordinals = new int[blockCount];
        var block = 0;
        for (var child = 0; child < carriedBelow.Count; child++)
        {
            var above = blockCount - Math.Clamp(carriedBelow[child], 0, blockCount);
            for (; block < above; block++)
                ordinals[block] = block + child;
        }
        for (; block < blockCount; block++)
            ordinals[block] = block + carriedBelow.Count;
        return ordinals;
    }
}
