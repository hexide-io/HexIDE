using Avalonia;
using Avalonia.Input;

namespace HexIDE.Runtime.BuiltinControls;

public sealed partial class VBRichTextBox
{
    private int? selectionAnchor;
    private int selectionEnd;
    private int? navigationLine;

    private bool Navigate(KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)) return false;
        var command = Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var word = (e.KeyModifiers & command) == command;
        if ((e.KeyModifiers & ~(command | KeyModifiers.Shift)) != 0) return false;
        if (!shift && !word && Length > 0 && e.Key is not (Key.Left or Key.Right))
            throw new NotImplementedException("RichTextBox plain navigation selection endpoints are not implemented (#1100)");
        if (word && e.Key is not (Key.Left or Key.Right)) return false;
        var anchor = selectionAnchor ?? Start;
        var end = selectionAnchor.HasValue ? selectionEnd : Start + Length;
        if (!shift && Length > 0)
        {
            if (Start + Length > Document.Text.Length)
                throw new NotImplementedException("RichTextBox hidden-break collapse endpoints are not implemented (#1100)");
            if (word && e.Key == Key.Left)
                throw new NotImplementedException("RichTextBox command-left selection collapse endpoints are not implemented (#1100)");
            Select(e.Key == Key.Left ? Start : Math.Min(Start + Length, Document.Text.Length), 0);
            return true;
        }
        var text = Document.Text;
        if (text.Replace("\r\n", "").IndexOfAny(['\r', '\n']) >= 0)
            throw new NotImplementedException("RichTextBox lone-break keyboard endpoints are not implemented (#1100)");
        int next;
        int? nextLine = null;
        if (word) next = WordPosition(Math.Min(end, text.Length), e.Key == Key.Right);
        else if (e.Key == Key.Left)
            next = end > text.Length ? text.Length : end >= 2 && text.AsSpan(end - 2, 2).SequenceEqual("\r\n") ? end - 2 : Math.Max(0, end - 1);
        else if (e.Key == Key.Right)
            next = end >= text.Length ? text.Length + 2 : text.AsSpan(end).StartsWith("\r\n") ? end + 2 : end + 1;
        else
        {
            var starts = new List<int> { 0 };
            for (var i = 0; i < text.Length; i++)
                if (text[i] == '\n') starts.Add(i + 1);
            using var layout = CreateLayout();
            if (layout.TextLines.Count != starts.Count)
                throw new NotImplementedException("RichTextBox wrapped or lone-break keyboard endpoints are not implemented (#1100)");
            // Included breaks retain their line for End and upward reversal; Down uses the reported endpoint.
            var positionLine = starts.FindLastIndex(start => start <= Math.Min(end, text.Length));
            var line = e.Key == Key.Down ? positionLine : navigationLine ?? positionLine;
            int ContentEnd(int index) => index + 1 < starts.Count ? starts[index + 1] - 2 : text.Length;
            int BreakEnd(int index) => index + 1 < starts.Count ? starts[index + 1] : text.Length + 2;
            if (e.Key == Key.Home)
            {
                next = starts[line];
                nextLine = line;
            }
            else if (e.Key == Key.End)
            {
                next = BreakEnd(line);
                nextLine = line;
            }
            else
            {
                var target = line + (e.Key == Key.Down ? 1 : -1);
                nextLine = Math.Clamp(target, 0, starts.Count - 1);
                if (target < 0) next = 0;
                else if (target >= starts.Count) next = text.Length + 2;
                else
                {
                    var column = Math.Min(end, ContentEnd(line)) - starts[line];
                    next = column >= ContentEnd(target) - starts[target] ? BreakEnd(target) : starts[target] + column;
                }
            }
        }
        if (shift)
        {
            Select(Math.Min(anchor, next), Math.Abs(next - anchor));
            selectionAnchor = anchor;
            selectionEnd = next;
            navigationLine = nextLine;
            KeepCaretVisible();
        }
        else Select(next, 0);
        return true;
    }

    private int WordPosition(int position, bool right)
    {
        var text = Document.Text;
        if (text.Any(c => c > 127) || text.Replace("\r\n", "").IndexOfAny(['\r', '\n']) >= 0)
            throw new NotImplementedException("RichTextBox non-ASCII or lone-break word endpoints are not implemented (#1100)");
        var stops = new List<int> { 0 };
        var i = 0;
        bool Word(char c) => char.IsAsciiLetterOrDigit(c) || c == '\'';
        while (i < text.Length)
        {
            if (text.AsSpan(i).StartsWith("\r\n")) i += 2;
            else
            {
                var isWord = Word(text[i]);
                i++;
                while (i < text.Length && text[i] is not ('\r' or '\n' or ' ' or '\t') && Word(text[i]) == isWord) i++;
                while (i < text.Length && text[i] is ' ' or '\t') i++;
            }
            stops.Add(i);
        }
        return right ? stops.FirstOrDefault(stop => stop > position, text.Length)
            : stops.LastOrDefault(stop => stop < position, 0);
    }
}
