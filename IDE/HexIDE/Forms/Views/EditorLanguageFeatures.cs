using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using HexIDE.Lsp.Messages;
using HexIDE.Tools.ObjectBrowser;
using Serilog;

namespace HexIDE.Forms.Views;

public interface IEditorDocumentQueries
{
    Task FlushDocumentAsync(CancellationToken ct = default);
    Task<CompletionItem[]> RequestCompletionAsync(Position position, CancellationToken ct = default);
    Task<CompletionItem[]> RequestCompletionForTriggerAsync(Position position, string triggerCharacter, CancellationToken ct = default);
    IReadOnlyList<string> CompletionTriggerCharacters { get; }
    Task<SignatureHelp?> RequestSignatureHelpAsync(Position position, CancellationToken ct = default, string? triggerCharacter = null);
    IReadOnlyList<string> SignatureHelpTriggerCharacters { get; }
    string? DocumentUri { get; }
}

internal interface IEditorCompletionRules
{
    bool MayOpenCompletion();
    bool RefusesTyping(int offset, int length);
    bool Commits(Key key);
    bool HandlesReturn { get; }
    bool CompletionAtWordStart { get; }
    IReadOnlyList<string>? FixedSignatureTriggers { get; }
}

/// <summary>Completion, snippets and signature help attached to one text area.</summary>
internal sealed class EditorLanguageFeatures : IDisposable
{
    private readonly TextArea area;
    private readonly IEditorDocumentQueries queries;
    private readonly IEditorCompletionRules rules;
    private readonly Action? dismissHover;
    private readonly Action<CompletionWindow?>? completionChanged;
    private CompletionWindow? _completionWindow;
    private OverloadInsightWindow? _insightWindow;
    private CancellationTokenSource? _completionCts;
    private CancellationTokenSource? _signatureCts;
    private Task _completionShown = Task.CompletedTask;
    private Task _signatureShown = Task.CompletedTask;
    internal Task PendingRequests => Task.WhenAll(_completionShown, _signatureShown);
    internal IReadOnlyList<Avalonia.Controls.Primitives.Popup> OpenPopups =>
        new Avalonia.Controls.Primitives.Popup?[] { _completionWindow, _insightWindow }
            .Concat(CompletionSnippet.OpenChoiceWindows(area)).Where(p => p is { IsOpen: true }).Select(p => p!).ToList();

    public EditorLanguageFeatures(TextArea area, IEditorDocumentQueries queries, IEditorCompletionRules rules,
        Action? dismissHover = null, Action<CompletionWindow?>? completionChanged = null, bool handleInput = true)
    {
        this.area = area;
        this.queries = queries;
        this.rules = rules;
        this.dismissHover = dismissHover;
        this.completionChanged = completionChanged;
        if (handleInput)
        {
            area.TextEntering += OnTextEntering;
            area.TextEntered += OnTextEntered;
            area.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }
    }

    public void CloseCompletion()
    {
        _completionCts?.Cancel();
        _completionWindow?.Close();
    }

