using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;

namespace HexIDE.Controls;

/// <summary>
/// The designer's canvas: the controls it models, and beside them the boxes of the controls it carries as text (#157), so
/// that both stack by one ZIndex and a box goes between the controls VB6 stacks it between (#1553).
/// </summary>
/// <remarks>
/// <para>
/// The boxes were a layer of their own over the controls, because one inside a Frame or PictureBox is drawn on that
/// container and beneath it would be painted over by the container's background. Over every control, a control brought in
/// front of a carried one was saved in front of it (#1498) and still drawn under its box.
/// </para>
/// <para>
/// The controls are the items of <see cref="ControlsContainer"/>, which this panel hosts, and a box is no item: it is not
/// selected, moved or written. So each box's presenter is a child this panel keeps itself, in its visual and logical
/// children but not in <see cref="Panel.Children"/>, which the items own. <see cref="Panel"/> keeps its visual children
/// in step with <see cref="Panel.Children"/> by index, so a box's presenter always goes after every item's container.
/// What is drawn on top is decided by ZIndex, which the item style and <see cref="OverlayTemplate"/>'s presenter style bind.
/// </para>
/// </remarks>
public class DesignerCanvas : Canvas
{
    public static readonly StyledProperty<IEnumerable?> OverlaysProperty =
        AvaloniaProperty.Register<DesignerCanvas, IEnumerable?>(nameof(Overlays));

    public static readonly StyledProperty<IDataTemplate?> OverlayTemplateProperty =
        AvaloniaProperty.Register<DesignerCanvas, IDataTemplate?>(nameof(OverlayTemplate));

    /// <summary>The class each box's presenter has, for the style that places and stacks it.</summary>
    public const string OverlayClass = "carried";

    private readonly List<ContentPresenter> overlays = [];
    private INotifyCollectionChanged? watched;

    /// <summary>What the boxes show, one presenter each.</summary>
    public IEnumerable? Overlays
    {
        get => GetValue(OverlaysProperty);
        set => SetValue(OverlaysProperty, value);
    }

    /// <summary>The template each box is drawn with.</summary>
    public IDataTemplate? OverlayTemplate
    {
        get => GetValue(OverlayTemplateProperty);
        set => SetValue(OverlayTemplateProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == OverlaysProperty || change.Property == OverlayTemplateProperty)
            Rebuild();
    }

    // The designer's view is reused across its tabs, so the list is replaced as well as changed: let go of the old one.
    private void Rebuild()
    {
        if (watched is not null)
            watched.CollectionChanged -= OnOverlaysChanged;
        watched = Overlays as INotifyCollectionChanged;
        if (watched is not null)
            watched.CollectionChanged += OnOverlaysChanged;

        foreach (var presenter in overlays)
        {
            VisualChildren.Remove(presenter);
            LogicalChildren.Remove(presenter);
        }
        overlays.Clear();
        if (Overlays is not null)
            foreach (var item in Overlays)
                Add(item);
        InvalidateMeasure();
    }

    private void OnOverlaysChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Add(object? item)
    {
        var presenter = new ContentPresenter { Content = item, DataContext = item, ContentTemplate = OverlayTemplate };
        presenter.Classes.Add(OverlayClass);
        overlays.Add(presenter);
        // Logical, so the template's resources and styles reach it; visual, after every item's container, to draw it.
        LogicalChildren.Add(presenter);
        VisualChildren.Add(presenter);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        foreach (var presenter in overlays)
            presenter.Measure(Size.Infinity);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        foreach (var presenter in overlays)
        {
            var left = GetLeft(presenter);
            var top = GetTop(presenter);
            presenter.Arrange(new Rect(new Point(double.IsNaN(left) ? 0 : left, double.IsNaN(top) ? 0 : top), presenter.DesiredSize));
        }
        return size;
    }
}
