using AvaloniaEdit.Highlighting;

namespace HexIDE.Themes;

/// <summary>
/// The colouring shared by the protocol pane and the export preview.
/// </summary>
/// <remarks>
/// <b>AvaloniaEdit's own JSON definition, not one written here.</b> It bundles one, and a hand-maintained
/// copy would be a permanent tax paid for nothing — this content is JSON and the library already knows how
/// to colour JSON. The trace shape wraps it in two lines of its own, a header and a payload label; those
/// simply come out uncoloured, which is right, because they are the trace's framing rather than the thing
/// being read.
///
/// <para>
/// Adopted so the palette follows the IDE's theme. Every bundled definition hardcodes a light palette and
/// is unreadable on a dark background until it is, which is the same defect the VB6 definition had before
/// it was given one.
/// </para>
/// </remarks>
public static class ProtocolHighlighting
{
    /// <summary>
    /// The definition, or null when the library does not have one.
    /// </summary>
    /// <remarks>
    /// Null rather than a throw. Colouring is a nicety and this pane's job is to show what crossed the
    /// wire, so a missing definition must cost the colours and nothing else — the same rule a carried
    /// document's editor already follows.
    /// </remarks>
    public static IHighlightingDefinition? Definition
    {
        get
        {
            var definition = HighlightingManager.Instance.GetDefinition("Json");
            SyntaxHighlightingTheme.Adopt(definition);
            return definition;
        }
    }
}
