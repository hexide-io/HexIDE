using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using HexIDE.Language;

namespace HexIDE.Controls;

/// <summary>Draws determined inactive conditional-compilation lines in the theme's colour (#1113).</summary>
internal sealed class InactiveCodeColorizer(Func<IReadOnlyList<LineRange>> lines) : DocumentColorizingTransformer
{
    public IBrush? Brush { get; set; }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (Brush is not { } brush || !lines().Any(range => range.Contains(line.LineNumber)))
            return;
        ChangeLinePart(line.Offset, line.EndOffset, element => element.TextRunProperties.SetForegroundBrush(brush));
    }
}
