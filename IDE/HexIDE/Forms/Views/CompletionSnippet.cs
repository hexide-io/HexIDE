using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Snippets;

namespace HexIDE.Forms.Views;

/// <summary>Tabstops, nested placeholders and variables in an LSP completion snippet.</summary>
internal sealed record CompletionSnippet(string Text, IReadOnlyList<CompletionSnippet.Stop> Stops)
{
    internal sealed record Stop(int Number, int Offset, int Length, string[]? Choices = null, int? Parent = null);

    private sealed record Part(string? Text = null, int? Number = null, string? Variable = null,
        IReadOnlyList<Part>? Children = null, string[]? Choices = null);

    public static CompletionSnippet? Parse(string source, IReadOnlyDictionary<string, string?>? variables = null, string? indentation = null)
    {
        var position = 0;
        var definitions = new Dictionary<int, Part>();
        var unknowns = new Dictionary<string, int>();
        var parts = Read(false, 0);
        if (parts is null) return null;
        var nextNumber = definitions.Keys.DefaultIfEmpty(0).Max();
        var text = new StringBuilder();
        var stops = new List<Stop>();
        const int maxRenderedLength = 1024 * 1024;
        var renderedNumbers = new Dictionary<int, string>();
        var resolving = new HashSet<int>();
        if (!Render(parts, text, stops)) return null;
        return new(text.ToString(), stops);

        IReadOnlyList<Part>? Read(bool nested, int depth)
        {
            if (depth > 100) return null;
            var result = new List<Part>();
            var literal = new StringBuilder();
            while (position < source.Length)
            {
                var c = source[position++];
                if (c == '}' && nested)
                {
                    Flush();
                    return result;
                }
                if (c == '\\' && position < source.Length && source[position] is '$' or '}' or '\\')
                {
                    literal.Append(source[position++]);
                    continue;
                }
                if (c != '$')
                {
                    literal.Append(c);
                    continue;
                }
                Flush();
                var braced = position < source.Length && source[position] == '{';
                if (braced) position++;
                var first = position;
                int? number = null;
                string? variable = null;
                if (position < source.Length && char.IsAsciiDigit(source[position]))
                {
                    while (position < source.Length && char.IsAsciiDigit(source[position])) position++;
                    if (!int.TryParse(source.AsSpan(first, position - first), out var parsed)) return null;
                    number = parsed;
                }
                else
                {
                    if (position >= source.Length || !IsVariableStart(source[position]))
                    {
                        if (braced) return null;
                        literal.Append('$');
                        continue;
                    }
                    position++;
                    while (position < source.Length && (IsVariableStart(source[position]) || char.IsAsciiDigit(source[position]))) position++;
                    variable = source[first..position];
                }
                IReadOnlyList<Part>? children = null;
                string[]? choices = null;
                if (braced && position < source.Length && source[position] == ':')
                {
                    position++;
                    children = Read(true, depth + 1);
                    if (children is null) return null;
                }
                else if (braced && number is not null && position < source.Length && source[position] == '|')
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
                            if (position >= source.Length || source[position++] != '}') return null;
                            values.Add(value.ToString());
                            closed = true;
                            break;
                        }
                        else value.Append(character);
                    }
                    if (!closed) return null;
                    choices = values.ToArray();
                }
                else if (braced && (position >= source.Length || source[position++] != '}'))
                    return null; // Transforms are deliberately refused, including inside an unused default.
                var part = new Part(Number: number, Variable: variable, Children: children, Choices: choices);
                result.Add(part);
                if (number is { } index)
                {
                    if (!definitions.TryGetValue(index, out var existing) || existing.Children is null && existing.Choices is null)
                        definitions[index] = part;
                }
            }
            Flush();
            return nested ? null : result;

            void Flush()
            {
                if (literal.Length == 0) return;
                result.Add(new(Text: indentation is null ? literal.ToString()
                    : new CompletionSnippet(literal.ToString(), []).AdjustIndentation(indentation).Text));
                literal.Clear();
            }
        }

        bool Render(IReadOnlyList<Part> values, StringBuilder output, List<Stop>? locations, int? parent = null)
        {
            foreach (var part in values)
            {
                if (part.Text is { } literal)
                {
                    if (literal.Length > maxRenderedLength - output.Length) return false;
                    output.Append(literal);
                    continue;
                }
                var start = output.Length;
                if (part.Number is { } number)
                {
                    var definition = definitions[number];
                    if (!renderedNumbers.TryGetValue(number, out var rendered))
                    {
                        if (resolving.Count > 100 || !resolving.Add(number)) return false;
                        var buffer = new StringBuilder();
                        if (definition.Choices is { } choices)
                        {
                            if (choices[0].Length > maxRenderedLength) return false;
                            buffer.Append(choices[0]);
                        }
                        else if (definition.Children is { } children && !Render(children, buffer, null)) return false;
                        resolving.Remove(number);
                        renderedNumbers[number] = rendered = buffer.ToString();
                    }
                    if (rendered.Length > maxRenderedLength - output.Length) return false;
                    if (locations is not null && ReferenceEquals(part, definition) && definition.Children is { } nestedChildren)
                    {
                        if (!Render(nestedChildren, output, locations, number)) return false;
                    }
                    else output.Append(rendered);
                    locations?.Add(new(number, start, output.Length - start, definition.Choices, parent));
                }
                else if (part.Variable is { } variable)
                {
                    if (variables?.TryGetValue(variable, out var value) == true && !string.IsNullOrEmpty(value))
                    {
                        if (value.Length > maxRenderedLength - output.Length) return false;
                        output.Append(value);
                    }
                    else if (part.Children is { } children)
                    {
                        if (!Render(children, output, locations, parent)) return false;
                    }
                    else if (!KnownVariables.Contains(variable))
                    {
                        if (!unknowns.TryGetValue(variable, out var index))
                        {
                            if (nextNumber == int.MaxValue) return false;
                            unknowns[variable] = index = ++nextNumber;
                        }
                        if (variable.Length > maxRenderedLength - output.Length) return false;
                        output.Append(variable);
                        locations?.Add(new(index, start, variable.Length, Parent: parent));
                    }
                }
            }
            return true;
        }
    }

    private static bool IsVariableStart(char c) => char.IsAsciiLetter(c) || c == '_';

    private static readonly HashSet<string> KnownVariables =
    [
        "TM_SELECTED_TEXT", "TM_CURRENT_LINE", "TM_CURRENT_WORD", "TM_LINE_INDEX", "TM_LINE_NUMBER",
        "TM_FILENAME", "TM_FILENAME_BASE", "TM_DIRECTORY", "TM_FILEPATH"
    ];

    internal static IReadOnlyDictionary<string, string?> Variables(TextArea area, string? documentUri)
    {
        var document = area.Document;
        var line = document.GetLineByOffset(area.Caret.Offset);
        var start = area.Caret.Offset;
        var end = start;
        while (start > line.Offset && IsWord(document.GetCharAt(start - 1))) start--;
        while (end < line.EndOffset && IsWord(document.GetCharAt(end))) end++;
        var path = Uri.TryCreate(documentUri, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : null;
        return new Dictionary<string, string?>
        {
            ["TM_SELECTED_TEXT"] = area.Selection.GetText(),
            ["TM_CURRENT_LINE"] = document.GetText(line),
            ["TM_CURRENT_WORD"] = document.GetText(start, end - start),
            ["TM_LINE_INDEX"] = (line.LineNumber - 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["TM_LINE_NUMBER"] = line.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["TM_FILENAME"] = path is null ? null : System.IO.Path.GetFileName(path),
            ["TM_FILENAME_BASE"] = path is null ? null : FileBase(path),
            ["TM_DIRECTORY"] = path is null ? null : System.IO.Path.GetDirectoryName(path),
            ["TM_FILEPATH"] = path
        };

        static string FileBase(string path)
        {
            var name = System.IO.Path.GetFileName(path);
            var dot = name.LastIndexOf('.');
            return dot > 0 ? name[..dot] : name;
        }

        static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
    }

    public CompletionSnippet AdjustIndentation(string indentation)
    {
        var text = new StringBuilder();
        var offsets = new int[Text.Length + 1];
        var position = 0;
        while (position < Text.Length)
        {
            offsets[position] = text.Length;
            var character = Text[position++];
            text.Append(character);
            if (character != '\n' && (character != '\r' || position < Text.Length && Text[position] == '\n'))
                continue;
            text.Append(indentation);
        }
        offsets[position] = text.Length;
        return new(text.ToString(), Stops.Select(stop => stop with
        {
            Offset = offsets[stop.Offset],
            Length = offsets[stop.Offset + stop.Length] - offsets[stop.Offset]
        }).ToArray());
    }

    internal static IEnumerable<CompletionWindow> OpenChoiceWindows(TextArea area) =>
        area.StackedInputHandlers.OfType<Navigation>().Select(handler => handler.ChoiceWindow)
            .OfType<CompletionWindow>().Where(window => window.IsOpen);

    internal static void Deactivate(TextArea area)
    {
        foreach (var handler in area.StackedInputHandlers.OfType<Navigation>().ToArray())
            area.PopStackedInputHandler(handler);
    }

    public void Activate(TextArea area, int offset)
    {
        var context = new InsertionContext(area, offset) { InsertionPosition = offset + Text.Length };
        var groups = Stops.Where(stop => stop.Number != 0).GroupBy(stop => stop.Number)
            .OrderBy(group => group.Key).ToArray();
        NestedLinks? nested = null;
        if (Stops.Any(stop => stop.Parent is not null))
        {
            nested = new NestedLinks(context, groups.Select(group => group.ToArray()).ToArray(), offset);
            foreach (var element in nested.Elements)
                context.RegisterActiveElement(new SnippetReplaceableTextElement(), element);
        }
        else
        {
            foreach (var group in groups)
            {
                var segments = group.OrderBy(stop => stop.Offset)
                    .Select(stop => (ISegment)new TextSegment { StartOffset = offset + stop.Offset, Length = stop.Length }).ToArray();
                context.Link(segments[0], segments[1..]);
            }
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
        area.PushStackedInputHandler(new Navigation(area, context, final, index => nested is null
            ? choices[index] : nested.Select(index)));
    }

    // Flat AvaloniaEdit links end the whole session when any inner anchor is deleted. Nested fields
    // instead retire just that field, and propagate inner edits before copying their enclosing fields.
    private sealed class NestedLinks
    {
        private readonly TextArea _area;
        private readonly InsertionContext _context;
        private bool _attached;
        private bool _copying;
        private Element.Occurrence? _copyTarget;
        public Element[] Elements { get; }
        private int _selectedNumber;

        public string[]? Select(int index)
        {
            var element = Elements.Where(element => element.IsEditable).ElementAt(index);
            _selectedNumber = element.Number;
            return element.Choices;
        }

        public NestedLinks(InsertionContext context, Stop[][] groups, int offset)
        {
            _context = context;
            _area = context.TextArea;
            Elements = groups.Select(group => new Element(this, group, groups.SelectMany(value => value).ToArray(), offset)).ToArray();
        }

        private void Attach()
        {
            if (_attached) return;
            _attached = true;
            TextDocumentWeakEventManager.Changing.AddHandler(_area.Document, Changing);
            TextDocumentWeakEventManager.TextChanged.AddHandler(_area.Document, TextChanged);
        }

        private void Detach()
        {
            if (!_attached) return;
            _attached = false;
            TextDocumentWeakEventManager.Changing.RemoveHandler(_area.Document, Changing);
            TextDocumentWeakEventManager.TextChanged.RemoveHandler(_area.Document, TextChanged);
        }

        private void Changing(object? sender, DocumentChangeEventArgs e)
        {
            if (!_area.Document.UndoStack.AcceptChanges)
            {
                _context.Deactivate(new SnippetEventArgs(DeactivateReason.Deleted));
                return;
            }
            var selectedElement = Elements.First(element => element.Number == _selectedNumber);
            var selected = selectedElement.Segment;
            if (!_copying && e.RemovalLength > 0)
            {
                foreach (var element in Elements.Where(element => element.IsEditable && element.Number != _selectedNumber))
                {
                    foreach (var occurrence in element.Occurrences.ToArray())
                    {
                        var segment = occurrence.Segment;
                        if (segment.Offset < selected.Offset || segment.EndOffset > selected.EndOffset
                            || e.Offset > segment.Offset || e.Offset + e.RemovalLength < segment.EndOffset
                            || !DescendsFrom(occurrence.Parent, _selectedNumber)) continue;
                        // A confined edit keeps the child, even when it shares its parent's extent.
                        // An adjacent deletion does not consume an empty stop.
                        if (e.Offset == segment.Offset && e.RemovalLength == segment.Length) continue;
                        if (segment.Length == 0 && (e.Offset == segment.Offset
                            || e.Offset + e.RemovalLength == segment.EndOffset)) continue;
                        if (element.Occurrences.Count == 1) element.IsEditable = false;
                        else element.Occurrences.Remove(occurrence);
                    }
                }
            }
            var target = _copyTarget ?? selectedElement.Occurrences[0];
            foreach (var element in Elements.Where(element => element.IsEditable))
            {
                foreach (var occurrence in element.Occurrences)
                {
                    var ownsEdit = ReferenceEquals(occurrence, target)
                        || e.RemovalLength > 0 && e.Offset >= occurrence.Segment.Offset
                        && e.Offset + e.RemovalLength <= occurrence.Segment.EndOffset
                        || DescendsFrom(target.Parent, element.Number)
                        && occurrence.Segment.Offset <= e.Offset && occurrence.Segment.EndOffset >= e.Offset + e.RemovalLength;
                    occurrence.Start.MovementType = ownsEdit ? AnchorMovementType.BeforeInsertion : AnchorMovementType.AfterInsertion;
                    occurrence.End.MovementType = ownsEdit || occurrence.Segment.Length == 0
                        ? AnchorMovementType.AfterInsertion : AnchorMovementType.BeforeInsertion;
                }
            }
        }

        private bool DescendsFrom(int? parent, int number) => parent is { } value &&
            (value == number || Elements.FirstOrDefault(element => element.Number == value) is { } enclosing
                && enclosing.Parents.Any(ancestor => DescendsFrom(ancestor, number)));

        private void TextChanged(object? sender, EventArgs e)
        {
            if (_copying) return;
            _copying = true;
            try
            {
                foreach (var element in Elements.Where(element => element.IsEditable).OrderByDescending(element => element.Depth))
                {
                    var value = _area.Document.GetText(element.Segment);
                    foreach (var mirror in element.Occurrences.Skip(1).OrderByDescending(occurrence => occurrence.Segment.Offset))
                        if (_area.Document.GetText(mirror.Segment) != value)
                        {
                            _copyTarget = mirror;
                            _area.Document.Replace(mirror.Segment.Offset, mirror.Segment.Length, value);
                        }
                }
            }
            finally { _copyTarget = null; _copying = false; }
        }

        public sealed class Element : IActiveElement
        {
            private readonly NestedLinks _owner;
            public int Number { get; }
            public int[] Parents { get; }
            public int Depth => Parents.Select(parent => _owner.Elements.FirstOrDefault(element => element.Number == parent))
                .OfType<Element>().Select(enclosing => 1 + enclosing.Depth).DefaultIfEmpty(0).Max();
            public string[]? Choices { get; }
            public sealed record Occurrence(TextAnchor Start, TextAnchor End, int? Parent)
            {
                public AnchorSegment Segment { get; } = new(Start, End);
            }
            public List<Occurrence> Occurrences { get; }
            public bool IsEditable { get; set; } = true;
            public ISegment Segment => Occurrences[0].Segment;

            public Element(NestedLinks owner, Stop[] stops, Stop[] allStops, int offset)
            {
                _owner = owner;
                // A field containing children must be the editable source, rather than an earlier $n copy.
                var primary = stops.FirstOrDefault(HasChildren) ?? stops[0];
                Number = primary.Number;
                Parents = stops.Where(stop => stop.Parent is not null).Select(stop => stop.Parent!.Value).Distinct().ToArray();
                Choices = stops.FirstOrDefault(stop => stop.Choices is not null)?.Choices;
                Occurrences = stops.OrderBy(stop => ReferenceEquals(stop, primary) ? 0 : 1).Select(stop =>
                {
                    var start = owner._area.Document.CreateAnchor(offset + stop.Offset);
                    start.MovementType = AnchorMovementType.BeforeInsertion;
                    start.SurviveDeletion = true;
                    var end = owner._area.Document.CreateAnchor(offset + stop.Offset + stop.Length);
                    end.MovementType = AnchorMovementType.AfterInsertion;
                    end.SurviveDeletion = true;
                    return new Occurrence(start, end, stop.Parent);
                }).ToList();

                bool HasChildren(Stop stop) => allStops.Any(child => child.Parent == stop.Number
                    && child.Offset >= stop.Offset && child.Offset + child.Length <= stop.Offset + stop.Length);
            }

            public void OnInsertionCompleted() => _owner.Attach();
            public void Deactivate(SnippetEventArgs e) => _owner.Detach();
        }
    }

    // AvaloniaEdit provides linked editing, but its default handler visits buffer order and wraps on Tab.
    private sealed class Navigation(TextArea area, InsertionContext context, TextAnchor final, Func<int, string[]?> choices) : TextAreaStackedInputHandler(area)
    {
        private int _index;
        private IActiveElement? _current;
        private CompletionWindow? _choices;
        public CompletionWindow? ChoiceWindow => _choices;
        private IActiveElement[] Elements => context.ActiveElements.Where(element => element.IsEditable).ToArray();

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
            _current = Elements[_index];
            var segment = _current.Segment;
            TextArea.Selection = Selection.Create(TextArea, segment);
            TextArea.Caret.Offset = segment.EndOffset;
            if (choices(_index) is { } values && TopLevel.GetTopLevel(TextArea) is not null)
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
                _index = Array.IndexOf(Elements, _current!);
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _index = Math.Max(0, _index - 1);
                else _index++;
                if (_index < Elements.Length) Select();
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
