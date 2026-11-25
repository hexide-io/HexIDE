namespace HexIDE.Runtime.Components;

/// <summary>
/// What the form designer draws in a window's title bar, for a component class whose instances are a designer's root
/// window (#5633). Optional: the designer shows the generic form glyph for a root whose class doesn't say.
/// </summary>
/// <remarks>
/// Asked only of the class of a designer's root, never of a control placed on it, and only of a class registered through
/// the host, which grants a root window's role to its owner alone. A throw or a null answers as no caption does.
/// </remarks>
public interface IDesignerCaption
{
    /// <summary>The title bar's icon and whether it shows, for <paramref name="instance"/> as its properties now stand.</summary>
    DesignerCaption CaptionOf(ComponentInstance instance);
}

/// <summary>A designer window's title-bar icon.</summary>
/// <param name="Image">
/// The icon's bytes, as a form's picture holds them, or null for none. The designer decodes them as it decodes a form's
/// own pictures.
/// </param>
/// <param name="UseFormGlyph">Whether to show the generic window glyph in the icon's place.</param>
/// <param name="Visible">Whether the title bar shows an icon at all.</param>
public sealed record DesignerCaption(byte[]? Image, bool UseFormGlyph, bool Visible);
