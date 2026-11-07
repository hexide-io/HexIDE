using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Reactive;
using AvaloniaEdit;
using HexIDE.Controls;
using HexIDE.Forms.ViewModels;

namespace HexIDE.Forms.Views;

public partial class ArchiveDocumentView : UserControl
{
    private ArchiveDocumentViewModel? model;
    private bool syncing;
    private readonly InactiveCodeColorizer inactive;
    private IDisposable? inactiveBrush;
    public ArchiveDocumentView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => BindModel();
        var editor = this.FindControl<TextEditor>("Editor")!;
        editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (!syncing && model != null) model.CaretOffset = editor.CaretOffset;
        };
        // A .twin entry's inactive #If branches, greyed as the code window greys them (#1691).
        inactive = new InactiveCodeColorizer(() => model?.InactiveLines ?? []);
        editor.TextArea.TextView.LineTransformers.Add(inactive);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BindModel();
        var view = this.FindControl<TextEditor>("Editor")!.TextArea.TextView;
        inactiveBrush = this.GetResourceObservable("InactiveCodeTextBrush").Subscribe(new AnonymousObserver<object?>(value =>
        {
            inactive.Brush = value as IBrush;
            view.Redraw();
        }));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        inactiveBrush?.Dispose();
        inactiveBrush = null;
        if (model != null) model.PropertyChanged -= SyncSelection;
        model = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void BindModel()
    {
        if (model != null) model.PropertyChanged -= SyncSelection;
        model = DataContext as ArchiveDocumentViewModel;
        if (model != null) model.PropertyChanged += SyncSelection;
    }

    private void SyncSelection(object? sender, PropertyChangedEventArgs e)
    {
        if (model == null || syncing) return;
        var editor = this.FindControl<TextEditor>("Editor")!;
        if (e.PropertyName == nameof(model.InactiveLines))
        {
            editor.TextArea.TextView.Redraw();
            return;
        }
        syncing = true;
        try
        {
            if (e.PropertyName is nameof(model.SelectionStart) or nameof(model.SelectionLength))
            {
                var start = Math.Clamp(model.SelectionStart, 0, editor.Document.TextLength);
                editor.Select(start, Math.Clamp(model.SelectionLength, 0, editor.Document.TextLength - start));
                editor.TextArea.Caret.BringCaretToView();
            }
            else if (e.PropertyName == nameof(model.CaretOffset))
                editor.CaretOffset = Math.Clamp(model.CaretOffset, 0, editor.Document.TextLength);
        }
        finally { syncing = false; }
    }
}
