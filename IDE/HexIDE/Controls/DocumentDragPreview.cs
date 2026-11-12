using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;

namespace HexIDE.Controls;

public static class DocumentDragPreview
{
    public static readonly AttachedProperty<bool> FitStatusProperty =
        AvaloniaProperty.RegisterAttached<Window, bool>("FitStatus", typeof(DocumentDragPreview));

    static DocumentDragPreview() => FitStatusProperty.Changed.AddClassHandler<Window>((window, e) =>
    {
        if (e.GetNewValue<bool>()) window.Opened += OnOpened;
        else window.Opened -= OnOpened;
    });

    public static bool GetFitStatus(Window window) => window.GetValue(FitStatusProperty);
    public static void SetFitStatus(Window window, bool value) => window.SetValue(FitStatusProperty, value);

    private static void OnOpened(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;
        // Dock may reuse the preview window across drags: a tool pane's preview after a document's must not
        // keep the document's minimum width (#3802).
        window.ClearValue(Layoutable.MinWidthProperty);
        if (window.Content is not DragPreviewControl preview || preview.DataContext is not IDocument)
            return;

        // Dock fixes document previews to the tab's width. Measure the title and reserved status
        // together before its first frame, and keep that minimum when Dock freezes the window size.
        preview.ApplyTemplate();
        preview.Measure(Size.Infinity);
        window.MinWidth = preview.DesiredSize.Width;
    }
}
