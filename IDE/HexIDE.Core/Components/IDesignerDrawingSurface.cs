namespace HexIDE.Runtime.Components;

/// <summary>
/// The place the form designer lends a control while it's drawn (#5591). Its place in the designer's tree is what gives the
/// control the canvas's styles, resources and Light theme, so its picture matches the designer's.
/// </summary>
/// <remarks>
/// <para>
/// <b>In the IDE's process for now.</b> The visual is the drawer's own control, in the IDE's UI toolkit, which is why this is
/// typed as an object: HexIDE.Core has no Avalonia reference. It's an interim step: drawing out of process (#3505) would give
/// the drawer no place in the IDE's tree at all.
/// </para>
/// <para>
/// Only an owner HexIDE grants it gets one, through <see cref="HexIDE.Addins.IHexIdeHost.RegisterDesignerPictures"/>.
/// </para>
/// </remarks>
public interface IDesignerDrawingSurface
{
    /// <summary>Puts <paramref name="visual"/> in the designer's tree until <see cref="Detach"/>.</summary>
    /// <exception cref="ArgumentException">
    /// It isn't a control of the IDE's UI toolkit, or it already has a parent, or the surface holds another visual.
    /// </exception>
    void Attach(object visual);

    /// <summary>Takes <paramref name="visual"/> out again. A visual this surface didn't attach is left where it is.</summary>
    void Detach(object visual);

    /// <summary>Lays out the surface, and so the visual it holds.</summary>
    void UpdateLayout();
}
