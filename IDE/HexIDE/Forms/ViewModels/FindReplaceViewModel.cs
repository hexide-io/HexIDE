using System;
using System.Globalization;
using System.Text.RegularExpressions;
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
/// <remarks>
/// <para>
/// <b>There used to be a third member, <c>CurrentProject</c>, and it was removed rather than implemented.</b>
/// Every one of the three was offered by the dialog and read by nothing — a control that takes a choice and
/// ignores it, documented as a gap twice over and drawn for another year each time (hexide-io/HexIDE#363).
/// So each was made to mean something or taken away, and they did not come out the same.
/// </para>
/// <para>
/// <see cref="AllOpenDocuments"/> is answerable from what the dock already holds: every open document, each
/// with a live buffer. <c>CurrentProject</c> is not. A project's modules are mostly <em>not</em> open, and
/// their text lives in <c>ModuleDefinition.Code</c> / <c>FormDefinition.Code</c> — which the editor writes
/// back only on save and on close. Searching that would read the last-saved text of every module the user
/// currently has open and edited, and report a hit at an offset that no longer exists: a wrong answer
/// delivered confidently, which this project ranks below a clean refusal. Doing it properly needs either a
/// project-wide index or a flush of every editor before every keystroke of a search, and that is a
/// capability, not a bug fix — hexide-io/HexIDE#372.
/// </para>
/// </remarks>
public enum FindScope
{
    CurrentModule,
    AllOpenDocuments
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
    [Notify] private FindScope scope = FindScope.CurrentModule;
    [Notify] private FindDirection direction = FindDirection.Down;

    public DelegateCommand FindNextCommand { get; }
    public DelegateCommand ReplaceOneCommand { get; }
    public DelegateCommand ReplaceAllCommand { get; }
    public DelegateCommand CancelCommand { get; }

    // Scope items for the combo box
    public string[] ScopeItems { get; }

    public int SelectedScopeIndex
    {
        get => (int)Scope;
        set => Scope = (FindScope)value;
    }

    public FindReplaceViewModel(IWindowManager windowManager, IDocumentDockService documentDockService, ILocalizationService localization)
    {
        _windowManager = windowManager;
        _documentDockService = documentDockService;
        _localization = localization;

        ScopeItems =
        [
            _localization.GetString("Str.FindReplace.Msg.ScopeCurrentModule"),
            _localization.GetString("Str.FindReplace.Msg.ScopeAllOpenDocuments")
        ];

        FindNextCommand = new DelegateCommand(ExecuteFindNext, () => SearchText.Length > 0);
        ReplaceOneCommand = new DelegateCommand(ExecuteReplaceOne, () => SearchText.Length > 0);
        ReplaceAllCommand = new DelegateCommand(ExecuteReplaceAll, () => SearchText.Length > 0);
        CancelCommand = new DelegateCommand(() => CloseRequested?.Invoke(false), () => true);
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
        return _documentDockService.ActiveDocument as ISearchableDocument;
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
        if (Scope == FindScope.CurrentModule) return [];

        var all = new List<ISearchableDocument>();
        foreach (var open in _documentDockService.OpenDocuments)
        {
            if (open is ISearchableDocument searchable)
                all.Add(searchable);
        }

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
        var single = Scope == FindScope.CurrentModule;

        var here = FindNext(active.Document, active.CaretOffset, active.SelectionLength, allowWrap: single);
        if (here is not null) return (active, here.Value.offset, here.Value.length);
        if (single) return null;

        foreach (var other in OtherDocumentsInScope(active))
        {
            // Entered at the end for an upward search, so the LAST match in that document is the first
            // one an Up search meets — the same order the user would see stepping through by hand.
            var from = Direction == FindDirection.Down ? 0 : other.Document.TextLength;
            var found = FindNext(other.Document, from, 0, allowWrap: false);
            if (found is not null) return (other, found.Value.offset, found.Value.length);
        }

        var wrapped = FindNext(active.Document, active.CaretOffset, active.SelectionLength, allowWrap: true);
        return wrapped is null ? null : (active, wrapped.Value.offset, wrapped.Value.length);
    }

    /// <summary>Shows the match, bringing its document to the front first when it is not the active one.</summary>
    private void SelectMatch(ISearchableDocument target, int offset, int length)
    {
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
                string.Format(_localization.GetString("Str.FindReplace.Msg.NotFound"), SearchText),
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
            if (IsMatch(selectedText))
            {
                editor.Document.Replace(editor.SelectionStart, editor.SelectionLength, ReplaceText);
                editor.CaretOffset = editor.SelectionStart + ReplaceText.Length;
                editor.SelectionLength = 0;
            }
        }

