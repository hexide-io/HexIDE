namespace HexIDE.Runtime.BuiltinTypes;

/// <summary>
/// Framework-agnostic mouse pointer / cursor types matching VB6's MousePointer property values.
///
/// The names are VB6's own, as it writes them in the comment beside the value — <c>99  'Custom</c> is the
/// corpus case, on the splitter bar in <c>Treeview Listview Splitter.frm</c>.
///
/// Deliberately not contiguous: 4, 5, 13 and 14 are gaps in VB6's own numbering, and 15 is the last of the
/// standard set before the jump to 99. Filling them in would invent values VB6 does not have.
/// </summary>
public enum VBCursorType
{
    [Vb6Name("Default")] Default = 0,
    [Vb6Name("Arrow")] Arrow = 1,
    [Vb6Name("Cross")] Crosshair = 2,
    [Vb6Name("I-Beam")] IBeam = 3,
    [Vb6Name("Size NE SW")] SizeNESW = 6,
    [Vb6Name("Size N S")] SizeNS = 7,
    [Vb6Name("Size NW SE")] SizeNWSE = 8,
    [Vb6Name("Size W E")] SizeWE = 9,
    [Vb6Name("Up Arrow")] UpArrow = 10,
    [Vb6Name("Hourglass")] Hourglass = 11,
    [Vb6Name("No Drop")] NoDrop = 12,
    [Vb6Name("Size All")] SizeAll = 15,
    [Vb6Name("Custom")] Custom = 99,
}