    public void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        if (e.Text is not { Length: > 0 }) return;
        var ch = e.Text[0];
        if (!char.IsLetterOrDigit(ch) && ch != '_') CloseCompletion();
    }

    public void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (e.Text is not { Length: > 0 }) return;
        var ch = e.Text[0];
        if (char.IsLetter(ch)) TriggerCompletion();
        else if (queries.CompletionTriggerCharacters.Contains(e.Text)) TriggerCompletion(e.Text);
        if ((rules.FixedSignatureTriggers ?? queries.SignatureHelpTriggerCharacters).Contains(e.Text))
            TriggerSignatureHelp(e.Text);
        if (rules.FixedSignatureTriggers is not null && ch == ')') CloseInsightWindowIfBalanced();
    }

    public void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!rules.MayOpenCompletion()) CloseCompletion();
        if (e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Return && CompletionSnippet.OpenChoiceWindows(area).FirstOrDefault() is { } choice)
        {
            choice.CompletionList.RequestInsertion(e);
            e.Handled = true;
            return;
        }
        if (_completionWindow is not { } completion || !rules.Commits(e.Key)) return;
        if (e.Key == Key.Tab && completion.CompletionList.SelectedItem is null)
            completion.CompletionList.SelectedItem = completion.CompletionList.CompletionData.FirstOrDefault();
        var hasSelection = completion.CompletionList.SelectedItem is not null;
        if (hasSelection) completion.CompletionList.RequestInsertion(e);
        else completion.Close();
        if (e.Key == Key.Tab || (e.Key == Key.Return && rules.HandlesReturn && hasSelection)) e.Handled = true;
    }

    public void Dispose()
    {
        area.TextEntering -= OnTextEntering;
        area.TextEntered -= OnTextEntered;
        area.RemoveHandler(Avalonia.Input.InputElement.KeyDownEvent, OnKeyDown);
        CloseCompletion();
        _completionCts?.Dispose();
        _signatureCts?.Cancel();
        _signatureCts?.Dispose();
        _insightWindow?.Close();
        CompletionSnippet.Deactivate(area);
    }

    public void TriggerCompletion(string? triggerCharacter = null, bool completeWord = false)
    {
        if (!rules.MayOpenCompletion())
        {
            _completionCts?.Cancel();
            _completionWindow?.Close();
            return;
        }

        _completionCts?.Cancel();
        _completionCts?.Dispose();
        _completionCts = new CancellationTokenSource();
        var token = _completionCts.Token;

        var (start, _) = CompletionWord(area);
        var location = area.Document.GetLocation(rules.CompletionAtWordStart ? start : area.Caret.Offset);
        var lspPos = new Position(location.Line - 1, location.Column - 1);
        _completionShown = ShowCompletionAsync(lspPos, token, triggerCharacter, completeWord);
    }

    private async Task ShowCompletionAsync(Position position, CancellationToken token,
        string? triggerCharacter, bool completeWord)
    {
        try
        {
            // The document change is debounced 300ms, but completion needs the current source.
            await queries.FlushDocumentAsync(token);
            if (token.IsCancellationRequested) return;

            var items = triggerCharacter is null
                ? await queries.RequestCompletionAsync(position, token)
                : await queries.RequestCompletionForTriggerAsync(position, triggerCharacter, token);
            if (token.IsCancellationRequested || items.Length == 0) return;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested || !rules.MayOpenCompletion()) return;

                dismissHover?.Invoke();

                // If a window is already open, close it first
                _completionWindow?.Close();

                if (completeWord)
                {
                    var (start, end) = CompletionWord(area);
                    var word = area.Document.GetText(start, end - start);
                    var matches = items.Where(item => (item.FilterText ?? item.Label)
                        .StartsWith(word, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (word.Length > 0 && matches.Length == 1)
                    {
                        new VbCompletionData(matches[0], rules.RefusesTyping, () => queries.DocumentUri).Complete(area,
                            new TextSegment { StartOffset = start, Length = end - start }, EventArgs.Empty);
                        return;
                    }
                }
                _completionWindow = new CompletionWindow(area);
                completionChanged?.Invoke(_completionWindow);
                FillCompletionList(_completionWindow, items, rules.RefusesTyping, () => queries.DocumentUri);

                _completionWindow.Closed += (_, _) => { _completionWindow = null; completionChanged?.Invoke(null); };
                _completionWindow.Show();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Debug("[completion] {ErrorMessage}", ex.Message);
        }
    }

    internal static void FillCompletionList(CompletionWindow window, CompletionItem[] items,
        Func<int, int, bool> refusesTyping, Func<string?>? documentUri = null)
    {
        var area = window.TextArea;
        var (start, end) = CompletionWord(area);
        window.StartOffset = start;
        window.EndOffset = end;
        window.CloseWhenCaretAtBeginning = false;
        window.CompletionList.IsFiltering = false;
        foreach (var item in items.OrderBy(item => item.SortText ?? item.Label, StringComparer.Ordinal))
            window.CompletionList.CompletionData.Add(new VbCompletionData(item, refusesTyping, documentUri));

        void SelectWord(object? sender, EventArgs e)
        {
            var (wordStart, wordEnd) = CompletionWord(area);
            var word = area.Document.GetText(wordStart, wordEnd - wordStart);
            var matches = window.CompletionList.CompletionData
                .Where(data => word.Length > 0 && data.Text.StartsWith(word, StringComparison.OrdinalIgnoreCase));
            window.CompletionList.SelectedItem = matches.FirstOrDefault(data =>
                items.Any(item => item.Preselect == true && (item.FilterText ?? item.Label) == data.Text))
                ?? matches.FirstOrDefault();
            if (window.CompletionList.SelectedItem is { } selected)
                window.CompletionList.ScrollIntoView(selected);
        }
        // Run after the popup's caret handler, which matches only the text before the caret.
        window.Opened += (_, _) =>
        {
            area.Caret.PositionChanged += SelectWord;
            SelectWord(null, EventArgs.Empty);
        };
        window.Closed += (_, _) => area.Caret.PositionChanged -= SelectWord;
    }

    private static (int Start, int End) CompletionWord(AvaloniaEdit.Editing.TextArea area)
    {
        var start = area.Caret.Offset;
        var end = start;
        while (start > 0 && IsWord(area.Document.GetCharAt(start - 1))) start--;
        while (end < area.Document.TextLength && IsWord(area.Document.GetCharAt(end))) end++;
        return (start, end);

        static bool IsWord(char ch) => char.IsLetterOrDigit(ch) || ch == '_';
    }

    // ── Signature help ───────────────────────────────────────────────────────

    public void TriggerSignatureHelp(string? triggerCharacter = null)
    {

        _signatureCts?.Cancel();
        _signatureCts?.Dispose();
        _signatureCts = new CancellationTokenSource();
        var token = _signatureCts.Token;

        var caret = area.Caret.Position;
        var lspPos = new Position(caret.Line - 1, caret.Column - 1);
        _signatureShown = ShowSignatureHelpAsync(lspPos, token, triggerCharacter);
    }

    private void CloseInsightWindowIfBalanced()
    {
        // Count parens from start of current line to caret; if depth == 0, no open call
        var doc = area.Document;
        var caret = area.Caret;
        var lineObj = doc.GetLineByNumber(caret.Line);
        var lineText = doc.GetText(lineObj.Offset, caret.Offset - lineObj.Offset);
        int depth = 0;
        foreach (var c in lineText)
        {
            if (c == '(') depth++;
            else if (c == ')') depth--;
        }
        if (depth <= 0)
            _insightWindow?.Close();
    }

    private async Task ShowSignatureHelpAsync(Position position, CancellationToken token, string? triggerCharacter)
    {
        try
        {
            // The document change is debounced 300ms, but the server needs the current source
            // to scan for the call context. Flush immediately before the signatureHelp request.
            await queries.FlushDocumentAsync(token);
            if (token.IsCancellationRequested) return;

            var help = await queries.RequestSignatureHelpAsync(position, token, triggerCharacter);
            if (token.IsCancellationRequested) return;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;

                if (help is null || help.Signatures.Length == 0)
                {
                    _insightWindow?.Close();
                    return;
                }

                dismissHover?.Invoke();
                _insightWindow?.Close();
                var provider = new VbSignatureProvider(help);
                _insightWindow = new OverloadInsightWindow(area)
                {
                    Provider = provider
                };
                _insightWindow.Closed += (_, _) => _insightWindow = null;
                _insightWindow.Show();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Debug("[signatureHelp] {ErrorMessage}", ex.Message);
        }
    }

}

