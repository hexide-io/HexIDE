using System.Collections.Generic;

namespace HexIDE.Runtime.Components;

/// <summary>
/// Metadata-only view of a VB6 component class (Form, TextBox, CommandButton, etc.).
/// Framework-specific subclasses (e.g. ComponentBaseClass) add control instantiation.
/// </summary>
public interface IComponentClass
{
    string Name { get; }
    string VBTypeName { get; }

    /// <summary>
    /// True for controls with a visual footprint on the form. False for invisible/runtime-only controls
    /// (e.g. Timer), which in VB6 have no Width/Height/Visible — emitting those into the .frm makes
    /// vb6.exe reject the form ("property name Width ... is invalid").
    /// </summary>
    bool IsVisual { get; }

    IReadOnlyList<PropertyClass> Properties { get; }
    IReadOnlyDictionary<string, PropertyClass> PropertiesByName { get; }
    IReadOnlyList<EventClass> Events { get; }
}
