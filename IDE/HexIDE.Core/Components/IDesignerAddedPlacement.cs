namespace HexIDE.Runtime.Components;

/// <summary>
/// Where the form designer puts a control of this class that's added by double-clicking its tool, for a class whose
/// control VB6's designer doesn't centre at its added size and put on the grid as it does every other (#5681). Optional.
/// </summary>
/// <remarks>
/// A throw, or bounds that aren't usable, is logged, and the control is placed as though the class didn't have the
/// contract. Usable bounds are finite, have no negative width or height, and are each within what a whole number of twips
/// can hold, as <see cref="IDesignerComponent.AddedSize"/> is.
/// </remarks>
public interface IDesignerAddedPlacement
{
    /// <summary>
    /// The bounds, in the designer's pixels, that a control of this class takes when its tool is double-clicked on a form
    /// <paramref name="formWidth"/> pixels wide and <paramref name="formHeight"/> pixels high, or null to centre it at its
    /// added size.
    /// </summary>
    DesignerBounds? AddedBounds(double formWidth, double formHeight) => null;

    /// <summary>Whether Align to Grid moves a control of this class that's added by double-clicking its tool.</summary>
    bool AddedOnGrid => true;
}

/// <summary>A control's bounds in the form designer, in pixels.</summary>
/// <param name="Left">Its left edge.</param>
/// <param name="Top">Its top edge.</param>
/// <param name="Width">Its width.</param>
/// <param name="Height">Its height.</param>
public readonly record struct DesignerBounds(double Left, double Top, double Width, double Height);
