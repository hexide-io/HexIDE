namespace HexIDE.Runtime.BuiltinTypes;

/// <summary>
/// Framework-agnostic mouse pointer / cursor types matching VB6's MousePointer property values.
/// </summary>
public enum VBCursorType
{
    Default = 0,
    Arrow = 1,
    Crosshair = 2,
    IBeam = 3,
    SizeNESW = 6,
    SizeNS = 7,
    SizeNWSE = 8,
    SizeWE = 9,
    UpArrow = 10,
    Hourglass = 11,
    NoDrop = 12,
    SizeAll = 15,
    Custom = 99,
}
