namespace HexIDE.Runtime.BuiltinTypes;

/// <summary>
/// Framework-agnostic mouse pointer / cursor types matching VB6's MousePointer property values.
///
/// The names are VB6's own, as it writes them in the comment beside the value: the VB6 IDE wrote all seventeen on
/// saving a form that gave a Label each one (the oracle's "MousePointer's names and pointers", #120). 15 is the last of
/// the standard set before the jump to 99. Assignment checks the measured set, not this enum (#768).
/// </summary>
public enum VBCursorType
{
    [Vb6Name("Default")] Default = 0,
    [Vb6Name("Arrow")] Arrow = 1,
    [Vb6Name("Cross")] Crosshair = 2,
    [Vb6Name("I-Beam")] IBeam = 3,
    [Vb6Name("Icon")] Icon = 4,
    [Vb6Name("Size")] Size = 5,
    [Vb6Name("Size NE SW")] SizeNESW = 6,
    [Vb6Name("Size N S")] SizeNS = 7,
    [Vb6Name("Size NW SE")] SizeNWSE = 8,
    [Vb6Name("Size W E")] SizeWE = 9,
    [Vb6Name("Up Arrow")] UpArrow = 10,
    [Vb6Name("Hourglass")] Hourglass = 11,
    [Vb6Name("No Drop")] NoDrop = 12,
    [Vb6Name("Arrow and Hourglass")] ArrowHourglass = 13,
    [Vb6Name("Arrow and Question")] ArrowQuestion = 14,
    [Vb6Name("Size All")] SizeAll = 15,
    [Vb6Name("Custom")] Custom = 99,
}
