using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.Lsp.Messages;
using Avalonia.Data.Converters;
using AvaloniaEdit.Document;
using HexIDE.Utils;
using HexIDE.IDE;
using HexIDE.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Forms.ViewModels;

public static class FindDirectionConverters
{
    public static readonly IValueConverter IsUp = new DirectionConverter(FindDirection.Up);
    public static readonly IValueConverter IsDown = new DirectionConverter(FindDirection.Down);

    private class DirectionConverter(FindDirection target) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is FindDirection d && d == target;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is true ? target : AvaloniaData.UnsetValue;

        private static class AvaloniaData
        {
            public static readonly object UnsetValue = Avalonia.Data.BindingOperations.DoNothing;
        }
    }
}

/// <summary>
/// How far a search reaches.
/// </summary>
public enum FindScope
{
    CurrentModule,
    AllOpenDocuments,
    CurrentProcedure,
    CurrentProject,
    SelectedText,
    AllProjects
}

public enum FindDirection
{
    Down,
    Up
}

public partial class FindReplaceViewModel : ObservableObject, IDialog
{
    private readonly IWindowManager _windowManager;
    private readonly IDocumentDockService _documentDockService;
    private readonly ILocalizationService _localization;
    private readonly FindProjectDocuments? _projects;
    private ISearchableDocument? _boundedDocument;
    private (int start, int end)? _bounds;
    private ISearchableDocument? _selectionDocument;
    private TextAnchor? _selectionStart;
    private TextAnchor? _selectionEnd;
    private bool _busy;
    private List<ISearchableDocument>? _scopeDocuments;

    public string Title => ShowReplace
        ? _localization.GetString("Str.FindReplace.Msg.TitleReplace")
        : _localization.GetString("Str.FindReplace.Msg.TitleFind");
    public bool CanResize => false;
    public event Action<bool>? CloseRequested;

    [Notify] private string searchText = "";
    [Notify] private string replaceText = "";
    [Notify] private bool showReplace;
    [Notify] private bool matchCase;
    [Notify] private bool wholeWordOnly;
    [Notify] private bool usePatternMatching;
    [Notify] private FindScope scope = FindScope.CurrentProject;
    [Notify] private FindDirection direction = FindDirection.Down;

    public DelegateCommand FindNextCommand { get; }
    public DelegateCommand ReplaceOneCommand { get; }
    public DelegateCommand ReplaceAllCommand { get; }
    public DelegateCommand CancelCommand { get; }

    private FindScope[] AvailableScopes => _projects?.HasGroup == true
        ? [FindScope.CurrentProcedure, FindScope.CurrentModule, FindScope.CurrentProject,
            FindScope.SelectedText, FindScope.AllOpenDocuments, FindScope.AllProjects]
        : [FindScope.CurrentProcedure, FindScope.CurrentModule, FindScope.CurrentProject,
            FindScope.SelectedText, FindScope.AllOpenDocuments];

    public string[] ScopeItems => AvailableScopes.Select(item => item switch
    {
        FindScope.CurrentProject when _projects?.CurrentProject is { } project =>
            string.Format(_localization.GetString("Str.FindReplace.Msg.ScopeCurrentProject"), project.Name),
        FindScope.CurrentProject => _localization.GetString("Str.FindReplace.Msg.ScopeCurrentProjectUnnamed"),
        _ => _localization.GetString("Str.FindReplace.Msg.Scope" + item)
    }).ToArray();

    public int SelectedScopeIndex
    {
        get => Array.IndexOf(AvailableScopes, Scope);
        set { if (value >= 0 && value < AvailableScopes.Length) Scope = AvailableScopes[value]; }
    }

    public FindReplaceViewModel(IWindowManager windowManager, IDocumentDockService documentDockService,
        ILocalizationService localization) : this(windowManager, documentDockService, localization, null) { }

