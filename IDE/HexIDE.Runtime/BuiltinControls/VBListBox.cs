using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>
/// A VB6 ListBox. Its Click comes from two places, measured with vb6.exe, native and p-code agreeing (the oracle's
/// "A ListBox's Click", #1374):
/// <list type="bullet">
/// <item>A left press on an item raises Click at its release, before MouseUp, whether or not the selection changed and
/// wherever the release lands. The press selects at once, and the selection follows the pointer over the items while
/// the button is held. A left press in the empty area below the items, and any right or middle press, selects nothing
/// and raises no Click.</item>
/// <item>Code or a key that moves the selection raises Click, with ListIndex already the new value. Setting it again,
/// or a key that can go no further, raises none, and nor do AddItem, RemoveItem and Clear.</item>
/// </list>
/// So Click is not raised from a selection change, which those three also make.
/// <para>
/// With MultiSelect 1 (Simple) or 2 (Extended) the list keeps VB6's own rules rather than Avalonia's (the oracle's "A
/// ListBox's Selected, SelCount and Text", #1413). ListIndex is the item with the focus, 0 from the start, and the
/// selection is a set of items apart from it. Simple toggles the item pressed, whatever the modifiers, and its arrow
/// keys move only the focus, with Space toggling. Extended selects the item pressed alone, toggles it with Ctrl, and
/// with Shift selects from the anchor, the last item pressed or reached without Shift. A drag moves the focus, and on an
/// Extended list selects from the anchor too. Every key such a list takes raises Click, whether or not anything changed.
/// </para>
/// </summary>
public class VBListBox : ListBox
{
    protected override Type StyleKeyOverride => typeof(ListBox);

    // Whether the press now held is a left press on an item, which raises Click at its release.
    private bool pressedOnItem;

    private readonly VBMultiSelect multiSelect;
    // A multi-select list's ListIndex: the focused item, which need not be selected.
    private int focusIndex;
    // An Extended list's anchor, which a Shift range runs from.
    private int anchorIndex;
    // A Shift press on an Extended list, whose range comes at the release.
    private bool shiftPressed;
    private HashSet<int>? shiftSelection;
    private int? rangeEnd;
    // The modifiers of the last key down, for a letter's search, which comes with its character after KeyPress.
    private KeyModifiers lastKeyModifiers;

    private int? pointerTop;
    private bool scrollIntoViewOnLayout;
    private bool rowsNeedPadding;
    private readonly HashSet<int> checks = [];
    private int savedStyle;
    private int columns;
    private bool designer;

    internal int SavedStyle { get => savedStyle; init { savedStyle = value; ConfigureLayout(); } }
    internal int Columns { get => columns; init { columns = value; ConfigureLayout(); } }
    internal bool HasChecks => !designer && savedStyle == 1 && !IsMultiSelect;
    internal bool Sorted { get; init; }
    private bool HasColumns => !designer && columns > 0;

    internal void UsePlainDesignerLayout()
    {
        designer = true;
        ClearValue(ItemsPanelProperty);
        ClearValue(ItemTemplateProperty);
        ClearValue(ScrollViewer.HorizontalScrollBarVisibilityProperty);
        ClearValue(ScrollViewer.VerticalScrollBarVisibilityProperty);
    }

