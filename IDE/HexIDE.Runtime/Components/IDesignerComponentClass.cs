using Avalonia;
using Avalonia.Controls;

namespace HexIDE.Runtime.Components;

/// <summary>
/// What the form designer asks of a control class beyond its description: the designer reaches a class through this,
/// and not through <see cref="ComponentBaseClass"/>, so that the classes can one day come from elsewhere (#3482).
/// </summary>
/// <remarks>
/// Here rather than beside <see cref="IComponentClass"/> because <see cref="ClientInset"/> and
/// <see cref="InstantiateForDesigner"/> name Avalonia types, and HexIDE.Core has no Avalonia reference.
/// </remarks>
public interface IDesignerComponentClass : IComponentClass
{
    /// <summary>Whether VB6 draws this control on its container's own surface, under every windowed control.</summary>
    bool IsWindowless { get; }

    /// <summary>The size, in twips, a control of this class takes when added by double-clicking its tool.</summary>
    (int Width, int Height) AddedSize { get; }

    /// <summary>How far inside this control's bounds its contained controls are measured from.</summary>
    Thickness ClientInset(ComponentInstance instance);

    /// <summary>The control the designer draws for <paramref name="instance"/>.</summary>
    Control InstantiateForDesigner(ComponentInstance instance);
}
