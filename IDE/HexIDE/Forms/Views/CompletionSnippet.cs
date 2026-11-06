using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Snippets;

namespace HexIDE.Forms.Views;

/// <summary>The numbered tabstops and placeholders in an LSP completion snippet.</summary>
internal sealed record CompletionSnippet(string Text, IReadOnlyList<CompletionSnippet.Stop> Stops)
{
    internal sealed record Stop(int Number, int Offset, int Length, string[]? Choices = null);

    public static CompletionSnippet? Parse(string source)
    {
        var text = new StringBuilder();
        var stops = new List<Stop>();
        var defaults = new Dictionary<int, string>();
        var position = 0;
        if (!Read(false) || position != source.Length) return null;
        // A reference can precede the placeholder that supplies its default.
        position = 0;
        text.Clear();
        stops.Clear();
        if (!Read(false)) return null;
        var result = text.ToString();
        if (stops.GroupBy(stop => stop.Number).Any(group => group
                .Select(stop => result.Substring(stop.Offset, stop.Length)).Distinct().Count() > 1)) return null;
        return new(result, stops);

        bool Read(bool nested)
        {
            while (position < source.Length)
            {
                var c = source[position++];
                if (c == '}' && nested)
                    return true;
                if (c == '\\' && position < source.Length && source[position] is '$' or '}' or '\\')
                {
                    text.Append(source[position++]);
                    continue;
                }
                if (c != '$')
                {
                    text.Append(c);
                    continue;
                }
                if (nested) return false;
                var braced = position < source.Length && source[position] == '{';
                if (braced) position++;
                var first = position;
                while (position < source.Length && char.IsAsciiDigit(source[position])) position++;
                if (first == position || !int.TryParse(source.AsSpan(first, position - first), out var number))
                    return false;
                var start = text.Length;
                string[]? choices = null;
                if (braced && position < source.Length && source[position] == ':')
                {
                    position++;
                    if (!Read(true)) return false;
                    defaults.TryAdd(number, text.ToString(start, text.Length - start));
                }
                else if (braced && position < source.Length && source[position] == '|')
                {
                    position++;
                    var values = new List<string>();
                    var value = new StringBuilder();
                    var closed = false;
                    while (position < source.Length)
                    {
                        var character = source[position++];
                        if (character == '\\' && position < source.Length && source[position] is '$' or '}' or '\\' or ',' or '|')
                            value.Append(source[position++]);
                        else if (character == ',')
                        {
                            values.Add(value.ToString());
                            value.Clear();
                        }
                        else if (character == '|')
                        {
                            if (position >= source.Length || source[position++] != '}') return false;
                            values.Add(value.ToString());
                            closed = true;
                            break;
                        }
                        else value.Append(character);
                    }
                    if (!closed) return false;
                    choices = values.ToArray();
                    text.Append(choices[0]);
                    defaults.TryAdd(number, choices[0]);
                }
                else
                {
                    if (braced && (position >= source.Length || source[position++] != '}')) return false;
                    if (defaults.TryGetValue(number, out var value)) text.Append(value);
                }
                stops.Add(new(number, start, text.Length - start, choices));
            }
            return !nested;
        }
    }

    internal static IEnumerable<CompletionWindow> OpenChoiceWindows(TextArea area) =>
        area.StackedInputHandlers.OfType<Navigation>().Select(handler => handler.ChoiceWindow)
            .OfType<CompletionWindow>().Where(window => window.IsOpen);

