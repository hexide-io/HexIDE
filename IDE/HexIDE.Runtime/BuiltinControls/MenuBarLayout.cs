using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.BuiltinControls;

// Console measurements: #4978, oracle PRs #4977 and #4985.
internal static class MenuBarLayout
{
    internal static Func<string, double>? MeasureCaption { get; set; }

    internal static int[] Rows(IEnumerable<double> widths, double clientWidth)
    {
        var rows = new List<int>();
        double used = 0;
        var count = 0;
        foreach (var width in widths)
        {
            if (count > 0 && used + width > clientWidth - 7)
            {
                rows.Add(count);
                used = 0;
                count = 0;
            }
            used += width;
            count++;
        }
        if (count > 0) rows.Add(count);
        return rows.ToArray();
    }

    internal static double Height(int rows) => rows == 0 ? 0 : 19 * rows + 1;
}
