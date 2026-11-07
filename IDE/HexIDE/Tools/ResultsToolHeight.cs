using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Controls.ProportionalStackPanel;
using Dock.Model.Core;
using Dock.Model.Controls;

namespace HexIDE.Tools;

internal sealed class ResultsToolHeight
{
    private const int RoundingAllowancePixels = 3;
    private readonly UserControl view;

    public ResultsToolHeight(UserControl view)
    {
        this.view = view;
        view.DataContextChanged += (_, _) => Subscribe();
        view.AttachedToVisualTree += (_, _) => Subscribe();
        view.DetachedFromVisualTree += (_, _) =>
        {
            if (subscribed is not null) subscribed.ShowRequested -= RequestHeight;
        };
        view.LayoutUpdated += (_, _) =>
        {
            if (!heightPending || adjustmentPosted
                || view.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault(c => c is ListBox or TreeView)
                    ?.ContainerFromIndex(0) is not { Bounds.Height: > 0 }) return;
            adjustmentPosted = true;
            Dispatcher.UIThread.Post(() =>
            {
                adjustmentPosted = false;
                if (!heightPending) return;
                heightPending = false;
                EnsureUsableHeight();
            }, DispatcherPriority.Background);
        };
    }

    private bool heightPending;
    private bool adjustmentPosted;
    private ResultsToolViewModel? subscribed;

    private void Subscribe()
    {
        if (subscribed is not null) subscribed.ShowRequested -= RequestHeight;
        subscribed = view.DataContext as ResultsToolViewModel;
        if (subscribed is not null) subscribed.ShowRequested += RequestHeight;
        RequestHeight();
    }

    private void RequestHeight()
    {
        heightPending = true;
        view.InvalidateMeasure();
    }

    private void EnsureUsableHeight()
    {
        if (view.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault(c => c is ListBox or TreeView) is not { } list
            || list.ContainerFromIndex(0) is not { } row
            || !(row.Bounds.Height > 0)) return;
        var dock = view.GetVisualAncestors().OfType<ToolDockControl>()
            .Select(c => c.DataContext).OfType<IToolDock>().FirstOrDefault();
        if (dock?.Owner is not IProportionalDock { Orientation: Orientation.Vertical } parent) return;
        var document = parent.VisibleDockables?.FirstOrDefault(ContainsDocument);
        if (document is null || ReferenceEquals(document, dock)) return;
        dock.GetVisibleBounds(out _, out _, out _, out var height);
        document.GetVisibleBounds(out _, out _, out _, out var documentHeight);
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (!(height > 0) || !(documentHeight > 0) || scroll is null) return;
        var needed = Math.Max(0, 6 * row.Bounds.Height - scroll.Viewport.Height);
        if (needed <= 0) return;
        var minimum = Math.Max(MainViewViewModel.DockFactory.MinimumDocumentHeight,
            double.IsFinite(document.MinHeight) ? document.MinHeight : 0);
        var panel = view.GetVisualAncestors().OfType<ProportionalStackPanel>()
            .FirstOrDefault(p => p.Children.Any(c => ReferenceEquals(c.DataContext, dock)));
        if (panel is null) return;
        var available = panel.Bounds.Height - panel.Children
            .Where(c => c.DataContext is IProportionalDockSplitter).Sum(c => c.Bounds.Height);
        if (!(available > 0)) return;
        var units = parent.VisibleDockables!.Where(d => d is not IProportionalDockSplitter)
            .Sum(d => d.Proportion) / available;
        // Panel, tool chrome and list viewport each round to device pixels.
        var pixel = 1 / (TopLevel.GetTopLevel(view)?.RenderScaling ?? 1);
        var delta = Math.Min((needed + RoundingAllowancePixels * pixel) * units,
            Math.Max(0, document.Proportion - (minimum + pixel) * units));
        if (!(delta > 0)) return;
        var resultProportion = dock.Proportion + delta;
        var documentProportion = document.Proportion - delta;
        dock.Proportion = resultProportion;
        document.Proportion = documentProportion;
        // Dock's local attached proportions take precedence over the model bindings.
        foreach (var child in panel.Children)
            if (ReferenceEquals(child.DataContext, dock))
                child.SetCurrentValue(ProportionalStackPanel.ProportionProperty, resultProportion);
            else if (ReferenceEquals(child.DataContext, document))
                child.SetCurrentValue(ProportionalStackPanel.ProportionProperty, documentProportion);
        subscribed?.NotifyLayoutResized();
    }

    private static bool ContainsDocument(IDockable item) => item is IDocumentDock
        || item is IDock dock && dock.VisibleDockables?.Any(ContainsDocument) == true;
}