    private void ConfigureLayout()
    {
        if (HasChecks)
            ItemTemplate = new FuncDataTemplate<string>((text, _) =>
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = false };
                row.Children.Add(new CheckGlyph(this));
                row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
                return row;
            });
        if (HasColumns)
        {
            ItemsPanel = new FuncTemplate<Panel?>(() => new ColumnsPanel(this));
            ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(this, ScrollBarVisibility.Disabled);
        }
    }

    private sealed class CheckGlyph(VBListBox list) : Control
    {
        protected override Size MeasureOverride(Size availableSize) => new(16, 16);
        public override void Render(DrawingContext context)
        {
            var pen = new Pen(list.Foreground, 1);
            context.DrawRectangle(list.Background, pen, new Rect(1, 2, 11, 11));
            var index = list.IndexFromContainer(this.FindAncestorOfType<ListBoxItem>()!);
            if (list.checks.Contains(index))
            {
                context.DrawLine(pen, new Point(3, 7), new Point(6, 10));
                context.DrawLine(pen, new Point(6, 10), new Point(10, 4));
            }
        }
    }

    // Column width and checkbox pixels are drawing choices, not oracle measurements (#4209).
    private sealed class ColumnsPanel(VBListBox list) : Panel
    {
        internal int Rows { get; private set; } = 1;
        internal double ColumnWidth { get; private set; } = 1;
        private double rowHeight = 1;
        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var child in Children) child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            rowHeight = Children.Count > 0 ? Children[0].DesiredSize.Height : 1;
            var height = rowHeight;
            Rows = Math.Max(1, (int)Math.Floor(availableSize.Height / height));
            ColumnWidth = Math.Max(1, (list.Bounds.Width - list.BorderThickness.Left - list.BorderThickness.Right
                - list.Padding.Left - list.Padding.Right) / list.columns);
            foreach (var child in Children) child.Measure(new Size(ColumnWidth, height));
            return new Size(Math.Ceiling((double)Children.Count / Rows) * ColumnWidth, Rows * height);
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            var height = rowHeight;
            for (var i = 0; i < Children.Count; i++)
                Children[i].Arrange(new Rect(i / Rows * ColumnWidth, i % Rows * height, ColumnWidth, height));
            return finalSize;
        }
    }

    private ColumnsPanel? ColumnLayout => ItemsPanelRoot as ColumnsPanel;
    private int LastColumnTop => Math.Max(0, (int)Math.Ceiling((double)ItemCount / ColumnLayout!.Rows) - columns) * ColumnLayout.Rows;

    private void BringSelectionIntoView(int index)
    {
        if (!HasColumns)
        {
            if (!HasChecks)
                ScrollIntoView(index);
            else
            {
                var height = RowHeight();
                var rows = Math.Max(1, (int)Math.Floor(Scroll!.Viewport.Height / height));
                var checkTop = TopIndex;
                if (index < checkTop) checkTop = index;
                else if (index >= checkTop + rows) checkTop = index - rows + 1;
                Scroll.Offset = new Vector(Scroll.Offset.X, checkTop * height);
            }
            return;
        }
        EnsureTopIndexLayout();
        var panel = ColumnLayout!;
        var top = TopIndex;
        var column = index / panel.Rows * panel.Rows;
        if (column < top) top = column;
        else if (column >= top + columns * panel.Rows) top = column - (columns - 1) * panel.Rows;
        Scroll!.Offset = new Vector(Math.Min(top, LastColumnTop) / panel.Rows * panel.ColumnWidth, 0);
    }

    private void RaiseItemCheck(int index)
    {
        foreach (var visual in this.GetVisualDescendants())
            if (visual is CheckGlyph glyph) glyph.InvalidateVisual();
        foreach (var handler in ControlEvents.Handlers(this, this.FindLogicalAncestorOfType<IModuleExecutionRoot>(), ListBoxComponentClass.ItemCheckEvent.Name))
        {
            Vb6Value[] args = handler.Own && VBProps.GetIndex(this) is { } arrayIndex
                ? [new Vb6Value(arrayIndex), new Vb6Value(index)] : [new Vb6Value(index)];
            handler.Root.ExecuteSub(handler.Name, args);
        }
    }

    static VBListBox()
    {
        AttachedProperties.FontProperty.Changed.AddClassHandler<VBListBox>((list, _) => list.UpdateRows());
        ControlKeyEvents.Attach<VBListBox>();
        IsTextSearchEnabledProperty.OverrideDefaultValue<VBListBox>(true);
        // VB6 selects nothing for a right or middle press, where Avalonia's ListBox selects on a right one. Handled on
        // the way down, its own press handler never sees it; the mouse events take handled presses too. A left press on
        // a multi-select list is VB6's to handle, for the same reason.
        PointerPressedEvent.AddClassHandler<VBListBox>((list, e) =>
        {
            if (!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed)
                e.Handled = true;
            else if (list.IsMultiSelect)
            {
                list.PressMulti(list.IndexAt(e.Source), e.KeyModifiers, e.ClickCount);
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // Take keys on the way down: a focused item takes Space, and its ScrollViewer takes paging first.
        KeyDownEvent.AddClassHandler<VBListBox>((list, e) =>
        {
            list.lastKeyModifiers = e.KeyModifiers;
            // What it takes raises Click, changed or not (measured).
            if (list.IsMultiSelect && list.KeyMulti(e.Key, e.KeyModifiers))
            {
                e.Handled = true;
                list.ExecuteSub(ComponentBaseClass.ClickEvent);
            }
            else if (!list.IsMultiSelect && (e.Key is Key.Down or Key.Up or Key.PageDown or Key.PageUp or Key.Home or Key.End
                || list.HasColumns && e.Key == Key.Right || list.HasChecks && e.Key == Key.Space))
            {
                // An item keeps the focus after a click. Take navigation before its ScrollViewer takes paging.
                list.OnKeyDown(e);
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // Only scroll-bar pointer input participates; wheel and code scrolling must stay silent (#3275).
        PointerPressedEvent.AddClassHandler<VBListBox>((list, e) => list.BeginBarInput(e), RoutingStrategies.Tunnel);
        PointerMovedEvent.AddClassHandler<VBListBox>((list, e) => list.BeginBarInput(e), RoutingStrategies.Tunnel);
        PointerReleasedEvent.AddClassHandler<VBListBox>((list, e) => list.BeginBarInput(e), RoutingStrategies.Tunnel);
        PointerPressedEvent.AddClassHandler<VBListBox>((list, e) => list.EndBarInput(), handledEventsToo: true);
        PointerMovedEvent.AddClassHandler<VBListBox>((list, e) => list.EndBarInput(), handledEventsToo: true);
        PointerReleasedEvent.AddClassHandler<VBListBox>((list, e) => list.EndBarInput(), handledEventsToo: true);
        // Before the mouse events, so Click comes before MouseUp.
        PointerPressedEvent.AddClassHandler<VBListBox>((list, e) => list.Pressed(e), handledEventsToo: true);
        PointerMovedEvent.AddClassHandler<VBListBox>((list, e) => list.Moved(e), handledEventsToo: true);
        PointerReleasedEvent.AddClassHandler<VBListBox>((list, e) => list.Released(e), handledEventsToo: true);
        AttachedEvents.AttachMouse<VBListBox>(AttachedEvents.DblClick.InPlaceOfMouseDown);
        // A ListBox takes the focus itself, as VB6's does, not only through its items (#1101).
        FocusableProperty.OverrideDefaultValue<VBListBox>(true);
        AttachedEvents.AttachFocusEvents<VBListBox>();
    }

    public VBListBox()
    {
        // Code and keys scroll synchronously; a queued anchor scroll would undo a later TopIndex (#4229).
        AutoScrollToSelectedItem = false;
        LayoutUpdated += (_, _) =>
        {
            if (rowsNeedPadding)
            {
                rowsNeedPadding = false;
                UpdateRows();
            }
            if (!scrollIntoViewOnLayout || !CanScrollNow())
                return;
            scrollIntoViewOnLayout = false;
            // ListIndex rather than the index chosen: an item added above it since then moves it down.
            if (ListIndex >= 0)
            {
                BringSelectionIntoView(ListIndex);
                UpdateLayout();
            }
        };
        // Clear leaves the focus at 0; removal keeps its index unless that item was removed (#1424).
        Items.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                focusIndex = anchorIndex = 0;
                rangeEnd = null;
                shiftSelection = null;
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove && IsMultiSelect)
            {
                if (e.OldStartingIndex == focusIndex || focusIndex >= ItemCount)
                    focusIndex = Math.Clamp(focusIndex - 1, 0, Math.Max(0, ItemCount - 1));
                anchorIndex = -1;
                rangeEnd = null;
            }
            else if (focusIndex >= ItemCount)
                focusIndex = Math.Max(0, ItemCount - 1);
            if (ItemCount == 0)
                checks.Clear();
        };
    }

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (container is ListBoxItem listItem && ListBoxRowHeights.Find(AttachedProperties.GetFont(this)) is { } height)
        {
            listItem.UseLayoutRounding = false;
            listItem.Height = MeasuredRowHeight(height);
            rowsNeedPadding = true;
        }
        else
            ApplyRowHeight(container);
    }

    private void UpdateRows()
    {
        // VB6's first row starts at the list's inner edge: the theme's top padding pushed the last of 1,425 twips' seven
        // rows partly out of view (#4073, seen live). The bottom padding stays: it is the scroll slack that lets
        // SetTopIndexFromCode reach the measured last page when the viewport isn't a whole number of rows.
        // Horizontal padding is unmeasured and left as it is.
        ClearValue(PaddingProperty);
        if (ListBoxRowHeights.Find(AttachedProperties.GetFont(this)) is not null)
            Padding = new Thickness(Padding.Left, 0, Padding.Right, Padding.Bottom);
        foreach (var container in GetRealizedContainers())
            ApplyRowHeight(container);
    }

    // Only the default-font checkbox box has measured page capacity (#4209); other fonts keep their old rows.
    private double MeasuredRowHeight(double height) =>
        HasChecks && AttachedProperties.GetFont(this) == VBFont.Default ? 16 : height;

    private void ApplyRowHeight(Control container)
    {
        if (container is not ListBoxItem item)
            return;
        if (ListBoxRowHeights.Find(AttachedProperties.GetFont(this)) is { } height)
        {
            item.UseLayoutRounding = false;
            item.Height = MeasuredRowHeight(height);
            item.ClearValue(PaddingProperty);
            var padding = item.Padding;
            item.Padding = new Thickness(padding.Left, 0, padding.Right, 0);
        }
        else
        {
            item.ClearValue(UseLayoutRoundingProperty);
            item.ClearValue(HeightProperty);
            item.ClearValue(PaddingProperty);
        }
    }

    // #3275: TopIndex assignments raise Scroll only when the resulting top row changes.
    internal int TopIndex
    {
        get
        {
            EnsureTopIndexLayout();
            var height = RowHeight();
            if (height <= 0 || Scroll is not { } scroll)
                throw new NotImplementedException("ListBox.TopIndex cannot determine row layout (#3986)");
            return HasColumns && ColumnLayout is { } panel
                ? (int)Math.Round(scroll.Offset.X / panel.ColumnWidth) * panel.Rows
                : (int)Math.Floor(scroll.Offset.Y / height + 0.000001);
        }
    }

    private void EnsureTopIndexLayout()
    {
        UpdateLayout();
        if (RowHeight() > 0 && Scroll is { } scroll && scroll.Viewport.Height > 0)
            return;
        // A loaded form need not be shown for TopIndex (#3986). Measure and arrange the list at its
        // form dimensions so the real template supplies the same row height and viewport as when shown.
        // Initialize from the host: its content presenter must attach the list without showing the window.
        if (!double.IsFinite(Width) || !double.IsFinite(Height))
            return;
        Control root = this;
        while (root.Parent is Control parent)
            root = parent;
        InitializeLayout(root);
        InvalidateMeasure();
        Measure(new Size(Width, Height));
        Arrange(new Rect(Bounds.Position, DesiredSize));
        UpdateLayout();
    }

    private static void InitializeLayout(Control control)
    {
        if (!control.IsInitialized)
        {
            control.BeginInit();
            control.EndInit();
        }
        control.ApplyStyling();
        control.ApplyTemplate();
        // Detached presenters defer creating their child; force it before measuring the hidden form.
        if (control is Avalonia.Controls.Presenters.ContentPresenter presenter)
            presenter.UpdateChild();
        foreach (var child in control.GetVisualChildren())
            if (child is Control childControl)
                InitializeLayout(childControl);
        control.InvalidateMeasure();
    }

    private double RowHeight()
    {
        foreach (var container in GetRealizedContainers())
            if (container.Bounds.Height > 0)
                return container.Bounds.Height;
        return 0;
    }

    internal void SetTopIndexFromCode(int index)
    {
        if (index < 0 || index >= ItemCount)
            throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
        scrollIntoViewOnLayout = false;
        EnsureTopIndexLayout();
        var height = RowHeight();
        if (height <= 0 || Scroll is not { } scroll)
            throw new NotImplementedException("ListBox.TopIndex cannot determine row layout (#3986)");
        var before = TopIndex;
        if (HasColumns && ColumnLayout is { } panel)
            scroll.Offset = new Vector(Math.Min(index / panel.Rows * panel.Rows, LastColumnTop) / panel.Rows * panel.ColumnWidth, 0);
        else
        {
            var lastPage = Math.Max(0, ItemCount - (int)Math.Floor(scroll.Viewport.Height / height));
            scroll.Offset = new Vector(scroll.Offset.X, Math.Min(index, lastPage) * height);
        }
        UpdateLayout();
        if (TopIndex != before)
            this.ExecuteSub(ListBoxComponentClass.ScrollEvent);
    }

    /// <summary>The form's MultiSelect, which VB6 sets at design time only.</summary>
    public VBMultiSelect MultiSelect
    {
        get => multiSelect;
        init
        {
            multiSelect = value;
            IsTextSearchEnabled = value == VBMultiSelect.None;
            if (value != VBMultiSelect.None)
                SelectionMode = SelectionMode.Multiple;
        }
    }

    internal bool IsMultiSelect => multiSelect != VBMultiSelect.None;

    /// <summary>ListIndex as VB6 reads it: the selected item, or on a multi-select list the focused one.</summary>
    internal int ListIndex => IsMultiSelect ? focusIndex : SelectedIndex;

    /// <summary>Whether item <paramref name="index"/> is selected; the caller checks the range.</summary>
    internal bool IsItemSelected(int index) => HasChecks ? checks.Contains(index) : IsMultiSelect ? Selection.IsSelected(index) : index == SelectedIndex;

    /// <summary>SelCount: how many items are selected.</summary>
    internal int SelectedCount => HasChecks ? checks.Count : IsMultiSelect ? Selection.SelectedIndexes.Count : SelectedIndex >= 0 ? 1 : 0;

    // Checkbox ListIndex names a position, while checks follow items (#4209, round 2).
    internal void InsertCheckboxItem(int index, string text)
    {
        var position = ItemCount == 0 ? 0 : ListIndex;
        MoveChecks(index, 1);
        Items.Insert(index, text);
        SelectedIndex = position;
    }

    internal void RemoveCheckboxItem(int index)
    {
        var position = ListIndex;
        checks.Remove(index);
        MoveChecks(index + 1, -1);
        Items.RemoveAt(index);
        if (position == index)
            position = Math.Max(0, position - 1);
        SelectedIndex = Math.Min(position, ItemCount - 1);
    }

    private void MoveChecks(int start, int offset)
    {
        var moved = new HashSet<int>();
        foreach (var index in checks)
            moved.Add(index >= start ? index + offset : index);
        checks.Clear();
        checks.UnionWith(moved);
    }

    /// <summary>Text as VB6 reads it: the text of the item at ListIndex, or "".</summary>
    internal string ListIndexText => ListIndex >= 0 && ListIndex < ItemCount ? Items[ListIndex] as string ?? "" : "";

    internal bool IsScrollBarSource(object? source)
    {
        for (var visual = source as Visual; visual is not null && !ReferenceEquals(visual, this); visual = visual.GetVisualParent())
            if (visual is ScrollBar bar && bar.Orientation == Avalonia.Layout.Orientation.Vertical)
                return true;
        return false;
    }

    private void BeginBarInput(PointerEventArgs e)
    {
        pointerTop = !IsMultiSelect && IsScrollBarSource(e.Source) ? TopIndex : null;
        if (pointerTop is not null)
            for (var visual = e.Source as Visual; visual is not null && !ReferenceEquals(visual, this); visual = visual.GetVisualParent())
                if (visual is ScrollBar bar)
                {
                    bar.SetCurrentValue(ScrollBar.SmallChangeProperty, RowHeight());
                    bar.SetCurrentValue(ScrollBar.LargeChangeProperty, Math.Max(RowHeight(), Scroll!.Viewport.Height - RowHeight()));
                }
    }

    private void EndBarInput()
    {
        var before = pointerTop;
        pointerTop = null;
        if (before is null)
            return;
        UpdateLayout();
        if (TopIndex != before)
            this.ExecuteSub(ListBoxComponentClass.ScrollEvent);
    }

    private void Pressed(PointerPressedEventArgs e)
    {
        pressedOnItem = e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed
                        && IndexAt(e.Source) >= 0;
    }

    private void PressMulti(int index, KeyModifiers modifiers, int clickCount)
    {
        // The press is handled here, so Avalonia gives nothing the keyboard focus; its items take it, not the list.
        if (index >= 0)
            ContainerFromIndex(index)?.Focus(NavigationMethod.Pointer);
        // Below the items nothing changes, and a double click's second press toggles nothing again (measured).
        if (index < 0 || clickCount > 1)
            return;
        focusIndex = index;
        shiftPressed = false;
        shiftSelection = null;
        if (multiSelect == VBMultiSelect.Simple)
        {
            Toggle(index);
            anchorIndex = index;
        }
        else if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            var anchor = Anchor();
            if ((modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
            {
                // A previous Shift range is replaced, but other selections survive (#1413, #1424).
                if (rangeEnd is { } end)
                    for (var i = Math.Min(anchor, end); i <= Math.Max(anchor, end); i++)
                        if (i != anchor) Selection.Deselect(i);
                shiftSelection = new HashSet<int>(Selection.SelectedIndexes);
            }
            // The range comes at release; keep the existing press state.
            SelectOnly(Selection.IsSelected(anchor) ? anchor : -1);
            shiftPressed = true;
        }
        else if ((modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
        {
            Toggle(index);
            anchorIndex = index;
            rangeEnd = null;
        }
        else
        {
            SelectOnly(index);
            anchorIndex = index;
            rangeEnd = null;
        }
    }

    private void Moved(PointerEventArgs e)
    {
        if (!pressedOnItem || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        // Over the empty area, or outside the list, the selection stays on the last item reached.
        var index = IndexAt(this.InputHitTest(e.GetPosition(this)));
        if (index < 0)
            return;
        if (IsMultiSelect)
        {
            // A drag moves the focus, and on an Extended list selects from the anchor to it (measured).
            if (index == focusIndex)
                return;
            focusIndex = index;
            if (multiSelect == VBMultiSelect.Extended)
            {
                // Ctrl+Shift drags are unmeasured; keep their existing plain range (#1424).
                shiftSelection = null;
                SelectRange(index);
            }
        }
        else if (index != SelectedIndex)
            SelectedIndex = index;
    }

    private void Released(PointerReleasedEventArgs e)
    {
        var click = pressedOnItem && e.InitialPressMouseButton == MouseButton.Left && !AttachedEvents.IsSecondPress(this);
        pressedOnItem = false;
        if (shiftPressed)
        {
            shiftPressed = false;
            SelectRange(focusIndex);
        }
        // A double click's second release raises no Click (#1031).
        if (click)
            this.ExecuteSub(ComponentBaseClass.ClickEvent);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (IsMultiSelect)
        {
            // A letter searches with its character, after KeyPress has had it, as a single-select list's does (measured
            // there, key-events-keydown-keypress-and-keyup.md, #2325): KeyAscii = 0 cancels it, and Alt+letter, which
            // types nothing, stays an access key (#1424).
            if (e.Text is { Length: 1 } text && char.IsAsciiLetter(text[0]) && LetterMulti(text[0]))
            {
                e.Handled = true;
                this.ExecuteSub(ComponentBaseClass.ClickEvent);
            }
            return;
        }
        var before = SelectedIndex;
        base.OnTextInput(e);
        if (SelectedIndex != before)
            this.ExecuteSub(ComponentBaseClass.ClickEvent);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // A multi-select list's keys are taken on the way down; Avalonia's own would select as Avalonia does.
        if (IsMultiSelect || e.Handled)
            return;
        if (HasChecks && e.Key == Key.Space && ItemCount > 0)
        {
            var index = Math.Max(0, SelectedIndex);
            if (!checks.Add(index)) checks.Remove(index);
            this.ExecuteSub(ComponentBaseClass.ClickEvent);
            RaiseItemCheck(index);
            e.Handled = true;
            return;
        }
        var before = SelectedIndex;
        var scrollKey = e.Key is Key.Down or Key.Up or Key.PageDown or Key.PageUp or Key.Home or Key.End
            || HasColumns && e.Key == Key.Right;
        var top = scrollKey && RowHeight() > 0 ? TopIndex : (int?)null;
        if (HasColumns && scrollKey && ItemCount > 0)
        {
            var rows = ColumnLayout!.Rows;
            var delta = e.Key switch
            {
                Key.Down => 1, Key.Up => -1, Key.Right => rows,
                Key.PageDown => rows * columns - 1, Key.PageUp => 1 - rows * columns,
                Key.Home => -ItemCount, Key.End => ItemCount, _ => 0
            };
            SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, ItemCount - 1);
            e.Handled = true;
        }
        else if (e.Key is Key.PageDown or Key.PageUp && top is not null && ItemCount > 0)
        {
            var page = Math.Max(1, (int)Math.Floor(Scroll!.Viewport.Height / RowHeight()) - 1);
            // Both keys move the selection a page, the rows shown less one, from wherever it is; the view then scrolls
            // the least it can to show it. The top row plays no part (measured, #3986).
            SelectedIndex = e.Key == Key.PageUp
                ? Math.Max(0, SelectedIndex - page)
                : Math.Min(ItemCount - 1, SelectedIndex + page);
            e.Handled = true;
        }
        else
            base.OnKeyDown(e);
        if (top is not null && SelectedIndex != before)
        {
            BringSelectionIntoView(SelectedIndex);
            UpdateLayout();
            if (HasColumns || e.Key is Key.PageDown or Key.PageUp)
                ContainerFromIndex(SelectedIndex)?.Focus(NavigationMethod.Unspecified);
        }
        var scrolled = top is not null && TopIndex != top;
        // An arrow that moves the selection raises Click; one that can go no further does not.
        if (SelectedIndex != before)
            this.ExecuteSub(ComponentBaseClass.ClickEvent);
        if (scrolled)
            this.ExecuteSub(ListBoxComponentClass.ScrollEvent);
    }

    // Navigation measured on Simple and Extended lists (#1413, #1424). Letters come through OnTextInput.
    private bool KeyMulti(Key key, KeyModifiers modifiers)
    {
        if (ItemCount == 0 || key is not (Key.Up or Key.Down or Key.Space or Key.Home or Key.End or Key.PageUp or Key.PageDown))
            return false;
        if (key == Key.Space)
        {
            // Simple toggles the focused item, and Extended changes nothing, with Ctrl or without (measured).
            if (multiSelect == VBMultiSelect.Simple && focusIndex >= 0 && focusIndex < ItemCount)
            {
                Toggle(focusIndex);
                anchorIndex = focusIndex;
            }
            return true;
        }
        shiftSelection = null;
        var page = Math.Max(1, (int)Math.Floor((Scroll?.Viewport.Height ?? 0) / RowHeight()) - 1);
        focusIndex = key switch
        {
            Key.Home => 0,
            Key.End => ItemCount - 1,
            Key.PageUp => Math.Max(0, focusIndex - page),
            Key.PageDown => Math.Min(ItemCount - 1, focusIndex + page),
            _ => Math.Clamp(focusIndex + (key == Key.Down ? 1 : -1), 0, ItemCount - 1)
        };
        MoveFocusMulti(modifiers);
        return true;
    }

    // A letter finds the next item after the focus that starts with it, wrapping round (measured, #1424).
    private bool LetterMulti(char letter)
    {
        if (ItemCount == 0)
            return false;
        var prefix = letter.ToString();
        for (var step = 1; step <= ItemCount; step++)
        {
            var index = (Math.Max(-1, focusIndex) + step) % ItemCount;
            if (Items[index] is string text && text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                shiftSelection = null;
                focusIndex = index;
                MoveFocusMulti(lastKeyModifiers);
                return true;
            }
        }
        // Unmatched letters were not measured; retain the existing no-op.
        return false;
    }

    private void MoveFocusMulti(KeyModifiers modifiers)
    {
        // The focused item comes into view and takes the keyboard focus, as with the mouse. Not by a directional move,
        // which would have Avalonia select it.
        ScrollIntoView(focusIndex);
        ContainerFromIndex(focusIndex)?.Focus(NavigationMethod.Unspecified);
        if (multiSelect == VBMultiSelect.Simple)
            return;
        if (modifiers.HasFlag(KeyModifiers.Shift))
            SelectRange(focusIndex);
        else
        {
            // With Ctrl too: Ctrl+Up selected the item alone, as Up does (measured).
            SelectOnly(focusIndex);
            anchorIndex = focusIndex;
            rangeEnd = null;
        }
    }

    // Shift ranges keep the anchor's state; Ctrl+Shift also keeps the outside selection (#1424).
    private void SelectRange(int target)
    {
        var anchor = Anchor();
        var anchorSelected = Selection.IsSelected(anchor);
        Selection.BeginBatchUpdate();
        try
        {
            Selection.Clear();
            if (shiftSelection is not null)
                foreach (var index in shiftSelection) Selection.Select(index);
            for (var i = Math.Min(anchor, target); i <= Math.Max(anchor, target); i++)
                if (shiftSelection is null ? i != anchor || anchorSelected : anchorSelected)
                    Selection.Select(i);
            rangeEnd = target;
        }
        finally
        {
            Selection.EndBatchUpdate();
        }
    }

    private int Anchor() => Math.Clamp(anchorIndex, 0, Math.Max(0, ItemCount - 1));

    private void Toggle(int index)
    {
        if (Selection.IsSelected(index))
            Selection.Deselect(index);
        else
            Selection.Select(index);
    }

    // Only item index selected, or none for -1.
    private void SelectOnly(int index)
    {
        Selection.BeginBatchUpdate();
        try
        {
            Selection.Clear();
            if (index >= 0)
                Selection.Select(index);
        }
        finally
        {
            Selection.EndBatchUpdate();
        }
    }

    /// <summary>
    /// Sets ListIndex from code: Click when the selection moves, and none when it is set again (measured, #1374). On a
    /// multi-select list it moves the focus, and selects and clears nothing (#1413). Range checked by the caller.
    /// </summary>
    internal void SetListIndexFromCode(int index)
    {
        if (index == ListIndex)
            return;
        if (IsMultiSelect)
            focusIndex = index;
        else
            SelectFromCode(index);
        this.ExecuteSub(ComponentBaseClass.ClickEvent);
    }

    private void SelectFromCode(int index)
    {
        SelectedIndex = index;
        if (index >= 0)
            ScrollIntoViewFromCode(index);
        else
            scrollIntoViewOnLayout = false;
    }

    private bool CanScrollNow() =>
        IsEffectivelyVisible && RowHeight() > 0 && Scroll is { Viewport.Height: > 0 };

    /// <summary>
    /// Brings an item code selected into view, the least it can, before the statement after it runs: inside Click and
    /// without Scroll (#3986). A MultiSelect list's Selected(i) = True does the same (measured, #4229). A list not laid
    /// out yet, as in Form_Load, scrolls when it is, unless a TopIndex assignment comes first.
    /// </summary>
    private void ScrollIntoViewFromCode(int index)
    {
        scrollIntoViewOnLayout = false;
        if (!CanScrollNow())
        {
            scrollIntoViewOnLayout = true;
            return;
        }
        BringSelectionIntoView(index);
        UpdateLayout();
    }

    /// <summary>
    /// Sets Selected(index) from code, with Click when it changes (measured, #1395). On a single-select list it moves the
    /// selection; on a multi-select one, True adds the item and moves the focus to it, and False removes it and leaves
    /// the focus (#1413). Range checked by the caller.
    /// </summary>
    internal void SetSelectedFromCode(int index, bool selected)
    {
        if (HasChecks)
        {
            if (selected)
            {
                if (!checks.Add(index))
                    return;
                SelectFromCode(index);
            }
            else if (!checks.Remove(index))
                throw new NotImplementedException("ListBox checkbox Selected False on an unchecked item is not measured (#4209)");
            RaiseItemCheck(index);
            this.ExecuteSub(ComponentBaseClass.ClickEvent);
            return;
        }
        if (!IsMultiSelect)
        {
            if (selected)
                SetListIndexFromCode(index);
            else if (index == SelectedIndex)
                SetListIndexFromCode(-1);
            return;
        }
        if (Selection.IsSelected(index) == selected)
            return;
        if (selected)
        {
            Selection.Select(index);
            focusIndex = anchorIndex = index;
            rangeEnd = null;
            ScrollIntoViewFromCode(index);
        }
        else
            Selection.Deselect(index);
        this.ExecuteSub(ComponentBaseClass.ClickEvent);
    }

    internal void ItemTextReplaced(int index)
    {
        if (!IsMultiSelect) return;
        focusIndex = anchorIndex = index;
        rangeEnd = null;
    }

    /// <summary>
    /// Sets Text from code: selects the first item whose whole text it is, without regard to case, or nothing, and raises
    /// Click even when the selection does not move (measured, #1395). On a multi-select list it raises Click and changes
    /// nothing (#1413).
    /// </summary>
    internal void SetTextFromCode(string text)
    {
        if (!IsMultiSelect)
        {
            var index = -1;
            for (var i = 0; i < ItemCount && index < 0; i++)
                if (string.Equals(Items[i] as string, text, StringComparison.OrdinalIgnoreCase))
                    index = i;
            if (index != SelectedIndex)
                SelectFromCode(index);
        }
        this.ExecuteSub(ComponentBaseClass.ClickEvent);
    }

    // The index of the item a point or an event's source is on, or -1 for the empty area and anything outside the items.
    private int IndexAt(object? source)
    {
        for (var visual = source as Visual; visual is not null && !ReferenceEquals(visual, this); visual = visual.GetVisualParent())
            if (visual is ListBoxItem item)
                return IndexFromContainer(item);
        return -1;
    }
}
