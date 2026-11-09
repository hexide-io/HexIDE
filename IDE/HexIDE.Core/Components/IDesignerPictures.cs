namespace HexIDE.Runtime.Components;

/// <summary>
/// Draws controls for the form designer, and hands back pictures, so that what draws them need not share the IDE's
/// process or its version of the UI toolkit (#3505).
/// </summary>
public interface IDesignerPictures
{
    /// <summary>Draws <paramref name="instance"/> at the requested size.</summary>
    /// <remarks>A control that refuses to draw throws, and the designer shows the message in the control's place.</remarks>
    DesignerPicture Draw(ComponentInstance instance, DesignerDrawRequest request);
}

/// <summary>What the designer wants drawn.</summary>
/// <param name="PixelWidth">The picture's width in pixels.</param>
/// <param name="PixelHeight">The picture's height in pixels.</param>
/// <param name="LineTwipsPerUnit">How many twips one of the control's container's units is, which only a Line draws from.</param>
public readonly record struct DesignerDrawRequest(int PixelWidth, int PixelHeight, (double X, double Y) LineTwipsPerUnit);

/// <summary>A control as the designer shows it.</summary>
/// <param name="PixelWidth">The picture's width in pixels.</param>
/// <param name="PixelHeight">The picture's height in pixels.</param>
/// <param name="Pixels">32-bit BGRA, premultiplied, the top row first, with no gap between rows.</param>
/// <param name="MinWidth">The smallest width the control can take.</param>
/// <param name="MinHeight">The smallest height the control can take.</param>
/// <param name="MaxWidth">The largest width the control can take.</param>
/// <param name="MaxHeight">The largest height the control can take.</param>
public sealed record DesignerPicture(int PixelWidth, int PixelHeight, byte[] Pixels,
    double MinWidth, double MinHeight, double MaxWidth, double MaxHeight);
