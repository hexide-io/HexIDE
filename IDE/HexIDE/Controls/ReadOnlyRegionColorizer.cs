using System;
using System.Collections.Generic;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Controls;

/// <summary>
/// Greys the code window's read-only regions: the file's header and each member's <c>Attribute</c> lines
/// (hexide-io/HexIDE#273 task 3.15).
/// </summary>
/// <remarks>
/// <para>
/// <b>After the syntax colouring, before the diagnostics colouring.</b> AvaloniaEdit always puts its
/// highlighting colorizer first in the line transformers, so anything after it overrides the syntax colours.
/// The view adds this one ahead of <c>LspDiagnosticsColorizer</c>, so an error's red text still shows inside a
/// region: a diagnostic is a server's claim about the file, and greying it away would hide the claim.
/// </para>
/// <para>
/// <b>Membership decides it, not the fold.</b> An expanded header is greyed, and so is a one-line header that is
/// never folded. A form held read-only as a whole is not thereby greyed: its code is in no region.
/// </para>
/// <para>
/// <b>No brush, nothing greyed.</b> The brush is the theme's <c>ReadOnlyTextBrush</c>, which the view resolves and
/// follows. Where that key does not resolve, nothing is greyed rather than a literal colour taken that no theme
/// could reach.
/// </para>
/// </remarks>
/// <param name="regions">The buffer's read-only regions as they stand, in document order.</param>
internal sealed class ReadOnlyRegionColorizer(Func<IReadOnlyList<TextRegion>> regions) : DocumentColorizingTransformer
{
    /// <summary>The colour read-only text is drawn in, or null to grey nothing.</summary>
    public IBrush? Brush { get; set; }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (Brush is not { } brush)
            return;

        // A region spans whole lines and ends past its last line's terminator, which is outside the line this
        // call may change, so each is clamped to the line.
        var start = line.Offset;
        var end = line.EndOffset;
        foreach (var region in regions())
        {
            if (region.Start >= end)
                break;
            if (region.End <= start)
                continue;
            ChangeLinePart(Math.Max(start, region.Start), Math.Min(end, region.End),
                element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}