        // Find next
        ExecuteFindNext();
    }

    private void ExecuteReplaceAll()
    {
        var editor = GetActiveEditor();
        if (editor is null)
        {
            ReportNothingToSearch();
            return;
        }

        // Scope means the same thing to Replace All as it does to Find. Honouring it in one and not the
        // other would leave the combo telling the truth in Find mode and lying in Replace mode, which is
        // the defect this change exists to end rather than to halve.
        var count = ReplaceAllIn(editor.Document);
        foreach (var other in OtherDocumentsInScope(editor))
            count += ReplaceAllIn(other.Document);

        _windowManager.MessageBox(
            count > 0
                ? string.Format(_localization.GetString("Str.FindReplace.Msg.ReplacementsMade"), count)
                : string.Format(_localization.GetString("Str.FindReplace.Msg.NotFound"), SearchText),
            _localization.GetString("Str.FindReplace.Msg.TitleReplace"),
            MessageBoxButtons.Ok,
            MessageBoxIcon.Information);
    }

    private int ReplaceAllIn(TextDocument doc)
    {
        var count = 0;

        doc.BeginUpdate();
        try
        {
            // Search from end to start to preserve offsets
            int pos = doc.TextLength;
            while (pos > 0)
            {
                var result = FindPrevious(doc, pos);
                if (result is null) break;

                doc.Replace(result.Value.offset, result.Value.length, ReplaceText);
                pos = result.Value.offset;
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
    private (int offset, int length)? FindNext(TextDocument doc, int startOffset, int currentSelectionLength,
        bool allowWrap)
    {
        if (SearchText.Length == 0) return null;

        if (UsePatternMatching)
            return FindNextRegex(doc, startOffset, currentSelectionLength, allowWrap);

        var comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        int searchStart;
        if (Direction == FindDirection.Down)
        {
            // Start after current selection to avoid re-finding same match
            searchStart = startOffset;
            if (currentSelectionLength > 0)
                searchStart = Math.Min(startOffset + 1, doc.TextLength);

            // Search forward, skipping non-whole-word matches
            var pos = searchStart;
            while (pos < doc.TextLength)
            {
                var idx = doc.IndexOf(SearchText, pos, doc.TextLength - pos, comparison);
                if (idx < 0) break;
                if (CheckWholeWord(doc, idx, SearchText.Length))
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
                    if (CheckWholeWord(doc, idx, SearchText.Length))
                        return (idx, SearchText.Length);
                    pos = idx + 1;
                }
            }
        }
        else
        {
            searchStart = startOffset > 0 ? startOffset - 1 : doc.TextLength - 1;
            var text = doc.Text;
            for (int i = searchStart; i >= 0; i--)
            {
                if (i + SearchText.Length > text.Length) continue;
                var candidate = text.Substring(i, SearchText.Length);
                if (string.Equals(candidate, SearchText, comparison) && CheckWholeWord(doc, i, SearchText.Length))
                    return (i, SearchText.Length);
            }
            // Wrap around from end
            if (allowWrap)
            {
                for (int i = text.Length - SearchText.Length; i > searchStart; i--)
                {
                    var candidate = text.Substring(i, SearchText.Length);
                    if (string.Equals(candidate, SearchText, comparison) && CheckWholeWord(doc, i, SearchText.Length))
                        return (i, SearchText.Length);
                }
            }
        }

        return null;
    }

    private (int offset, int length)? FindNextRegex(TextDocument doc, int startOffset, int currentSelectionLength,
        bool allowWrap)
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
            var match = regex.Match(text, searchFrom);
            if (match.Success && (!WholeWordOnly || CheckWholeWord(doc, match.Index, match.Length)))
                return (match.Index, match.Length);

            // Wrap
            if (allowWrap && searchFrom > 0)
            {
                match = regex.Match(text, 0);
                if (match.Success && match.Index < searchFrom &&
                    (!WholeWordOnly || CheckWholeWord(doc, match.Index, match.Length)))
                    return (match.Index, match.Length);
            }
        }
        else
        {
            // For reverse, collect all matches and find the one before startOffset
            var matches = regex.Matches(text);
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                if (matches[i].Index < startOffset &&
                    (!WholeWordOnly || CheckWholeWord(doc, matches[i].Index, matches[i].Length)))
                    return (matches[i].Index, matches[i].Length);
            }
            // Wrap
            if (allowWrap)
            {
                for (int i = matches.Count - 1; i >= 0; i--)
                {
                    if (matches[i].Index >= startOffset &&
                        (!WholeWordOnly || CheckWholeWord(doc, matches[i].Index, matches[i].Length)))
                        return (matches[i].Index, matches[i].Length);
                }
            }
        }

        return null;
    }

    private (int offset, int length)? FindPrevious(TextDocument doc, int beforeOffset)
    {
        if (SearchText.Length == 0) return null;

        if (UsePatternMatching)
        {
            var options = MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
            try
            {
                var regex = new Regex(SearchText, options);
                var matches = regex.Matches(doc.Text);
                for (int i = matches.Count - 1; i >= 0; i--)
                {
                    if (matches[i].Index < beforeOffset &&
                        (!WholeWordOnly || CheckWholeWord(doc, matches[i].Index, matches[i].Length)))
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
            if (string.Equals(candidate, SearchText, comparison) && CheckWholeWord(doc, i, SearchText.Length))
                return (i, SearchText.Length);
        }

        return null;
    }

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
