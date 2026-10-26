using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using Avalonia.Threading;
using Dock.Model.Mvvm.Controls;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp.Messages;
using PropertyChanged.SourceGenerator;
using HexIDE.Utils;

namespace HexIDE.Tools;

public partial class OutlineToolViewModel : Tool, IDisposable
{
    private readonly IDocumentDockService documents;
    private IOutlineDocument? editor;
    private readonly ILocalizationService localization;
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(400) };
    [Notify] private IReadOnlyList<OutlineNode> roots = [];
    [Notify] private OutlineNode? selectedNode;
    [Notify] private OutlineKind? selectedKind;
    public IReadOnlyList<OutlineKind> Kinds { get; private set; } = [];

    public OutlineToolViewModel(ILocalizationService localization, IDocumentDockService documents)
    {
        this.documents = documents;
        this.localization = localization;
        localization.BindTitle(this, "Str.Tool.Outline.Title");
        CanPin = false;
        CanClose = true;
        documents.PropertyChanged += DocumentChanged;
        localization.LanguageChanged += Rebuild;
        refresh.Tick += (_, _) =>
        {
            refresh.Stop();
            if (editor is { } active) active.RefreshSymbolsAsync().ListenErrors();
        };
        Attach();
        Rebuild();
    }

    private void DocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IDocumentDockService.ActiveDocument) or null) Attach();
    }

    public void Dispose()
    {
        refresh.Stop();
        documents.PropertyChanged -= DocumentChanged;
        localization.LanguageChanged -= Rebuild;
        Detach();
    }

    private void Detach()
    {
        if (editor is null) return;
        editor.PropertyChanged -= EditorChanged;
        editor.Document.TextChanged -= TextChanged;
        editor.LanguageServerStateChanged -= ScheduleRefresh;
        editor = null;
    }

    private void Attach()
    {
        if (ReferenceEquals(editor, documents.ActiveDocument)) return;
        refresh.Stop();
        Detach();
        editor = documents.ActiveDocument as IOutlineDocument;
        if (editor is not null)
        {
            editor.PropertyChanged += EditorChanged;
            editor.Document.TextChanged += TextChanged;
            editor.LanguageServerStateChanged += ScheduleRefresh;
            ScheduleRefresh();
        }
        Rebuild();
    }

    private void TextChanged(object? sender, EventArgs e) => ScheduleRefresh();
    private void ScheduleRefresh() { refresh.Stop(); refresh.Start(); }
    private void EditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CodeEditorViewModel.OutlineSymbols)) Rebuild();
        if (e.PropertyName == nameof(CodeEditorViewModel.CaretOffset)) FollowCaret();
    }

    private void Rebuild()
    {
        var symbols = editor?.OutlineSymbols ?? [];
        var kind = SelectedKind?.Kind;
        Kinds = new[] { new OutlineKind(null, localization.GetString("Str.Tool.Outline.AllKinds")) }
            .Concat(symbols.SelectMany(s => s.Flatten()).Select(s => s.Kind).Distinct().OrderBy(k => k)
                .Select(k => new OutlineKind(k, localization.GetString("Str.Tool.Outline.Kind." + k)))).ToArray();
        OnPropertyChanged(nameof(Kinds));
        SelectedKind = Kinds.FirstOrDefault(k => k.Kind == kind) ?? Kinds[0];
        Roots = Build(symbols, SelectedKind?.Kind);
        FollowCaret();
    }

    private void OnSelectedKindChanged(OutlineKind? oldValue, OutlineKind? newValue)
    {
        Roots = Build(editor?.OutlineSymbols ?? [], newValue?.Kind);
        FollowCaret();
    }

    internal static IReadOnlyList<OutlineNode> Build(IEnumerable<DocumentSymbol> symbols, SymbolKind? kind, int depth = 0)
    {
        if (depth >= 64) return [];
        return symbols.Select(s => new OutlineNode(s, Build(s.Children ?? [], kind, depth + 1)))
            .Where(n => kind is null || n.Symbol.Kind == kind || n.Children.Count > 0).ToArray();
    }

    private void FollowCaret()
    {
        if (editor is null) { SelectedNode = null; return; }
        var location = editor.Document.GetLocation(Math.Clamp(editor.CaretOffset, 0, editor.Document.TextLength));
        SelectedNode = FindAt(Roots, new Position(location.Line - 1, location.Column - 1));
    }

    internal static OutlineNode? FindAt(IReadOnlyList<OutlineNode> nodes, Position position)
    {
        foreach (var node in nodes)
        {
            var range = node.Symbol.Range;
            if (Compare(position, range.Start) < 0 || Compare(position, range.End) >= 0) continue;
            var child = FindAt(node.Children, position);
            if (child is not null) node.IsExpanded = true;
            return child ?? node;
        }
        return null;
    }

    private static int Compare(Position a, Position b) => a.Line == b.Line
        ? a.Character.CompareTo(b.Character) : a.Line.CompareTo(b.Line);

    public void Navigate(OutlineNode node)
    {
        if (editor is null) return;
        editor.NavigateToSymbol(node.Symbol);
    }
}

public record OutlineKind(SymbolKind? Kind, string Name);
public partial class OutlineNode(DocumentSymbol symbol, IReadOnlyList<OutlineNode> children)
{
    public DocumentSymbol Symbol { get; } = symbol;
    public IReadOnlyList<OutlineNode> Children { get; } = children;
    public string Name => Symbol.Name;
    [Notify] private bool isExpanded = true;
}
