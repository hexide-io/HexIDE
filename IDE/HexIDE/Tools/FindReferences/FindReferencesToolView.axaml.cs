using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Controls.ProportionalStackPanel;
using Dock.Model.Core;
using Dock.Model.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace HexIDE.Tools;

public partial class FindReferencesToolView : UserControl
{
    public FindReferencesToolView()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(KeyDownEvent, OnKeyDown, handledEventsToo: true);
        LayoutUpdated += (_, _) => EnsureUsableHeight();
    }

    private bool heightPending = true;
    private FindReferencesToolViewModel? subscribed;

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (subscribed is not null) subscribed.ShowRequested -= RequestHeight;
        base.OnDataContextChanged(e);
        subscribed = DataContext as FindReferencesToolViewModel;
        if (subscribed is not null) subscribed.ShowRequested += RequestHeight;
        RequestHeight();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (subscribed is not null)
        {
            subscribed.ShowRequested -= RequestHeight;
            subscribed.ShowRequested += RequestHeight;
        }
        RequestHeight();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (subscribed is not null) subscribed.ShowRequested -= RequestHeight;
        base.OnDetachedFromVisualTree(e);
    }

    private void RequestHeight()
    {
        heightPending = true;
        InvalidateMeasure();
    }

    private void EnsureUsableHeight()
    {
        if (!heightPending || Content is not ListBox list || list.ContainerFromIndex(0) is not ListBoxItem row
            || !(row.Bounds.Height > 0)) return;
        var dock = this.GetVisualAncestors().OfType<ToolDockControl>()
            .Select(c => c.DataContext).OfType<IToolDock>().FirstOrDefault();
        if (dock?.Owner is not IProportionalDock { Orientation: Orientation.Vertical } parent) return;
        var document = parent.VisibleDockables?.FirstOrDefault(ContainsDocument);
        if (document is null || ReferenceEquals(document, dock)) return;
        dock.GetVisibleBounds(out _, out _, out _, out var height);
        document.GetVisibleBounds(out _, out _, out _, out var documentHeight);
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (!(height > 0) || !(documentHeight > 0) || scroll is null) return;
        var growth = Math.Min(Math.Max(0, Math.Ceiling(6 * row.Bounds.Height - scroll.Viewport.Height)),
            Math.Max(0, documentHeight - DocumentMinimum(document)));
        heightPending = false;
        if (growth <= 0) return;
        var total = parent.VisibleDockables!.Where(d => d is not IProportionalDockSplitter).Sum(d =>
        {
            d.GetVisibleBounds(out _, out _, out _, out var h);
            return h;
        });
        var panel = this.GetVisualAncestors().OfType<ProportionalStackPanel>()
            .FirstOrDefault(p => p.Children.Any(c => ReferenceEquals(c.DataContext, dock)));
        if (panel is null) return;
        Dispatcher.UIThread.Post(() =>
        {
            dock.Proportion = (height + growth) / total;
            document.Proportion = (documentHeight - growth) / total;
            // Dock's local attached proportions take precedence over the model bindings.
            foreach (var child in panel.Children)
                if (ReferenceEquals(child.DataContext, dock))
                    child.SetCurrentValue(ProportionalStackPanel.ProportionProperty, dock.Proportion);
                else if (ReferenceEquals(child.DataContext, document))
                    child.SetCurrentValue(ProportionalStackPanel.ProportionProperty, document.Proportion);
            subscribed?.NotifyLayoutResized();
        });
    }

    private static bool ContainsDocument(IDockable item) => item is IDocumentDock
        || item is IDock dock && dock.VisibleDockables?.Any(ContainsDocument) == true;

    private static double DocumentMinimum(IDockable item) => Math.Max(
        Math.Max(item is IDocumentDock ? MainViewViewModel.DockFactory.MinimumDocumentHeight : 0, double.IsFinite(item.MinHeight) ? item.MinHeight : 0),
        item is IDock dock ? dock.VisibleDockables?.Select(DocumentMinimum).DefaultIfEmpty(0).Max() ?? 0 : 0);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is FindReferencesToolViewModel vm)
            e.Handled = vm.NavigateSelected();
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is FindReferencesToolViewModel vm) vm.NavigateSelected();
    }
}
