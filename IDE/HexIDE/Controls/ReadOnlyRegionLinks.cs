using System;
using System.Collections.Generic;
using AvaloniaEdit.Rendering;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Controls;

/// <summary>
/// Draws a web or mail address inside a read-only region as plain text, so it is greyed with the rest of the region
/// (hexide-io/HexIDE#273 task 3.15).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a generator and not the colorizer.</b> AvaloniaEdit gives a link the text view's link brush while it formats
/// the line, which is after every line transformer has run, so <see cref="ReadOnlyRegionColorizer"/>'s grey never
/// reaches it. Left alone, a URL in a form's <c>Caption</c> or a member's <c>VB_Description</c> is drawn blue and
/// underlined in the middle of greyed text, in a colour no theme sets and that misses the contrast bar on both dark
/// packs. This generator runs ahead of the link generators and claims, as plain text, what they would have made a
/// link inside a region.
/// </para>
/// <para>
/// <b>It asks the link generators rather than repeating their patterns</b>, so what it claims is exactly what would
/// have been a link, and nothing outside a region is touched: there, a link is still a link.
/// </para>
/// <para>
/// <b>The address is no longer clickable inside a region.</b> The region is metadata drawn as not the developer's to
/// edit, and VB6 showed none of it; an address there is text, as it was in VB6.
/// </para>
/// </remarks>
/// <param name="regions">The buffer's read-only regions as they stand, in document order.</param>
/// <param name="view">The text view whose link generators decide what is an address.</param>
internal sealed class ReadOnlyRegionLinks(Func<IReadOnlyList<TextRegion>> regions, TextView view) : VisualLineElementGenerator
{
    public override int GetFirstInterestedOffset(int startOffset)
    {
        // A region is whole lines, so the line being built is in one or is not, and so is every address on it; a
        // link generator looks no further than that line. Most lines are in none, and are left to them unasked.
        if (!InRegion(startOffset))
            return -1;

        var first = -1;
        foreach (var generator in view.ElementGenerators)
        {
            if (generator is LinkElementGenerator link && link.GetFirstInterestedOffset(startOffset) is var offset and >= 0
                && (first < 0 || offset < first))
                first = offset;
        }
        return first;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        // The link generator measures the address; its element is discarded for plain text of the same length.
        foreach (var generator in view.ElementGenerators)
        {
            if (generator is LinkElementGenerator link && link.ConstructElement(offset) is { } element)
                return new VisualLineText(CurrentContext.VisualLine, element.DocumentLength);
        }
        return null;
    }

    private bool InRegion(int offset)
    {
        foreach (var region in regions())
        {
            if (region.Start > offset)
                return false;
            if (offset < region.End)
                return true;
        }
        return false;
    }
}
