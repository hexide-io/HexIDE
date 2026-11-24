using Avalonia.Controls;

namespace HexIDE.Runtime.Components;

/// <summary>
/// What the form designer asks of a control class beyond its description: the designer reaches a class through this,
/// and not through <see cref="ComponentBaseClass"/>, so that the classes can one day come from elsewhere (#3482).
/// </summary>
/// <remarks>
/// Here rather than beside <see cref="IDesignerComponent"/> because
/// <see cref="InstantiateForDesigner"/> names an Avalonia type, and HexIDE.Core has no Avalonia reference.
/// </remarks>
public interface IDesignerComponentClass : IDesignerComponent
{
    /// <summary>The control the designer draws for <paramref name="instance"/>.</summary>
    Control InstantiateForDesigner(ComponentInstance instance);
}
