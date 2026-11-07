using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using HexIDE.Lsp.Messages;
using HexIDE.Utils;

namespace HexIDE.Forms.Views;

public partial class CodeEditorView
{
    public DelegateCommand ExpandSelection { get; }
    public DelegateCommand ShrinkSelection { get; }

    private readonly List<(int Start, int End)> _selectionChain = [];
    private int _selectionLevel;
    private int _selectionGeneration;
    private bool _applyingSelection;
    private int? _selectionRequestGeneration;

    private bool CanChangeSelection() => DataContext is ViewModels.CodeEditorViewModel { SupportsSelectionRanges: true };

    private void ResetSelectionChain()
    {
        if (_applyingSelection) return;
        _selectionGeneration++;
        _selectionChain.Clear();
        _selectionLevel = 0;
    }

    private void ApplySelectionLevel()
    {
        _applyingSelection = true;
        try
        {
            var (start, end) = _selectionChain[_selectionLevel];
            TextEditor.Select(start, end - start);
        }
        finally { _applyingSelection = false; }
    }

    private void ShrinkSelectionCore()
    {
        if (!CanChangeSelection() || _selectionLevel == 0) return;
        _selectionLevel--;
        ApplySelectionLevel();
    }

    private async Task ExpandSelectionAsync()
    {
        if (!CanChangeSelection() || _selectionRequestGeneration == _selectionGeneration || DataContext is not ViewModels.CodeEditorViewModel vm) return;
        if (_selectionChain.Count != 0)
        {
            if (_selectionLevel + 1 < _selectionChain.Count)
            {
                _selectionLevel++;
                ApplySelectionLevel();
            }
            return;
        }

        var document = TextEditor.Document;
        var start = TextEditor.SelectionLength == 0 ? TextEditor.CaretOffset : TextEditor.SelectionStart;
        var end = start + TextEditor.SelectionLength;
        var generation = _selectionGeneration;
        Position At(int offset)
        {
            var location = document.GetLocation(offset);
            return new Position(location.Line - 1, location.Column - 1);
        }
        _selectionRequestGeneration = generation;
        try
        {
            var ranges = await vm.RequestSelectionRangesAsync(start == end ? [At(start)] : [At(start), At(end)]);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation != _selectionGeneration || document != TextEditor.Document || DataContext != vm || !CanChangeSelection()) return;
                var candidates = new List<(int Start, int End)>();
                // Use only ranges supplied by the server, never synthesize a word or a union.
                foreach (var root in ranges)
                {
                    for (var range = (SelectionRange?)root; range is not null; range = range.Parent)
                    {
                        var a = range.Range.Start;
                        var b = range.Range.End;
                        if (a.Line < 0 || b.Line < a.Line || b.Line >= document.LineCount || a.Character < 0 || b.Character < 0) continue;
                        var first = document.GetLineByNumber(a.Line + 1);
                        var last = document.GetLineByNumber(b.Line + 1);
                        if (a.Character > first.Length || b.Character > last.Length) continue;
                        var candidate = (Start: first.Offset + a.Character, End: last.Offset + b.Character);
                        if (candidate.Start <= start && candidate.End >= end)
                            candidates.Add(candidate);
                    }
                }
                candidates.Sort((a, b) => (a.End - a.Start).CompareTo(b.End - b.Start));
                var chain = new List<(int Start, int End)> { (start, end) };
                foreach (var candidate in candidates)
                {
                    var previous = chain[^1];
                    if (candidate.Start <= previous.Start && candidate.End >= previous.End && candidate != previous)
                        chain.Add(candidate);
                }
                if (chain.Count == 1) return;
                _selectionChain.AddRange(chain);
                _selectionLevel = 1;
                ApplySelectionLevel();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Selection ranges: {ex.Message}");
        }
        finally
        {
            if (_selectionRequestGeneration == generation) _selectionRequestGeneration = null;
        }
    }
}