    internal FindReplaceViewModel(IWindowManager windowManager, IDocumentDockService documentDockService,
        ILocalizationService localization, FindProjectDocuments? projects)
    {
        _windowManager = windowManager;
        _documentDockService = documentDockService;
        _localization = localization;
        _projects = projects;

        FindNextCommand = new DelegateCommand(() => RunAsync(ExecuteFindNext).ListenErrors(), () => SearchText.Length > 0);
        ReplaceOneCommand = new DelegateCommand(() => RunAsync(ExecuteReplaceOne).ListenErrors(), () => SearchText.Length > 0);
        ReplaceAllCommand = new DelegateCommand(() => RunAsync(ExecuteReplaceAllAsync).ListenErrors(), () => SearchText.Length > 0);
        CancelCommand = new DelegateCommand(() => CloseRequested?.Invoke(false), () => true);
    }

    internal void RefreshScopes()
    {
        if (Scope == FindScope.AllProjects && _projects?.HasGroup != true) Scope = FindScope.CurrentProject;
        OnPropertyChanged(nameof(ScopeItems));
        OnPropertyChanged(nameof(SelectedScopeIndex));
    }

    private void OnScopeChanged()
    {
        _selectionDocument = null;
        OnPropertyChanged(nameof(SelectedScopeIndex));
    }

    internal Task FindNextAsync() => RunAsync(ExecuteFindNext);
    internal Task ReplaceAllAsync() => RunAsync(ExecuteReplaceAllAsync);
    internal Task ReplaceOneAsync() => RunAsync(ExecuteReplaceOne);

