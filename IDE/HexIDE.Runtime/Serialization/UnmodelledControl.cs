using System;
using System.Globalization;

namespace HexIDE.Runtime.Serialization;

/// <summary>
/// What the designer can show of a control HexIDE does not model (#157): its class, its name, and the position and
/// size VB6 gives the box it draws in the control's place, in twips as the file records them.
/// </summary>
/// <remarks>
/// <para>
/// Read from the block the control was preserved as, and only from the container syntax every block shares: the
/// <c>Begin</c> line, and the block's own <c>Name = Value</c> lines. Nothing about the control's own format is needed,
/// and nothing here is written back; the block is carried as it was read.
/// </para>
/// <para>
/// <b>The size is VB6's, measured in its IDE</b> (docs/vb6-fidelity-oracle/a-control-vb6-cannot-load-renders-as-an-empty-box.md, "Its size when the file records none,
/// and what the box is"). <c>Width</c> and <c>Height</c> size the box when the file has them, and it is 1200 × 480
/// twips when it has neither. <c>_ExtentX</c> and <c>_ExtentY</c> play no part, though they are the obvious guess: an
/// ImageList records only those, and VB6 draws it 1200 × 480. A file recording one of the two only was not measured,
/// and takes the default for the other. A missing <c>Left</c> or <c>Top</c> is 0, as for any control.
/// </para>
/// </remarks>
public sealed record UnmodelledControl(string TypeName, string Name, double Left, double Top, double Width, double Height)
{
    /// <summary>VB6's width for the box of a control whose file records none, in twips (measured).</summary>
    public const double DefaultWidth = 1200;

    /// <summary>VB6's height for the box of a control whose file records none, in twips (measured).</summary>
    public const double DefaultHeight = 480;

    /// <summary>
    /// Whether HexIDE runs the control though the designer only carries its block (#2327), as
    /// <see cref="FormDeserializer.RunsWhenCarried"/> decides: the box then says it is view-only in the designer, not
    /// that it is unsupported.
    /// </summary>
    public bool ViewOnly { get; init; }

    /// <summary>The control <paramref name="block"/> holds, or null when it does not open with a <c>Begin</c> line.</summary>
    public static UnmodelledControl? Read(string block)
    {
        var lines = block.Split(["\r\n", "\n"], StringSplitOptions.None);
        var first = Array.FindIndex(lines, line => line.Trim().Length > 0);
        if (first < 0)
            return null;

        var opening = lines[first].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (opening.Length < 3 || !opening[0].Equals("Begin", StringComparison.OrdinalIgnoreCase))
            return null;

        double left = 0, top = 0;
        double? width = null, height = null;
        // Whether it holds controls of its own, or is a control array's element: a Toolbar that does either still doesn't run.
        bool holdsControls = false, inArray = false;
        // How many blocks deep inside this one a line sits: a BeginProperty block, or a nested control's Begin block.
        var depth = 0;
        for (var i = first + 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("BeginProperty", StringComparison.OrdinalIgnoreCase))
                depth++;
            else if (line.Equals("EndProperty", StringComparison.OrdinalIgnoreCase))
                depth--;
            else if (line.StartsWith("Begin ", StringComparison.OrdinalIgnoreCase))
            {
                holdsControls = true;
                depth++;
            }
            else if (line.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                if (depth == 0)
                    break;
                depth--;
            }
            else if (depth == 0 && Number(line) is var (name, value))
            {
                if (name.Equals("Left", StringComparison.OrdinalIgnoreCase)) left = value;
                else if (name.Equals("Top", StringComparison.OrdinalIgnoreCase)) top = value;
                else if (name.Equals("Width", StringComparison.OrdinalIgnoreCase)) width = value;
                else if (name.Equals("Height", StringComparison.OrdinalIgnoreCase)) height = value;
                else if (name.Equals("Index", StringComparison.OrdinalIgnoreCase)) inArray = true;
            }
        }

        return new UnmodelledControl(opening[1], opening[2], left, top, width ?? DefaultWidth, height ?? DefaultHeight)
        {
            ViewOnly = FormDeserializer.RunsWhenCarried(opening[1], holdsControls, inArray),
        };
    }

    // A "Name = number" line's name and value, any trailing comment aside; null for any other line, a string's included.
    private static (string Name, double Value)? Number(string line)
    {
        var equals = line.IndexOf('=');
        if (equals <= 0)
            return null;
        var text = line[(equals + 1)..];
        var comment = text.IndexOf('\'');
        if (comment >= 0)
            text = text[..comment];
        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? (line[..equals].Trim(), value)
            : null;
    }
}
