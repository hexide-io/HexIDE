using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Components;

/// <summary>
/// VB6's scale rules at design time (#5758): the units a control's Left, Top, Width and Height are shown in, and the units a
/// Line's endpoints are held in. The provider whose forms these are registers one, through
/// <c>IHexIdeHost.RegisterDesignPositions</c>. The Properties window, the toolbar's readout, the form designer and
/// automation ask it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Each call is contained, and each answer is checked before it's used.</b> A throw is logged, and so is an answer the IDE
/// can't use: a shown value or pixels that aren't finite, a size below zero, a unit that isn't one VB6's Properties window
/// shows, or a Line unit that isn't finite and above zero. That call then answers as though no positions were registered.
/// </para>
/// <para>
/// <b>What the IDE does without an answer.</b> A position is shown as the model holds it, in pixels, with no unit. A value
/// typed in is refused, and nothing changes. A Line is laid out and drawn as though its units were twips, but isn't moved,
/// since a move written in twips would be wrong in any other scale.
/// </para>
/// </remarks>
public interface IDesignPositions
{
    /// <summary>
    /// The value the Properties window shows for <paramref name="property"/>, one of <paramref name="component"/>'s Left,
    /// Top, Width and Height, from the model's <paramref name="pixels"/>, and the unit it's in.
    /// </summary>
    /// <returns>
    /// The value, and the unit as a reply names it: <c>twips</c>, <c>points</c>, <c>pixels</c>, <c>characters</c>,
    /// <c>inches</c>, <c>millimetres</c>, <c>centimetres</c> or <c>user units</c>.
    /// </returns>
    (double Value, string Unit) Shown(FormDefinition form, ComponentInstance component, PropertyClass property, double pixels);

    /// <summary>
    /// The model's pixels for <paramref name="value"/>, typed into the Properties window in the unit <see cref="Shown"/>
    /// gives, or null for a value no position can hold.
    /// </summary>
    double? ToPixels(FormDefinition form, ComponentInstance component, PropertyClass property, double value);

    /// <summary>
    /// How many twips one unit of <paramref name="line"/>'s container is, across and down: the scale its X1, Y1, X2 and Y2
    /// are held in. Null for a scale the provider can't run: the designer then lays the Line out, draws it and moves it as
    /// though its units were twips, as it always has (#5759).
    /// </summary>
    (double X, double Y)? LineTwipsPerUnit(FormDefinition form, ComponentInstance line);
}
