using System;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>
/// VB6's scale rules at design time, as the provider gives them to the IDE (#5758): <see cref="DesignPositions"/> for a
/// control's Left, Top, Width and Height, and <see cref="LineComponentClass.TwipsPerUnit"/> for a Line's endpoints.
/// </summary>
internal sealed class ProviderDesignPositions : IDesignPositions
{
    public (double Value, string Unit) Shown(FormDefinition form, ComponentInstance component, PropertyClass property,
        double pixels) =>
        (DesignPositions.Shown(form, component, property, pixels), DesignPositions.Unit(form, component, property));

    // A value whose twips aren't finite is refused there by a throw; here it's an answer, so the IDE doesn't log it.
    public double? ToPixels(FormDefinition form, ComponentInstance component, PropertyClass property, double value)
    {
        try
        {
            return DesignPositions.ToPixels(form, component, property, value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    // A scale nobody has measured is refused there by a throw; here it's an answer, so the IDE doesn't log it.
    public (double X, double Y)? LineTwipsPerUnit(FormDefinition form, ComponentInstance line)
    {
        try
        {
            return LineComponentClass.TwipsPerUnit(line, form);
        }
        catch (NotImplementedException)
        {
            return null;
        }
    }
}
