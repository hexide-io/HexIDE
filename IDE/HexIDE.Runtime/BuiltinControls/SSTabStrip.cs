using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Runtime.BuiltinControls;

// Layout is deliberately qualitative: the oracle records row order and caption treatment, not pixel metrics.
internal sealed class SSTabStrip(VBSSTab owner) : Panel
{
    private readonly List<(int Tab, Border Header)> headers = [];
    private readonly Border page = new();
    private readonly Avalonia.Controls.Shapes.Rectangle focus = new()
    {
        StrokeThickness = 1,
        StrokeDashArray = [1, 1],
    };

    internal int? TabAt(Point point)
    {
        foreach (var (tab, header) in headers)
            if (header.Bounds.Contains(point))
                return tab;
        return null;
    }

    internal void RefreshFocus()
    {
        focus.IsVisible = owner.IsFocused && owner.ShowFocusRect && headers.Any(h => h.Tab == owner.Tab);
    }

    internal void Refresh()
    {
        Children.Clear();
        headers.Clear();
        var face = VBColor.FromSystemColor(VbSystemColor.Btnface).ToBrush();
        var shadow = VBColor.FromSystemColor(VbSystemColor.Btnshadow).ToBrush();
        page.Background = face;
        page.BorderBrush = shadow;
        page.BorderThickness = new Thickness(1, 1, 2, 2);
        Children.Add(page);
        for (var i = 0; i < owner.Tabs; i++)
        {
            if (!owner.TabVisible(i))
                continue;
            var caption = new TextBlock
            {
                Text = owner.Caption(i).Replace("&&", "\0").Replace("&", "").Replace("\0", "&"),
                FontWeight = owner.VbStyle == 0 && i == owner.Tab ? FontWeight.Bold : FontWeight.Normal,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextWrapping = owner.WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                Foreground = owner.IsEffectivelyEnabled && owner.TabEnabled(i)
                    ? VBColor.Black.ToBrush() : VBColor.FromSystemColor(VbSystemColor.Graytext).ToBrush(),
            };
            var header = new Border
            {
                Child = caption,
                Background = face,
                BorderBrush = shadow,
                BorderThickness = i == owner.Tab ? new Thickness(1, 1, 1, 0) : new Thickness(1),
                CornerRadius = new CornerRadius(3, 3, 0, 0),
                ClipToBounds = true,
                IsEnabled = owner.TabEnabled(i),
            };
            headers.Add((i, header));
            Children.Add(header);
        }
        focus.Stroke = VBColor.Black.ToBrush();
        RefreshFocus();
        Children.Add(focus);
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            child.Measure(Size.Infinity);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rows = owner.Rows;
        var height = Math.Round(owner.TabHeight);
        var thickness = Math.Min(rows * height, owner.TabOrientation < 2 ? finalSize.Height : finalSize.Width);
        var pageRect = owner.TabOrientation switch
        {
            0 => new Rect(0, thickness, finalSize.Width, Math.Max(0, finalSize.Height - thickness)),
            1 => new Rect(0, 0, finalSize.Width, Math.Max(0, finalSize.Height - thickness)),
            2 => new Rect(thickness, 0, Math.Max(0, finalSize.Width - thickness), finalSize.Height),
            _ => new Rect(0, 0, Math.Max(0, finalSize.Width - thickness), finalSize.Height),
        };
        page.Arrange(pageRect);
        var active = headers.FindIndex(h => h.Tab == owner.Tab);
        var activeRow = active < 0 ? -1 : active / owner.TabsPerRow;
        var length = owner.TabOrientation < 2 ? finalSize.Width : finalSize.Height;
        for (var row = 0; row < rows; row++)
        {
            var front = row == activeRow;
            var depth = front ? 0 : row < activeRow ? row + 1 : row;
            var offset = depth * 2d;
            var along = offset;
            foreach (var (_, header) in headers.Skip(row * owner.TabsPerRow).Take(owner.TabsPerRow))
            {
                var width = owner.VbStyle == 0
                    ? length / owner.TabsPerRow
                    : header.DesiredSize.Width + 12;
                if (owner.TabMaxWidth > 0)
                    width = Math.Min(width, owner.TabMaxWidth);
                var across = (rows - 1 - depth) * height;
                var rect = owner.TabOrientation switch
                {
                    0 => new Rect(along, across, width, height),
                    1 => new Rect(along, finalSize.Height - across - height, width, height),
                    2 => new Rect(across, along, height, width),
                    _ => new Rect(finalSize.Width - across - height, along, height, width),
                };
                header.Measure(rect.Size);
                header.Arrange(rect);
                along += width;
            }
        }
        var activeHeader = headers.Find(h => h.Tab == owner.Tab).Header;
        if (activeHeader?.Child is { } caption)
        {
            var rect = caption.Bounds.Translate(activeHeader.Bounds.Position).Deflate(1);
            focus.Arrange(rect);
        }
        return finalSize;
    }
}
