using System.Collections.Generic;

namespace HexIDE.Runtime.Serialization;

internal static class SavedTabIndexes
{
    // #4843, oracle PR #4873: reserve first in-range occurrences before filling the holes.
    internal static int[] Normalize(IReadOnlyList<int> saved)
    {
        var used = new bool[saved.Count];
        var result = new int[saved.Count];
        for (var i = 0; i < saved.Count; i++)
        {
            var value = saved[i];
            if (value >= 0 && value < saved.Count && !used[value])
            {
                used[value] = true;
                result[i] = value;
            }
            else
                result[i] = -1;
        }
        var free = 0;
        for (var i = 0; i < result.Length; i++)
        {
            if (result[i] >= 0)
                continue;
            while (used[free])
                free++;
            result[i] = free;
            used[free] = true;
        }
        return result;
    }
}
