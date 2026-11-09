using Avalonia;
using Avalonia.Media;

namespace HexIDE.Runtime.BuiltinControls;

internal static class VBClientEdge
{
    internal static void Draw(DrawingContext context, Size size, bool sunken, bool singleBorder)
    {
        if (sunken)
        {
            Ring(0, VbSystemColor.Btnshadow, VbSystemColor.Btnhighlight);
            Ring(1, VbSystemColor._3DDKSHADOW, VbSystemColor._3DLIGHT);
        }
        if (singleBorder) Ring(sunken ? 2 : 0, VbSystemColor.Windowframe, VbSystemColor.Windowframe);

        void Ring(int inset, VbSystemColor top, VbSystemColor bottom)
        {
            var rect = new Rect(inset + 0.5, inset + 0.5,
                Math.Max(0, size.Width - 2 * inset - 1), Math.Max(0, size.Height - 2 * inset - 1));
            var first = new Pen(VBColor.FromSystemColor(top).ToBrush());
            var last = new Pen(VBColor.FromSystemColor(bottom).ToBrush());
            context.DrawLine(first, rect.TopLeft, rect.TopRight);
            context.DrawLine(first, rect.TopLeft, rect.BottomLeft);
            context.DrawLine(last, rect.BottomLeft, rect.BottomRight);
            context.DrawLine(last, rect.TopRight, rect.BottomRight);
        }
    }
}
