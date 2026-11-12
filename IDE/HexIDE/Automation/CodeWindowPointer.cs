using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Rendering;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;

namespace HexIDE.Automation;

internal static class CodeWindowPointer
{
    private static TextEditor? EditorOf(Control target) => target as TextEditor
        ?? target.FindAncestorOfType<TextEditor>()
        ?? (target as CodeEditorView)?.ActiveTextArea.FindAncestorOfType<TextEditor>();

    internal static string Note(Control target, Point point)
    {
        var found = target is CodeEditorView
            ? target.GetVisualDescendants().OfType<TextEditor>().FirstOrDefault(candidate =>
                target.TranslatePoint(point, candidate) is { } p && new Rect(candidate.Bounds.Size).Contains(p))
            : EditorOf(target);
        if (found is not { DataContext: CodeEditorViewModel vm } editor) return "";
        var view = editor.TextArea.TextView;
        if (target.TranslatePoint(point, view) is not { } local || local.Y < 0 || local.Y >= view.Bounds.Height) return "";
        view.EnsureVisualLines();
        var line = view.GetVisualLineFromVisualTop(local.Y + view.VerticalOffset);
        if (line is null) return "; below the document text";
        var number = line.FirstDocumentLine.LineNumber;
        var column = "";
        if (local.X >= 0 && local.X <= view.Bounds.Width && view.GetPosition(local + view.ScrollOffset) is { } position)
        {
            number = position.Line;
            column = $", column {position.Column}";
        }
        var marks = (vm.HasBreakpoint(number) ? "; breakpoint" : "")
                    + (vm.BookmarkService.IsBookmarked(vm.Identity, number - 1) ? "; bookmark" : "");
        return $"; line {number}{column} of {vm.Identity.Display}{marks}";
    }

    // Resolve immediately before input, using the same visual lines as the margins; lenses add height, not lines.
    internal static bool Aim(Control target, ref string? value, out string? error)
    {
        error = null;
        var pressValue = Regex.Split(value ?? "", @"(?:^|\s)to\s+", RegexOptions.IgnoreCase)[0];
        var token = Regex.Match(pressValue, @"(?:^|\s)(line:[^\s]+)", RegexOptions.IgnoreCase);
        if (!token.Success) return true;
        if (EditorOf(target) is not { DataContext: CodeEditorViewModel vm } editor)
        {
            error = "line:N or line:N,col:M needs a code window, its text or a margin; nothing was pressed";
            return false;
        }
        var match = Regex.Match(token.Groups[1].Value, @"^line:(\d+)(?:,col:(\d+))?$", RegexOptions.IgnoreCase);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var line) || line < 1 || line > editor.Document.LineCount)
        {
            error = $"'{token.Groups[1].Value}' is not a position in {vm.Identity.Display}, which has {editor.Document.LineCount} lines; nothing was pressed";
            return false;
        }
        var column = 1;
        if (match.Groups[2].Success && (!int.TryParse(match.Groups[2].Value, out column) || column < 1
            || column > editor.Document.GetLineByNumber(line).Length + 1))
        {
            error = $"column must be 1 to {editor.Document.GetLineByNumber(line).Length + 1} on line {line} of {vm.Identity.Display}; nothing was pressed";
            return false;
        }
        if (editor.TextArea.TextView.GetService(typeof(FoldingManager)) is FoldingManager folds)
            foreach (var fold in folds.GetFoldingsContaining(editor.Document.GetOffset(line, column))) fold.IsFolded = false;
        editor.ScrollTo(line, column);
        editor.UpdateLayout();
        var view = editor.TextArea.TextView;
        view.EnsureVisualLines();
        var point = view.GetVisualPosition(new TextViewPosition(line, column), VisualYPosition.TextMiddle) - view.ScrollOffset;
        if (view.TranslatePoint(point, target) is not { } local)
        {
            error = "could not translate the code position; nothing was pressed";
            return false;
        }
        if (target is AbstractMargin) local = new Point(target.Bounds.Width / 2, local.Y);
        var replacement = FormattableString.Invariant($"{local.X},{local.Y}");
        var group = token.Groups[1];
        value = value![..group.Index] + replacement + value[(group.Index + group.Length)..];
        return true;
    }
}
