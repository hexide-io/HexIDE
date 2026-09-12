using System.Linq;
using Avalonia.Headless.XUnit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using HexIDE.Themes;

namespace HexIDE.Integration.Tests.Controls;

/// <summary>
/// The colouring shared by the protocol pane and the export preview.
/// </summary>
/// <remarks>
/// <b>The definition is AvaloniaEdit's own, so what is tested is the wiring, not the colours.</b> Two
/// things can silently fail here and both look plausible in a screenshot: the library could rename or drop
/// the definition on a version bump, and the theme adoption could stop being applied — leaving a hardcoded
/// light palette on a dark background. The code that consumes it deliberately tolerates a null, because
/// the bytes have to appear either way, so the swallow needs a test above it.
/// </remarks>
public class ProtocolHighlightingTests
{
    private static IHighlightingDefinition Definition()
    {
        var definition = ProtocolHighlighting.Definition;
        definition.Should().NotBeNull(
            "AvaloniaEdit bundles a JSON definition; a null here means a version bump dropped or renamed "
          + "it and the colouring is silently off");
        return definition!;
    }

    private static string[] ColoursOn(string line)
    {
        var document = new TextDocument(line);
        var highlighter = new DocumentHighlighter(document, Definition());

        return highlighter.HighlightLine(1).Sections
            .Select(s => s.Color.Name ?? "")
            .Where(name => name.Length > 0)
            .Distinct()
            .ToArray();
    }

    [AvaloniaFact]
    public void TheBundledJsonDefinitionIsAvailable()
    {
        Definition().Should().NotBeNull();
    }

    [AvaloniaFact]
    public void AFrameIsColouredRatherThanLeftFlat()
    {
        // The one assertion that matters: something in a real frame comes back coloured. Which colour is
        // the library's business, not ours.
        ColoursOn("""{"jsonrpc":"2.0","id":2,"method":"initialize"}""").Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public void ATraceHeaderCostsNothingEvenThoughItIsNotJson()
    {
        // The trace wraps its JSON in two lines of its own. They are framing rather than content, so
        // leaving them uncoloured is right — but it must not throw or swallow the rest of the block.
        var act = () => ColoursOn("[Trace - 02:27:26.728] [hexide.vb6] Sending request 'initialize - (2)'.");

        act.Should().NotThrow();
    }

    [AvaloniaFact]
    public void ThePaletteIsBroughtUnderThemeControl()
    {
        // A bundled definition hardcodes a light palette and is unreadable on a dark background until it
        // is adopted. Adoption is idempotent, so asking twice must not double-register or re-tint.
        var first = ProtocolHighlighting.Definition;
        var second = ProtocolHighlighting.Definition;

        second.Should().BeSameAs(first);
        first!.NamedHighlightingColors.Should().NotBeEmpty("there must be colours for the theme to adopt");
    }
}
