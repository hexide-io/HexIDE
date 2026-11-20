using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HexIDE.Runtime.BuiltinControls;

// #4551 / #4586, VB6 SP6 measurements in oracle PR #5031, quoted in the issue.
// Measure the full line even when the face is too short; the edge clips it, rather than squeezing it.
internal sealed class CommandButtonCaptionPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var desired = new Size();
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
            desired = new Size(Math.Max(desired.Width, child.DesiredSize.Width),
                Math.Max(desired.Height, child.DesiredSize.Height));
        }
        return desired;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Clip = new RectangleGeometry(new Rect(2, 2, Math.Max(0, finalSize.Width - 4),
            Math.Max(0, finalSize.Height - 4)));
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            child.Arrange(new Rect(Math.Truncate((finalSize.Width - 1 - size.Width) / 2),
                Math.Truncate((finalSize.Height - 1 - size.Height) / 2), size.Width, size.Height));
        }
        return finalSize;
    }
}