    private async Task RunAsync(Action action)
    {
        await RunAsync(() => { action(); return Task.CompletedTask; });
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            _scopeDocuments = Scope is FindScope.CurrentProject or FindScope.AllProjects
                ? _projects?.GetDocuments(Scope == FindScope.AllProjects) : null;
            _boundedDocument = GetActiveEditor();
            _bounds = null;
            if (_boundedDocument is { } active)
            {
                if (Scope == FindScope.SelectedText)
                {
                    if (!ReferenceEquals(_selectionDocument, active))
                    {
                        _selectionDocument = active;
                        _selectionStart = active.Document.CreateAnchor(active.SelectionStart);
                        _selectionEnd = active.Document.CreateAnchor(active.SelectionStart + active.SelectionLength);
                        _selectionStart.SurviveDeletion = _selectionEnd.SurviveDeletion = true;
                        _selectionStart.MovementType = AnchorMovementType.BeforeInsertion;
                        _selectionEnd.MovementType = AnchorMovementType.AfterInsertion;
                    }
                    _bounds = (_selectionStart!.Offset, _selectionEnd!.Offset);
                }
                else if (Scope is FindScope.CurrentProcedure or FindScope.CurrentModule)
                {
                    var version = active.Document.Version;
                    var symbols = await active.RequestSearchSymbolsAsync();
                    if (!ReferenceEquals(version, active.Document.Version)
                        || !ReferenceEquals(active, _documentDockService.ActiveDocument)) return;
                    var kinds = Scope == FindScope.CurrentProcedure
                        ? new[] { SymbolKind.Method, SymbolKind.Function, SymbolKind.Property, SymbolKind.Constructor }
                        : new[] { SymbolKind.Module, SymbolKind.Namespace };
                    _bounds = symbols.SelectMany(symbol => symbol.Flatten())
                        .Where(symbol => kinds.Contains(symbol.Kind))
                        .Select(symbol => SearchBounds(active.Document, symbol))
                        .Where(range => range is { } span && span.start <= active.CaretOffset && active.CaretOffset <= span.end)
                        .OrderBy(range => range!.Value.end - range.Value.start).FirstOrDefault();
                }
            }
            await action();
        }
        finally { _busy = false; }
    }

    private static (int start, int end)? SearchBounds(TextDocument document, DocumentSymbol symbol)
    {
        var start = symbol.Range.Start;
        var end = symbol.Range.End;
        if (start.Line < 0 || end.Line < start.Line || end.Line >= document.LineCount) return null;
        var first = document.GetLineByNumber(start.Line + 1);
        var last = document.GetLineByNumber(end.Line + 1);
        if (start.Character < 0 || start.Character > first.Length || end.Character < 0 || end.Character > last.Length) return null;
        return (first.Offset + start.Character, last.Offset + end.Character);
    }

    private void OnSearchTextChanged()
    {
        FindNextCommand.RaiseCanExecutedChanged();
        ReplaceOneCommand.RaiseCanExecutedChanged();
        ReplaceAllCommand.RaiseCanExecutedChanged();
    }

    private void OnShowReplaceChanged()
    {
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>
    /// The active document, if it is one Find can search.
    /// </summary>
    /// <remarks>
    /// Asks for the <em>capability</em> rather than for <c>CodeEditorViewModel</c> by name. The old cast
    /// made "searchable" mean "VB6 code window", which refused the carried-file editor — a genuine text
    /// editor with a genuine buffer — as firmly as it refused the form designer.
    /// </remarks>
    private ISearchableDocument? GetActiveEditor()
    {
        var active = _documentDockService.ActiveDocument as ISearchableDocument;
        return _scopeDocuments is { } documents
            ? active is not null && documents.Contains(active) ? active : documents.FirstOrDefault()
            : active;
    }

    /// <summary>
    /// Says so, instead of returning.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other way this dialog can fail already speaks — "was not found", "invalid regular
    /// expression". Only the one case that is not the user's fault was mute, and silence there is worse
    /// than useless: a user who presses Find Next on the Object Browser and sees nothing happen cannot
    /// distinguish "this surface has no text to search" from "your term is not in the file", so they
    /// blame the term (hexide-io/HexIDE#363).
    /// </para>
    /// <para>
    /// Reachable at all only because the dialog is modeless. Edit ▸ Find, Replace and F3 are disabled
    /// when nothing searchable is active — VB6's own behaviour — so the dialog cannot be <em>opened</em>
    /// here; but one already open survives a switch to another tab, and its buttons are the path this
    /// covers.
    /// </para>
    /// </remarks>
    private void ReportNothingToSearch()
    {
        _windowManager.MessageBox(
            _localization.GetString("Str.FindReplace.Msg.NothingToSearch"),
            Title,
            MessageBoxButtons.Ok,
            MessageBoxIcon.Information);
    }

    /// <summary>
    /// The other documents the current scope reaches, in the order a search should visit them: onwards
    /// from the active one and round, or backwards from it when searching Up. The active document itself
    /// is not in the list — the caller has already searched it from the caret.
    /// </summary>
    private List<ISearchableDocument> OtherDocumentsInScope(ISearchableDocument active)
    {
        if (Scope is FindScope.CurrentModule or FindScope.CurrentProcedure or FindScope.SelectedText) return [];

        var all = Scope is FindScope.CurrentProject or FindScope.AllProjects
            ? _scopeDocuments ?? []
            : _documentDockService.OpenDocuments.OfType<ISearchableDocument>().ToList();
        if (!all.Contains(active)) all.Insert(0, active);

        var index = all.IndexOf(active);
        if (index < 0) return [];

        var ordered = new List<ISearchableDocument>(Math.Max(all.Count - 1, 0));
        for (var step = 1; step < all.Count; step++)
        {
            var i = Direction == FindDirection.Down
                ? (index + step) % all.Count
                : ((index - step) % all.Count + all.Count) % all.Count;
            ordered.Add(all[i]);
        }

        return ordered;
    }

    /// <summary>
    /// The next match anywhere in scope, and which document holds it.
    /// </summary>
    /// <remarks>
    /// The active document is searched first and, under a multi-document scope, <b>without wrapping</b> —
    /// a wrap there would return to the top of this document before ever reaching the next one, which is
    /// how "All Open Documents" would end up meaning "current module" with extra steps. The wrap happens
    /// once, at the end, after every other document has been exhausted.
    /// </remarks>
    private (ISearchableDocument document, int offset, int length)? FindInScope(ISearchableDocument active)
    {
        var single = Scope is FindScope.CurrentModule or FindScope.CurrentProcedure or FindScope.SelectedText;

        var here = FindNext(active.Document, active.CaretOffset, active.SelectionLength, allowWrap: single,
            active.IsReadOnlyRegion);
        if (here is not null) return (active, here.Value.offset, here.Value.length);
        if (single) return null;

        foreach (var other in OtherDocumentsInScope(active))
        {
            // Entered at the end for an upward search, so the LAST match in that document is the first
            // one an Up search meets — the same order the user would see stepping through by hand.
            var from = Direction == FindDirection.Down ? 0 : other.Document.TextLength;
            var found = FindNext(other.Document, from, 0, allowWrap: false, other.IsReadOnlyRegion);
            if (found is not null) return (other, found.Value.offset, found.Value.length);
        }

        var wrapped = FindNext(active.Document, active.CaretOffset, active.SelectionLength, allowWrap: true,
            active.IsReadOnlyRegion);
        return wrapped is null ? null : (active, wrapped.Value.offset, wrapped.Value.length);
    }

    /// <summary>Shows the match, bringing its document to the front first when it is not the active one.</summary>
    private void SelectMatch(ISearchableDocument target, int offset, int length)
    {
        if (target is FindProjectDocuments.UnopenedDocument unopened)
        {
            target = unopened.Open()!;
            if (target is null) return;
        }
        if (!ReferenceEquals(target, _documentDockService.ActiveDocument)
            && target is BaseEditorWindowViewModel window)
        {
            // Activated BEFORE the selection is written, so the view is the front tab by the time it
            // scrolls the match into view.
            _documentDockService.TryActivate<BaseEditorWindowViewModel>(d => ReferenceEquals(d, window));
        }

        target.SelectionStart = offset;
        target.SelectionLength = length;
        target.CaretOffset = Direction == FindDirection.Down ? offset + length : offset;
    }

    private void ExecuteFindNext()
    {
        var editor = GetActiveEditor();
        if (editor is null)
        {
            ReportNothingToSearch();
            return;
        }

        var result = FindInScope(editor);
        if (result is null)
        {
            _windowManager.MessageBox(
                string.Format(_localization.GetString(ProtectedMatchesInScope(editor) > 0
                    ? "Str.FindReplace.Msg.ProtectedOnly" : "Str.FindReplace.Msg.NotFound"), SearchText),
                _localization.GetString("Str.FindReplace.Msg.TitleFind"),
                MessageBoxButtons.Ok,
                MessageBoxIcon.Information);
            return;
        }

        SelectMatch(result.Value.document, result.Value.offset, result.Value.length);
    }

    private void ExecuteReplaceOne()
    {
        var editor = GetActiveEditor();
        if (editor is null)
        {
            ReportNothingToSearch();
            return;
        }

        // If current selection matches the search, replace it
        if (editor.SelectionLength > 0)
        {
            var selectedText = editor.Document.GetText(editor.SelectionStart, editor.SelectionLength);
            // Never inside the header or a member's attribute lines (#273 task 3.9): VB6 never searched the
            // header, because it was not in the code window, and a match there is text only the IDE changes.
            if (!editor.IsReadOnlyDocument && InBounds(editor.Document, editor.SelectionStart, editor.SelectionLength) && IsMatch(selectedText) && !editor.IsReadOnlyRegion(editor.SelectionStart, editor.SelectionLength))
            {
                var span = ReplaceText.Length == 0
                    ? editor.WidenDeletion(editor.SelectionStart, editor.SelectionLength)
                    : (editor.SelectionStart, editor.SelectionLength);
                editor.Document.Replace(span.Item1, span.Item2, ReplaceText);
                if (Scope == FindScope.SelectedText)
                    _bounds = (_selectionStart!.Offset, _selectionEnd!.Offset);
                else if (_bounds is { } bounds)
                    _bounds = (bounds.start, bounds.end + ReplaceText.Length - span.Item2);
                editor.CaretOffset = editor.SelectionStart + ReplaceText.Length;
                editor.SelectionLength = 0;
            }
        }

        // Find next
        ExecuteFindNext();
    }

    private async Task ExecuteReplaceAllAsync()
    {
        var editor = GetActiveEditor();
        if (editor is null)
        {
            ReportNothingToSearch();
            return;
        }

        var documents = new List<ISearchableDocument> { editor };
        documents.AddRange(OtherDocumentsInScope(editor));
        var matching = documents.Where(document => FindPrevious(document.Document, document.Document.TextLength,
            document.IsReadOnlyRegion) is not null).ToList();
        if (matching.Count > 1 && await _windowManager.MessageBox(
                _localization.GetString("Str.FindReplace.Msg.ConfirmAcrossFiles"), Title,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != MessageBoxResult.Yes) return;

        var skipped = documents.Sum(ProtectedMatchesIn);
        var count = 0;
        foreach (var document in matching)
        {
            var target = document is FindProjectDocuments.UnopenedDocument unopened ? unopened.Open() : document;
            if (target is not null) count += ReplaceAllIn(target);
        }

        await _windowManager.MessageBox(
            skipped > 0
                ? string.Format(_localization.GetString("Str.FindReplace.Msg.ReplacementsWithSkipped"), count, skipped)
                : count > 0
                ? string.Format(_localization.GetString("Str.FindReplace.Msg.ReplacementsMade"), count)
                : string.Format(_localization.GetString("Str.FindReplace.Msg.NotFound"), SearchText),
            _localization.GetString("Str.FindReplace.Msg.TitleReplace"),
            MessageBoxButtons.Ok,
            MessageBoxIcon.Information);
    }

    private int ProtectedMatchesInScope(ISearchableDocument active)
    {
        var count = ProtectedMatchesIn(active);
        foreach (var other in OtherDocumentsInScope(active))
            count += ProtectedMatchesIn(other);
        return count;
    }

    private int ProtectedMatchesIn(ISearchableDocument target)
    {
        // Count before any replacement can move the regions or introduce new matches.
        Func<int, int, bool> isReadOnly = target.IsReadOnlyDocument
            ? target.IsReadOnlyRegion
            : target.SnapshotReadOnlyRegions();
        var count = 0;
        var pos = target.Document.TextLength;
        while (pos > 0)
        {
            var match = FindPrevious(target.Document, pos, (offset, length) => !isReadOnly(offset, length));
            if (match is null) break;
            count++;
            pos = match.Value.offset;
        }
        return count;
    }

    private int ReplaceAllIn(ISearchableDocument target)
    {
        if (target.IsReadOnlyDocument) return 0;
        var doc = target.Document;
        var count = 0;

        doc.BeginUpdate();
        try
        {
            // Search from end to start to preserve offsets. A match inside the header or a member's attribute
            // lines is never found, so never replaced (#273 tasks 3.9 and 3.11): replacing every Command1 in a
            // form's code must not rename the control in its designer block, which only the IDE changes. The
            // regions are found once, and stay true because nothing before a replacement moves.
            var isReadOnly = target.SnapshotReadOnlyRegions();
            int pos = doc.TextLength;
            while (pos > 0)
            {
                var result = FindPrevious(doc, pos, isReadOnly);
                if (result is null) break;

                pos = result.Value.offset;
                var span = ReplaceText.Length == 0
                    ? target.WidenDeletion(result.Value.offset, result.Value.length)
                    : (result.Value.offset, result.Value.length);
                doc.Replace(span.Item1, span.Item2, ReplaceText);
                count++;
            }
        }
        finally
        {
            doc.EndUpdate();
        }

        return count;
    }

    /// <param name="allowWrap">
    /// Whether an unsuccessful scan may start again from the other end of this document. False while a
    /// multi-document search is still working through the other documents — see <see cref="FindInScope"/>.
    /// </param>
    /// <param name="isReadOnly">The document's read-only regions; see <see cref="Accepts"/>.</param>
    private (int offset, int length)? FindNext(TextDocument doc, int startOffset, int currentSelectionLength,
        bool allowWrap, Func<int, int, bool> isReadOnly)
    {
        if (SearchText.Length == 0) return null;

        if (UsePatternMatching)
            return FindNextRegex(doc, startOffset, currentSelectionLength, allowWrap, isReadOnly);

        var comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        int searchStart;
        if (Direction == FindDirection.Down)
        {
            // Start after current selection to avoid re-finding same match
            searchStart = startOffset;
            if (currentSelectionLength > 0)
                searchStart = Math.Min(startOffset + 1, doc.TextLength);

            // Search forward, skipping matches Accepts refuses
            var pos = searchStart;
            while (pos < doc.TextLength)
            {
                var idx = doc.IndexOf(SearchText, pos, doc.TextLength - pos, comparison);
                if (idx < 0) break;
                if (Accepts(doc, idx, SearchText.Length, isReadOnly))
                    return (idx, SearchText.Length);
                pos = idx + 1;
            }

            // Wrap around from start
            if (allowWrap && searchStart > 0)
            {
                pos = 0;
                while (pos < searchStart)
                {
                    var idx = doc.IndexOf(SearchText, pos, Math.Min(searchStart + SearchText.Length, doc.TextLength) - pos, comparison);
                    if (idx < 0 || idx >= searchStart) break;
                    if (Accepts(doc, idx, SearchText.Length, isReadOnly))
                        return (idx, SearchText.Length);
                    pos = idx + 1;
                }
            }
        }
        else
        {
            searchStart = startOffset - 1;
            var text = doc.Text;
            for (int i = searchStart; i >= 0; i--)
            {
                if (i + SearchText.Length > text.Length) continue;
                var candidate = text.Substring(i, SearchText.Length);
                if (string.Equals(candidate, SearchText, comparison) && Accepts(doc, i, SearchText.Length, isReadOnly))
                    return (i, SearchText.Length);
            }
            // Wrap around from end
            if (allowWrap)
            {
                for (int i = text.Length - SearchText.Length; i > searchStart; i--)
                {
                    var candidate = text.Substring(i, SearchText.Length);
                    if (string.Equals(candidate, SearchText, comparison) && Accepts(doc, i, SearchText.Length, isReadOnly))
                        return (i, SearchText.Length);
                }
            }
        }

        return null;
    }

    private (int offset, int length)? FindNextRegex(TextDocument doc, int startOffset, int currentSelectionLength,
        bool allowWrap, Func<int, int, bool> isReadOnly)
    {
        var options = MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
        Regex regex;
        try
        {
            regex = new Regex(SearchText, options);
        }
        catch (ArgumentException)
        {
            _windowManager.MessageBox(
                _localization.GetString("Str.FindReplace.Msg.InvalidRegex"),
                _localization.GetString("Str.FindReplace.Msg.TitleFind"),
                MessageBoxButtons.Ok,
                MessageBoxIcon.Warning);
            return null;
        }

        var text = doc.Text;
        int searchFrom = Direction == FindDirection.Down
            ? (currentSelectionLength > 0 ? Math.Min(startOffset + 1, text.Length) : startOffset)
            : 0;

        if (Direction == FindDirection.Down)
        {
            var match = FirstAccepted(regex, text, searchFrom, doc, isReadOnly, before: text.Length + 1);
            if (match.Success)
                return (match.Index, match.Length);

            // Wrap
            if (allowWrap && searchFrom > 0)
            {
                match = FirstAccepted(regex, text, 0, doc, isReadOnly, before: searchFrom);
                if (match.Success)
                    return (match.Index, match.Length);
            }
        }
        else
        {
            // For reverse, collect every match Accepts takes and find the one before startOffset
            var matches = AcceptedMatches(regex, text, doc, isReadOnly);
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                if (matches[i].Index < startOffset)
                    return (matches[i].Index, matches[i].Length);
            }
            // Wrap
            if (allowWrap)
            {
                for (int i = matches.Count - 1; i >= 0; i--)
                {
                    if (matches[i].Index >= startOffset)
                        return (matches[i].Index, matches[i].Length);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The first match at or after <paramref name="from"/> that <see cref="Accepts"/> takes and that starts
    /// before <paramref name="before"/>, or an unsuccessful match.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Walked, not taken.</b> The first match may be one Accepts refuses, and the one the developer wants
    /// can still be further on. Taking only the first used to report "not found" whenever it failed Whole
    /// Word, and would do so whenever it sat in the header.
    /// </para>
    /// <para>
    /// <b>A refused match is stepped past by one character, not by its length.</b> <c>NextMatch</c> resumes
    /// at the end of the match it was given, so a refused match hides any match overlapping it. A pattern
    /// such as <c>\s*Private Sub</c> first matches from the header's last line break, which is refused, and
    /// <c>NextMatch</c> then resumed after <c>Private Sub</c> and never saw it. The plain scan has always
    /// stepped by one, and this is the same stepping.
    /// </para>
    /// </remarks>
    private Match FirstAccepted(Regex regex, string text, int from, TextDocument doc, Func<int, int, bool> isReadOnly,
        int before)
    {
        var match = regex.Match(text, from);
        while (match.Success && match.Index < before && !Accepts(doc, match.Index, match.Length, isReadOnly))
            match = StepPast(regex, text, match);
        return match.Success && match.Index < before ? match : Match.Empty;
    }

    /// <summary>
    /// Every match <see cref="Accepts"/> takes, in order: a taken match is followed by the next that does not
    /// overlap it, and a refused one by any that starts after its first character.
    /// </summary>
    /// <remarks>See <see cref="FirstAccepted"/> for why a refused match is stepped past by one character.</remarks>
    private List<Match> AcceptedMatches(Regex regex, string text, TextDocument doc, Func<int, int, bool> isReadOnly)
    {
        var accepted = new List<Match>();
        var match = regex.Match(text, 0);
        while (match.Success)
        {
            if (Accepts(doc, match.Index, match.Length, isReadOnly))
            {
                accepted.Add(match);
                match = match.NextMatch();
            }
            else
            {
                match = StepPast(regex, text, match);
            }
        }
        return accepted;
    }

    private static Match StepPast(Regex regex, string text, Match refused) =>
        refused.Index < text.Length ? regex.Match(text, refused.Index + 1) : Match.Empty;

    private (int offset, int length)? FindPrevious(TextDocument doc, int beforeOffset, Func<int, int, bool> isReadOnly)
    {
        if (SearchText.Length == 0) return null;

        if (UsePatternMatching)
        {
            var options = MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
            try
            {
                var regex = new Regex(SearchText, options);
                var matches = AcceptedMatches(regex, doc.Text, doc, isReadOnly);
                for (int i = matches.Count - 1; i >= 0; i--)
                {
                    if (matches[i].Index < beforeOffset)
                        return (matches[i].Index, matches[i].Length);
                }
            }
            catch (ArgumentException)
            {
                return null;
            }
            return null;
        }

        var comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var text = doc.Text;
        for (int i = Math.Min(beforeOffset - 1, text.Length - SearchText.Length); i >= 0; i--)
        {
            if (i + SearchText.Length > text.Length) continue;
            var candidate = text.Substring(i, SearchText.Length);
            if (string.Equals(candidate, SearchText, comparison) && Accepts(doc, i, SearchText.Length, isReadOnly))
                return (i, SearchText.Length);
        }

        return null;
    }

    /// <summary>
    /// Whether a match of the search text at <paramref name="offset"/> is one to report: a whole word when
    /// Whole Word is on, and not in a read-only region.
    /// </summary>
    /// <remarks>
    /// <b>One test at every place a match is yielded</b>: forward, backward, both wraps, plain and pattern,
    /// and Replace All's backward walk. A region is the header or a member's attribute lines (#273 task
    /// 3.11). VB6's Find never searched the header, because the header was not in its code window, and a
    /// search that stopped there now would land on text the developer cannot change, often inside a fold.
    /// A form held read-only as a whole is not a region, so Find still searches all of it.
    /// </remarks>
    private bool Accepts(TextDocument doc, int offset, int length, Func<int, int, bool> isReadOnly) =>
        InBounds(doc, offset, length) && CheckWholeWord(doc, offset, length) && !isReadOnly(offset, length);

    private bool InBounds(TextDocument doc, int offset, int length) =>
        _bounds is not { } bounds || !ReferenceEquals(doc, _boundedDocument?.Document)
        || (offset >= bounds.start && offset + length <= bounds.end);

    private bool CheckWholeWord(TextDocument doc, int offset, int length)
    {
        if (!WholeWordOnly) return true;
        var text = doc.Text;
        if (offset > 0 && char.IsLetterOrDigit(text[offset - 1])) return false;
        int end = offset + length;
        if (end < text.Length && char.IsLetterOrDigit(text[end])) return false;
        return true;
    }

    private bool IsMatch(string text)
    {
        if (UsePatternMatching)
        {
            var options = MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
            try
            {
                return Regex.IsMatch(text, $"^{SearchText}$", options);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        var comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return string.Equals(text, SearchText, comparison);
    }
}
