using Avalonia.Controls;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>
/// A control that hosts other VB6 controls inside itself — Frame, and from the PictureBox phase, PictureBox.
///
/// The control-side half of containment. The component class is the authority on whether a CLASS is a
/// container (<c>ComponentBaseClass.TryGetChildHost</c>); this is how code holding only a <see cref="Control"/>
/// — the property interop, the visibility rule — can ask the same question without a type list to keep in
/// step.
/// </summary>
public interface IVBContainerControl
{
    /// <summary>The Canvas contained controls are placed on, in this control's own coordinate space.</summary>
    Canvas? ChildHost { get; }
}
