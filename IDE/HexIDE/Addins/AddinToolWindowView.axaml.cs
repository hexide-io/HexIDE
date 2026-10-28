using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Controls.ProportionalStackPanel;
using Dock.Model.Core;
using Dock.Model.Controls;

namespace HexIDE.Addins;

public partial class AddinToolWindowView : UserControl
{
    public AddinToolWindowView()
    {
        InitializeComponent();
        LayoutUpdated += (_, _) => EnsureUsableWidth();
    }

    private void EnsureUsableWidth()
    {
        if (DataContext is not AddinToolWindowViewModel { Content: IToolWindowMinimumWidth minimum } tool)
            return;
        var dock = this.GetVisualAncestors().OfType<ToolDockControl>()
            .Select(c => c.DataContext).OfType<IToolDock>().FirstOrDefault();
        if (dock is null || tool.SizedDocks.Contains(dock)) return;

        if (tool.KeepSavedWidth)
        {
            tool.KeepSavedWidth = false;
            tool.SizedDocks.Add(dock);
            return;
        }

        // A vertically stacked tool grows its whole column, borrowing only the document column's slack.
        IDockable column = dock;
        while (column.Owner is IProportionalDock { Orientation: Orientation.Vertical } vertical)
            column = vertical;
        if (column.Owner is not IProportionalDock { Orientation: Orientation.Horizontal } parent) return;
        var document = parent.VisibleDockables?.FirstOrDefault(ContainsDocument);
        if (document is null || ReferenceEquals(document, column)) return;
        column.GetVisibleBounds(out _, out _, out var width, out _);
        document.GetVisibleBounds(out _, out _, out var documentWidth, out _);
        if (!(width > 0) || !(documentWidth > 0)) return;

        var required = minimum.MeasureMinimumUsableWidth() + Math.Max(0, width - Bounds.Width);
        if (!double.IsFinite(required) || required <= 0) return;
        tool.SizedDocks.Add(dock);
        var growth = Math.Min(Math.Max(0, required - width), Math.Max(0, documentWidth - DocumentMinimum(document)));
        if (growth <= 0) return;
        var total = parent.VisibleDockables!.Where(d => d is not IProportionalDockSplitter).Sum(d =>
        {
            d.GetVisibleBounds(out _, out _, out var w, out _);
            return w;
        });
        var panel = this.GetVisualAncestors().OfType<ProportionalStackPanel>()
            .FirstOrDefault(p => p.Children.Any(c => ReferenceEquals(c.DataContext, column)));
        if (panel is null) return;
        Dispatcher.UIThread.Post(() =>
        {
            column.Proportion = (width + growth) / total;
            document.Proportion = (documentWidth - growth) / total;
            // Dock's panel assigns local attached values, which take precedence over its model bindings.
            foreach (var child in panel.Children)
                if (ReferenceEquals(child.DataContext, column))
                    child.SetCurrentValue(ProportionalStackPanel.ProportionProperty, column.Proportion);
                else if (ReferenceEquals(child.DataContext, document))
                    child.SetCurrentValue(ProportionalStackPanel.ProportionProperty, document.Proportion);
        });
    }

    private static bool ContainsDocument(IDockable item) => item is IDocumentDock
        || item is IDock dock && dock.VisibleDockables?.Any(ContainsDocument) == true;

    private static double DocumentMinimum(IDockable item) => Math.Max(
        double.IsFinite(item.MinWidth) ? item.MinWidth : 0,
        item is IDock dock ? dock.VisibleDockables?.Select(DocumentMinimum).DefaultIfEmpty(0).Max() ?? 0 : 0);

    // An addin tool window's Content is a single, stateful Control (created once by the addin's factory).
    // Dock re-materialises this view when the tool window is dragged to a new dock, so two views can briefly
    // want the same control — and a control can only have one visual parent. A declarative
    // ContentControl.Content bind crashed there ("already has a visual parent"). Instead we host the control
    // ourselves and DETACH it from any prior parent first, so a re-host just relocates the (stateful) control.

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        HostContent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        HostContent();
    }

    private void HostContent()
    {
        var host = this.FindControl<Border>("PART_Host");
        if (host is null) return;

        if (DataContext is not AddinToolWindowViewModel { Content: Control control })
        {
            host.Child = null;
            return;
        }

        if (ReferenceEquals(host.Child, control))
            return; // already hosting it

        Detach(control);
        host.Child = control;
    }

    /// <summary>Remove a control from whatever parent currently holds it, so it can be re-hosted without
    /// hitting Avalonia's "already has a visual parent" check.</summary>
    private static void Detach(Control control)
    {
        switch (control.GetVisualParent())
        {
            case Border b when ReferenceEquals(b.Child, control):
                b.Child = null;
                break;
            case Decorator d when ReferenceEquals(d.Child, control):
                d.Child = null;
                break;
            case ContentPresenter cp when ReferenceEquals(cp.Content, control):
                cp.Content = null;
                break;
            case ContentControl cc when ReferenceEquals(cc.Content, control):
                cc.Content = null;
                break;
            case Panel p:
                p.Children.Remove(control);
                break;
        }
    }
}
