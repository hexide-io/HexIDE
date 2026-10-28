using System.Collections.Generic;

namespace HexIDE.Runtime.Components;

/// <summary>A property the designer changed, with the values before and after, so the change can be undone.</summary>
public readonly record struct PropertyChange(PropertyClass Property, object? Before, object? After);

/// <summary>
/// Metadata-only view of a VB6 component class (Form, TextBox, CommandButton, etc.).
/// Framework-specific subclasses (e.g. ComponentBaseClass) add control instantiation.
/// </summary>
public interface IComponentClass
{
    string Name { get; }
    string VBTypeName { get; }
    string ClassName { get; }

    /// <summary>
    /// True for controls with a visual footprint on the form. False for invisible/runtime-only controls
    /// (e.g. Timer), which in VB6 have no Width/Height/Visible — emitting those into the .frm makes
    /// vb6.exe reject the form ("property name Width ... is invalid").
    /// </summary>
    bool IsVisual { get; }

    /// <summary>False while this component has no faithful file writer.</summary>
    bool CanSaveFaithfully => true;

    IReadOnlyList<PropertyClass> Properties { get; }
    IReadOnlyDictionary<string, PropertyClass> PropertiesByName { get; }
    IReadOnlyList<EventClass> Events { get; }

    /// <summary>
    /// What VB6's designer changes along with <paramref name="property"/>, which has just been set on
    /// <paramref name="instance"/>: applied here, and returned so the designer can undo them with the change that caused
    /// them. Every route that sets a property at design time asks this, so the Properties window and automation agree.
    /// </summary>
    IReadOnlyList<PropertyChange> ApplyDesignerConsequences(ComponentInstance instance, PropertyClass property);
}
