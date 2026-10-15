using System.Collections.Generic;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>
/// A Line's members as a program reads and writes them (#163). Measured on vb6.exe, native and p-code agreeing (the
/// oracle's "The Line control"):
/// <list type="bullet">
/// <item>X1, Y1, X2 and Y2 read as Singles in the container's scale, held as whole twips: on a pixel form, 10.5 reads
/// 10.53333.</item>
/// <item>BorderWidth, BorderStyle and DrawMode read as Integers. BorderWidth 0 is 380, 2.5 is 2, and 8193 is taken.
/// BorderStyle outside 0 to 6 is 380, and so is DrawMode outside 1 to 16.</item>
/// <item>BorderColor reads as a Long, and refuses what any colour refuses: -1 is 380.</item>
/// <item>Left, Top, Width and Height are not members: a late-bound read is 438. Visible and Tag are, as on any
/// control.</item>
/// </list>
/// The first control whose members are declared beside it rather than in the shared bindings, which is the per-control
/// member table #1082 generalises.
/// </summary>
internal static class LineMembers
{
    private static readonly Dictionary<PropertyClass, int> Coordinates = new()
    {
        [VBProperties.X1Property] = VBLine.X1,
        [VBProperties.Y1Property] = VBLine.Y1,
        [VBProperties.X2Property] = VBLine.X2,
        [VBProperties.Y2Property] = VBLine.Y2,
    };

    /// <summary>
    /// A Line's own member, read: true with its value, false for a member a Line does not have, null for one every
    /// control has, which the shared bindings read.
    /// </summary>
    public static bool? TryGet(VBLine line, PropertyClass property, out Vb6Value value)
    {
        value = default;
        if (ControlPositions.IsPosition(property))
            return false;
        if (Coordinates.TryGetValue(property, out var coordinate))
            value = new Vb6Value(line.Read(coordinate));
        else if (ReferenceEquals(property, VBProperties.BorderWidthProperty))
            value = new Vb6Value(line.BorderWidth);
        else if (ReferenceEquals(property, VBProperties.ShapeBorderStyleProperty))
            value = new Vb6Value((int)line.BorderStyle);
        else if (ReferenceEquals(property, VBProperties.DrawModeProperty))
            value = new Vb6Value(line.DrawMode);
        else if (ReferenceEquals(property, VBProperties.BorderColorProperty))
            value = new Vb6Value((long)line.BorderColor.ToOle());
        else if (IsElsewhereOnly(property))
            return false;
        else
            return null;
        return true;
    }

    /// <summary>A Line's own member, written: as <see cref="TryGet"/>, and raising VB6's error for a value it refuses.</summary>
    public static bool? TrySet(VBLine line, PropertyClass property, Vb6Value value)
    {
        if (ControlPositions.IsPosition(property))
            return false;
        if (Coordinates.TryGetValue(property, out var coordinate))
            line.Write(coordinate, (double)AvaloniaInteroperability.ConvertForAssignment(property, value)!);
        else if (ReferenceEquals(property, VBProperties.BorderWidthProperty))
            line.BorderWidth = Integer(value, 1, short.MaxValue);
        else if (ReferenceEquals(property, VBProperties.ShapeBorderStyleProperty))
            line.BorderStyle = (BorderStyles)AvaloniaInteroperability.ConvertForAssignment(property, value)!;
        else if (ReferenceEquals(property, VBProperties.DrawModeProperty))
            line.DrawMode = Integer(value, 1, 16);
        else if (ReferenceEquals(property, VBProperties.BorderColorProperty))
            line.BorderColor = (VBColor)AvaloniaInteroperability.ConvertForAssignment(property, value)!;
        else if (IsElsewhereOnly(property))
            return false;
        else
            return null;
        return true;
    }

    // Converted as an Integer, so past 32767 is Overflow (6), then 380 outside the range.
    private static int Integer(Vb6Value value, int least, int most)
    {
        var number = System.Convert.ToInt32(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Integer, null).Value);
        return number >= least && number <= most ? number : throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
    }

    // What every other control's bindings answer for, but a Line has not: Enabled, the colours a Line lacks, and the rest
    // a Control-wide binding would otherwise give it.
    private static bool IsElsewhereOnly(PropertyClass property) =>
        !ReferenceEquals(property, VBProperties.VisibleProperty) && !ReferenceEquals(property, VBProperties.TagProperty);
}
