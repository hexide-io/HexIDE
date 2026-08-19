namespace HexIDE.Runtime.BuiltinTypes;

/// <summary>
/// VB6's <c>BorderStyle</c> for a <c>Shape</c> or a <c>Line</c> — the seven-value set, which is the same
/// set as <c>DrawStyle</c>. Not to be confused with the two other properties VB6 also calls BorderStyle:
/// a control's (<see cref="VBBorder"/>, None/Fixed Single) or a form's (None through Sizable ToolWindow).
///
/// Only <c>Transparent</c> and <c>Solid</c> existed here until 2026-08-19. The other five were not
/// merely unnamed — they were absent, so a file carrying one produced an enum value outside the type,
/// every equality test against it was false, and <see cref="VBShape"/> drew <b>no border at all</b>.
/// Microsoft's own <c>Template\Forms\About Dialog.frm</c> is the live case: its shape declares
/// <c>BorderStyle = 6  'Inside Solid</c>, which VB6 draws and HexIDE did not.
///
/// The names are VB6's own, taken from the comment it writes beside the value.
/// </summary>
public enum BorderStyles
{
    [Vb6Name("Transparent")]   Transparent = 0,
    [Vb6Name("Solid")]         Solid = 1,
    [Vb6Name("Dash")]          Dash = 2,
    [Vb6Name("Dot")]           Dot = 3,
    [Vb6Name("Dash-Dot")]      DashDot = 4,
    [Vb6Name("Dash-Dot-Dot")]  DashDotDot = 5,
    [Vb6Name("Inside Solid")]  InsideSolid = 6
}