    public void Activate(TextArea area, int offset)
    {
        var context = new InsertionContext(area, offset) { InsertionPosition = offset + Text.Length };
        var groups = Stops.Where(stop => stop.Number != 0).GroupBy(stop => stop.Number)
            .OrderBy(group => group.Key).ToArray();
        foreach (var group in groups)
        {
            var segments = group.OrderBy(stop => stop.Offset)
                .Select(stop => (ISegment)new TextSegment { StartOffset = offset + stop.Offset, Length = stop.Length }).ToArray();
            context.Link(segments[0], segments[1..]);
        }
        var finalStop = Stops.FirstOrDefault(stop => stop.Number == 0);
        var final = area.Document.CreateAnchor(offset + (finalStop?.Offset ?? Text.Length));
        final.MovementType = finalStop is null || Stops.TakeWhile(stop => stop != finalStop)
            .Any(stop => stop.Offset + stop.Length == finalStop.Offset)
            ? AnchorMovementType.AfterInsertion : AnchorMovementType.BeforeInsertion;
        final.SurviveDeletion = true;
        context.RaiseInsertionCompleted(EventArgs.Empty);
        if (groups.Length == 0)
        {
            area.Caret.Offset = final.Offset;
            return;
        }
        var choices = groups.Select(group => group.FirstOrDefault(stop => stop.Choices is not null)?.Choices).ToArray();
        area.PushStackedInputHandler(new Navigation(area, context, final, choices));
    }

    // AvaloniaEdit provides linked editing, but its default handler visits buffer order and wraps on Tab.
    private sealed class Navigation(TextArea area, InsertionContext context, TextAnchor final, string[]?[] choices) : TextAreaStackedInputHandler(area)
    {
        private int _index;
        private CompletionWindow? _choices;
        public CompletionWindow? ChoiceWindow => _choices;
        private ISegment[] Segments => context.ActiveElements.Where(element => element.IsEditable)
            .Select(element => element.Segment).ToArray();

        public override void Attach()
        {
            base.Attach();
            context.Deactivated += OnDeactivated;
            Select();
        }

        public override void Detach()
        {
            _choices?.Close();
            context.Deactivated -= OnDeactivated;
            context.Deactivate(new SnippetEventArgs(DeactivateReason.InputHandlerDetached));
            base.Detach();
        }

        private void OnDeactivated(object? sender, SnippetEventArgs e) => TextArea.PopStackedInputHandler(this);

        private void Select()
        {
            _choices?.Close();
            var segment = Segments[_index];
            TextArea.Selection = Selection.Create(TextArea, segment);
            TextArea.Caret.Offset = segment.EndOffset;
            if (choices[_index] is { } values && TopLevel.GetTopLevel(TextArea) is not null)
            {
                _choices = new CompletionWindow(TextArea)
                {
                    StartOffset = segment.Offset,
                    EndOffset = segment.EndOffset,
                    CloseWhenCaretAtBeginning = false
                };
                foreach (var value in values)
                    _choices.CompletionList.CompletionData.Add(new Choice(value));
                _choices.Show();
            }
        }

        private sealed class Choice(string value) : ICompletionData
        {
            public string Text => value;
            public override string ToString() => Text;
            public object Content => value;
            public object? Description => null;
            public IImage? Image => null;
            public double Priority => 0;

            public void Complete(TextArea textArea, ISegment completionSegment, EventArgs e)
            {
                textArea.Document.Replace(completionSegment.Offset, completionSegment.Length, value);
                textArea.Selection = Selection.Create(textArea, completionSegment.Offset, completionSegment.Offset + value.Length);
                textArea.Caret.Offset = completionSegment.Offset + value.Length;
            }
        }

        public override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Tab)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _index = Math.Max(0, _index - 1);
                else _index++;
                if (_index < Segments.Length) Select();
                else Finish(true);
                e.Handled = true;
            }
            else if (e.Key is Key.Escape or Key.Return)
            {
                Finish(e.Key == Key.Return);
                e.Handled = true;
            }
        }

        private void Finish(bool moveToEnd)
        {
            TextArea.PopStackedInputHandler(this);
            context.Deactivate(new SnippetEventArgs(DeactivateReason.InputHandlerDetached));
            TextArea.ClearSelection();
            if (moveToEnd) TextArea.Caret.Offset = final.Offset;
        }
    }
}
