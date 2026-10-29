using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace HexIDE.Runtime.Interpreter;

// Synthetic identities only: #2622, measured native/p-code in PRs #2624 and #2628.
internal static class PointerIdentities
{
    private sealed record Identity(long Number);
    private static readonly ConditionalWeakTable<object, Identity> identities = new();
    private static long next;

    internal static long Allocate(long size = 1)
    {
        var end = Interlocked.Add(ref next, size);
        if (end > int.MaxValue)
            throw new NotImplementedException("Pointer identity space exhausted (#2622)");
        return end - size + 1;
    }

    internal static long Of(object? key) => key is null ? 0 : identities.GetValue(key, _ => new Identity(Allocate())).Number;

    internal static long Target(IByRefTarget target)
    {
        if (target is SlotRef slot)
            return slot.State.VariableIdentity(slot.Location);
        if (target is not ArrayElementRef element || element.Array.Rank != 1 || element.Array.IsParamArray)
            throw new NotImplementedException("VarPtr of this target is unmeasured (#2622)");
        var array = element.Array;
        var type = array.ElementType;
        var size = type == Vb6Value.ValueType.Long ? 4
            : type == Vb6Value.ValueType.Integer ? 2
            : type == Vb6Value.ValueType.Currency ? 8
            : type is null || type == Vb6Value.ValueType.EmptyVariant ? 16
            : throw new NotImplementedException("VarPtr array element width is unmeasured (#2622)");
        var first = identities.GetValue(array, _ => new Identity(Allocate(
            ((long)array.UpperBound(1) - array.LowerBound(1) + 1) * size))).Number;
        return first + ((long)element.Index[0] - array.LowerBound(1)) * size;
    }
}