/// <summary>Wraps an LSP <see cref="CompletionItem"/> as AvaloniaEdit <see cref="ICompletionData"/>.</summary>
/// <param name="item">The server's completion item.</param>
/// <param name="isReadOnlyRegion">
/// Whether a span of the buffer is one only the IDE may write. A completion commit writes the document
/// directly, so the read-only section provider never sees it (#273 task 3.9).
/// </param>
internal sealed class VbCompletionData(CompletionItem item, Func<int, int, bool> isReadOnlyRegion,
    Func<string?>? documentUri = null) : ICompletionData
{
    public string Text => item.FilterText ?? item.Label;
    public object Content => KindGlyph is { } glyph
        ? new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6,
            Children = { new TextBlock { Text = glyph }, new TextBlock { Text = item.Label } }
        }
        : item.Label;

    private string? KindGlyph => item.Kind switch
    {
        CompletionItemKind.Method or CompletionItemKind.Function or CompletionItemKind.Constructor
            or CompletionItemKind.Operator => OBMemberViewModel.GlyphFor(OBMemberKind.Method),
        CompletionItemKind.Field or CompletionItemKind.Variable or CompletionItemKind.Property
            => OBMemberViewModel.GlyphFor(OBMemberKind.Property),
        CompletionItemKind.Constant or CompletionItemKind.Value => OBMemberViewModel.GlyphFor(OBMemberKind.Constant),
        CompletionItemKind.EnumMember => OBMemberViewModel.GlyphFor(OBMemberKind.EnumMember),
        CompletionItemKind.Event => OBMemberViewModel.GlyphFor(OBMemberKind.Event),
        CompletionItemKind.Module => OBClassViewModel.GlyphFor(OBClassKind.Module),
        CompletionItemKind.Class or CompletionItemKind.Interface or CompletionItemKind.Struct
            => OBClassViewModel.GlyphFor(OBClassKind.ClassModule),
        CompletionItemKind.Enum => OBClassViewModel.GlyphFor(OBClassKind.Enum),
        _ => null
    };

    // What an item reads as to the automation surface, whose ListBoxItem holds this object rather than the text: an item
    // was named by its position alone, and selecting one by its text found none (#942 review).
    public override string ToString() => item.Label;
    public object? Description => item.Documentation is { } documentation
        ? string.IsNullOrEmpty(item.Detail) ? documentation.Value : item.Detail + "\n\n" + documentation.Value
        : item.Detail;
    public double Priority => 0;
    public IImage? Image => null;

    public void Complete(AvaloniaEdit.Editing.TextArea textArea, ISegment completionSegment, EventArgs e)
    {
        var document = textArea.Document;
        var mainOffset = completionSegment.Offset;
        var mainLength = completionSegment.Length;
        var mainText = item.InsertText ?? item.Label;
        if (item.TextEdit is { } main)
        {
            var range = main.Range;
            if (main.Insert is { } insert && main.Replace is { } replace)
            {
                if (range is not null || insert.Start != replace.Start || insert.Start.Line != insert.End.Line
                    || replace.Start.Line != replace.End.Line || insert.End.Character > replace.End.Character
                    || !Resolve(new TextEdit(replace, main.NewText), out _, out _))
                    return;
                range = insert;
            }
            else if (main.Insert is not null || main.Replace is not null) return;
            if (range is null || !Resolve(new TextEdit(range, main.NewText), out mainOffset, out mainLength))
                return;
            mainText = main.NewText;
        }

        string? indentation = null;
        if (item.InsertTextMode == 2)
        {
            var line = document.GetLineByOffset(textArea.Caret.Offset);
            var beforeCaret = document.GetText(line.Offset, textArea.Caret.Offset - line.Offset);
            indentation = new string(beforeCaret.TakeWhile(c => c is ' ' or '\t').ToArray());
        }
        var snippet = item.InsertTextFormat == 2
            ? CompletionSnippet.Parse(mainText, CompletionSnippet.Variables(textArea, documentUri?.Invoke()), indentation) : null;
        if (item.InsertTextFormat == 2 && snippet is null)
            return;
        if (snippet is null && indentation is not null)
            mainText = new CompletionSnippet(mainText, []).AdjustIndentation(indentation).Text;
        mainText = snippet?.Text ?? mainText;
        var edits = new List<(int Offset, int Length, string Text)> { (mainOffset, mainLength, mainText) };
        foreach (var edit in item.AdditionalTextEdits ?? [])
        {
            if (!Resolve(edit, out var offset, out var length))
                return;
            edits.Add((offset, length, edit.NewText));
        }

        // All ranges refer to the original buffer. Refuse the whole item before writing any part of it.
        edits.Sort((left, right) => left.Offset.CompareTo(right.Offset));
        for (var i = 0; i < edits.Count; i++)
        {
            var edit = edits[i];
            if (isReadOnlyRegion(edit.Offset, edit.Length)
                || (i > 0 && (edits[i - 1].Offset + edits[i - 1].Length > edit.Offset
                              || edits[i - 1].Offset == edit.Offset)))
                return;
        }

        var caret = mainOffset + mainText.Length;
        using (document.RunUpdate())
        {
            foreach (var edit in edits.AsEnumerable().Reverse())
            {
                document.Replace(edit.Offset, edit.Length, edit.Text);
                if (edit.Offset < mainOffset)
                    caret += edit.Text.Length - edit.Length;
            }
        }
        textArea.ClearSelection();
        textArea.Caret.Offset = caret;
        snippet?.Activate(textArea, caret - mainText.Length);

        bool Resolve(TextEdit edit, out int offset, out int length)
        {
            offset = length = 0;
            if (edit.Range is not { Start: { } start, End: { } end }
                || start.Line < 0 || end.Line < start.Line || end.Line >= document.LineCount
                || start.Character < 0 || end.Character < 0)
                return false;
            var first = document.GetLineByNumber(start.Line + 1);
            var last = document.GetLineByNumber(end.Line + 1);
            if (start.Character > first.Length || end.Character > last.Length)
                return false;
            offset = first.Offset + start.Character;
            length = last.Offset + end.Character - offset;
            return length >= 0;
        }
    }
}

