using System.Collections.Generic;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using HexIDE.Lsp.Messages;

namespace HexIDE.Controls;

/// <summary>
/// Converts <see cref="FoldingRange"/> objects received from the LSP server into
/// <see cref="NewFolding"/> objects and applies them to a <see cref="FoldingManager"/>.
/// The collapsed label for each fold is the trimmed text of the fold's first line.
/// </summary>
public static class Vb6FoldingAdapter
{
    public static void Apply(FoldingManager manager, TextDocument document, FoldingRange[] ranges)
    {
        var newFoldings = new List<NewFolding>(ranges.Length);

        foreach (var r in ranges)
        {
            var startLine = r.StartLine + 1; // LSP is 0-based; AvaloniaEdit is 1-based
            var endLine   = r.EndLine   + 1;

            if (startLine < 1 || endLine > document.LineCount || endLine <= startLine)
                continue;

            var startOffset = document.GetLineByNumber(startLine).Offset;
            var endDocLine  = document.GetLineByNumber(endLine);
            var endOffset   = endDocLine.Offset + endDocLine.Length;

            // Use the trimmed first line as the collapsed placeholder label
            var firstLine = document.GetLineByNumber(startLine);
            var label = document.GetText(firstLine.Offset, firstLine.Length).Trim();
            if (string.IsNullOrEmpty(label))
                label = "...";

            newFoldings.Add(new NewFolding(startOffset, endOffset) { Name = label });
        }

        // AvaloniaEdit requires foldings sorted by StartOffset
        newFoldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        manager.UpdateFoldings(newFoldings, -1);
    }
}
