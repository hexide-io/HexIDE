using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaEdit;
using HexIDE.Forms.ViewModels;

namespace HexIDE.Forms.Views;

public partial class ArchiveDocumentView : UserControl
{
    private ArchiveDocumentViewModel? model;
    private bool syncing;
    public ArchiveDocumentView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => BindModel();
        this.FindControl<TextEditor>("Editor")!.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (!syncing && model != null) model.CaretOffset = this.FindControl<TextEditor>("Editor")!.CaretOffset;
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BindModel();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
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
