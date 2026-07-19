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

public enum FindScope
{
    CurrentModule,
    CurrentProject,
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
            _localization.GetString("Str.FindReplace.Msg.ScopeCurrentProject"),
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

    private CodeEditorViewModel? GetActiveEditor()
    {
        return _documentDockService.ActiveDocument as CodeEditorViewModel;
    }

    private void ExecuteFindNext()
    {
        var editor = GetActiveEditor();
        if (editor is null) return;

        var result = FindNext(editor.Document, editor.CaretOffset, editor.SelectionLength);
        if (result is null)
        {
            _windowManager.MessageBox(
                string.Format(_localization.GetString("Str.FindReplace.Msg.NotFound"), SearchText),
                _localization.GetString("Str.FindReplace.Msg.TitleFind"),
                MessageBoxButtons.Ok,
                MessageBoxIcon.Information);
            return;
        }

        editor.SelectionStart = result.Value.offset;
        editor.SelectionLength = result.Value.length;
        editor.CaretOffset = Direction == FindDirection.Down
            ? result.Value.offset + result.Value.length
            : result.Value.offset;
    }

    private void ExecuteReplaceOne()
    {
        var editor = GetActiveEditor();
        if (editor is null) return;

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
        if (editor is null) return;

        var doc = editor.Document;
        int count = 0;

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

        _windowManager.MessageBox(
            count > 0
                ? string.Format(_localization.GetString("Str.FindReplace.Msg.ReplacementsMade"), count)
                : string.Format(_localization.GetString("Str.FindReplace.Msg.NotFound"), SearchText),
            _localization.GetString("Str.FindReplace.Msg.TitleReplace"),
            MessageBoxButtons.Ok,
            MessageBoxIcon.Information);
    }

    private (int offset, int length)? FindNext(TextDocument doc, int startOffset, int currentSelectionLength)
    {
        if (SearchText.Length == 0) return null;

        if (UsePatternMatching)
            return FindNextRegex(doc, startOffset, currentSelectionLength);

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
            if (searchStart > 0)
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
            for (int i = text.Length - SearchText.Length; i > searchStart; i--)
            {
                var candidate = text.Substring(i, SearchText.Length);
                if (string.Equals(candidate, SearchText, comparison) && CheckWholeWord(doc, i, SearchText.Length))
                    return (i, SearchText.Length);
            }
        }

        return null;
    }

    private (int offset, int length)? FindNextRegex(TextDocument doc, int startOffset, int currentSelectionLength)
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
            if (searchFrom > 0)
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
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                if (matches[i].Index >= startOffset &&
                    (!WholeWordOnly || CheckWholeWord(doc, matches[i].Index, matches[i].Length)))
                    return (matches[i].Index, matches[i].Length);
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