/// <summary>
/// Implements <see cref="IOverloadProvider"/> over an LSP <see cref="SignatureHelp"/> response
/// for display in AvaloniaEdit's <see cref="OverloadInsightWindow"/>.
/// </summary>
internal sealed class VbSignatureProvider : IOverloadProvider
{
    private readonly SignatureHelp _help;
    private int _selectedIndex;

    public VbSignatureProvider(SignatureHelp help)
    {
        _help = help;
        _selectedIndex = help.ActiveSignature ?? 0;
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _selectedIndex = Math.Clamp(value, 0, _help.Signatures.Length - 1);
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(SelectedIndex)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CurrentHeader)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CurrentContent)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CurrentIndexText)));
        }
    }

    public int Count => _help.Signatures.Length;

    public string CurrentIndexText => $"{_selectedIndex + 1} of {_help.Signatures.Length}";

    public object? CurrentHeader
    {
        get
        {
            var sig = _help.Signatures[_selectedIndex];
            var activeParam = _help.ActiveParameter ?? 0;

            if (sig.Parameters is null || sig.Parameters.Length == 0 || activeParam >= sig.Parameters.Length)
                return sig.Label;

            var label = sig.Label;
            if (sig.Parameters[activeParam].Label.RangeIn(label) is not { } range) return label;
            return new TextBlock
            {
                Inlines =
                [
                    new Run(label[..range.Start]),
                    new Run(label.Substring(range.Start, range.Length)) { FontWeight = FontWeight.Bold },
                    new Run(label[(range.Start + range.Length)..])
                ]
            };
        }
    }

    public object? CurrentContent
    {
        get
        {
            var sig = _help.Signatures[_selectedIndex];
            var activeParam = _help.ActiveParameter ?? 0;
            if (sig.Parameters is not null && activeParam < sig.Parameters.Length)
            {
                var p = sig.Parameters[activeParam];
                if (p.Documentation is not null)
                    return $"{p.Label.TextIn(sig.Label)}: {p.Documentation}";
                return p.Label.TextIn(sig.Label);
            }
            return sig.Documentation;
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
