namespace HexIDE.Runtime.BuiltinTypes;

/// <summary>
/// VB6's FillStyle constants, in VB6's order.
///
/// Solid is 0 and Transparent is 1 — these two were the other way round here until 2026-08-19, while
/// every member from HorizontalLine on was already right. Microsoft's own
/// <c>Template\Userctls\Colorful Control.ctl</c> settles it: it writes <c>FillStyle = 0  'Solid</c>, and
/// that comment is VB6's own label for the value. A VB6 Shape saved as Solid therefore rendered
/// transparent in HexIDE, and one saved as Transparent rendered solid.
/// </summary>
public enum FillStyles
{
    [Vb6Name("Solid")]             Solid = 0,
    [Vb6Name("Transparent")]       Transparent = 1,
    [Vb6Name("Horizontal Line")]   HorizontalLine = 2,
    [Vb6Name("Vertical Line")]     VerticalLine = 3,
    [Vb6Name("Upward Diagonal")]   UpwardDiagonal = 4,
    [Vb6Name("Downward Diagonal")] DownwardDiagonal = 5,
    [Vb6Name("Cross")]             Cross = 6,
    [Vb6Name("Diagonal Cross")]    DiagonalCross = 7
}
