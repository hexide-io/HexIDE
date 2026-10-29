using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.LogicalTree;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

public sealed partial class VBListView
{
    private bool headerPress;
    private bool secondPress;
    private ListViewItem? anchor;

    private IEnumerable<(ListViewItem Item, Rect Cell, Rect Row)> ItemCells()
    {
        var content = new Rect(Bounds.Size).Deflate(VBProps.GetClientInset(this));
        if (content.Width <= 0 || content.Height <= 0)
            yield break;
        var report = Number("View") == 3;
        var top = content.Y + (report && !Flag("HideColumnHeaders") && Columns.Entries.Count > 0 ? 18 : 0);
        var icon = Number("View") == 0;
        var width = icon ? 76 : Math.Max(40, Items.Entries.Select(i => TextWidth(i.Caption) + (Checkboxes ? 24 : 8)).DefaultIfEmpty(40).Max());
        var height = icon ? 64 : 18;
        var across = Math.Max(1, (int)(content.Width / width));
        var down = Math.Max(1, (int)((content.Bottom - top) / height));
        for (var index = 0; index < Items.Entries.Count; index++)
        {
            if (report)
            {
                var x = content.X;
                foreach (var column in Columns.DisplayOrder)
                {
                    var cell = new Rect(x, top + index * 18, column.Width / 15d, 18);
                    if (ReferenceEquals(column, Columns.Entries[0]))
                        yield return (Items.Entries[index], cell, new Rect(content.X, cell.Y, content.Width, 18));
                    x += cell.Width;
                }
            }
            else
            {
                var columnsFirst = Number("View") == 2 || Number("Arrange") == 1;
                var col = columnsFirst ? index / down : index % across;
                var row = columnsFirst ? index % down : index / across;
                var cell = new Rect(content.X + col * width, top + row * height, width, height);
                yield return (Items.Entries[index], cell, cell);
            }
        }
    }

    private Rect ItemLabel(ListViewItem item, Rect cell)
    {
        var icon = Number("View") == 0;
        var label = icon ? new Rect(cell.X, cell.Y + 32, cell.Width, cell.Height - 32) : cell.Deflate(new Thickness(2, 1));
        if (icon && !Flag("LabelWrap"))
        {
            var width = Math.Max(label.Width, TextWidth(item.Caption) + 4);
            label = new Rect(cell.Center.X - width / 2, label.Y, width, label.Height);
        }
        return label;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled || e.GetCurrentPoint(this).Properties.PointerUpdateKind is not
            (PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.RightButtonPressed or PointerUpdateKind.MiddleButtonPressed))
            return;
        DispatchMouse(() => PressMouse(e));
        e.Handled = true;
    }

    private void DispatchMouse(Func<Task> work)
    {
        if (this.FindLogicalAncestorOfType<IModuleExecutionRoot>()?.Events is { } pump)
        {
            var posting = pump.Posting();
            try
            {
                pump.Raise("(ListView mouse)", () =>
                {
                    posting.Dispose();
                    return work();
                });
            }
            finally { posting.Dispose(); }
        }
        else
            work().GetAwaiter().GetResult();
    }

    private ValueTask WaitForMouseEvents() =>
        this.FindLogicalAncestorOfType<IModuleExecutionRoot>()?.Events?.WaitForRaised() ?? ValueTask.CompletedTask;

    private async Task PressMouse(PointerPressedEventArgs e)
    {
        labelTimer?.Stop();
        var point = e.GetPosition(this);
        headerPress = false;
        secondPress = e.ClickCount % 2 == 0;
        var inset = VBProps.GetClientInset(this);
        if (Number("View") == 3 && !Flag("HideColumnHeaders") && point.Y >= inset && point.Y < inset + 18)
        {
            var x = inset;
            foreach (var column in Columns.DisplayOrder)
            {
                var cell = new Rect(x, inset, column.Width / 15d, 18);
                if (cell.Contains(point))
                {
                    headerPress = true;
                    if (!secondPress)
                        RaiseItemEvent("ColumnClick", column);
                    e.Handled = true;
                    return;
                }
                x += cell.Width;
            }
        }
        Focus();
        e.Pointer.Capture(this);
        if (secondPress)
            this.ExecuteSub(ComponentBaseClass.DblClickEvent);
        else
        {
            AttachedEvents.RaiseMouse(this, e, ComponentBaseClass.MouseDownEvent);
            await WaitForMouseEvents();
            var hit = false;
            foreach (var (item, cell, row) in ItemCells())
            {
                var label = ItemLabel(item, cell);
                var box = new Rect(label.X, label.Y + 1, 12, 12);
                if (Checkboxes && item.HasCheckbox && cell.Contains(point) && box.Contains(point))
                {
                    item.TryGetProperty("Checked", out var value);
                    item.TrySetProperty("Checked", new Vb6Value(!(bool)value.Value!));
                    RaiseItemEvent("ItemCheck", item);
                    hit = true;
                    break;
                }
                var target = Number("View") == 3 && Flag("FullRowSelect") ? row : ItemLabelBounds(item, cell);
                if (!target.Contains(point) || !new Rect(Bounds.Size).Contains(point))
                    continue;
                var edit = ReferenceEquals(SelectedItem, item) && item.IsSelected
                    && Number("LabelEdit") == 0 && e.KeyModifiers == KeyModifiers.None
                    && e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed;
                await MouseSelect(item, e.KeyModifiers);
                if (edit)
                    ScheduleLabelEdit(item);
                hit = true;
                break;
            }
            if (!hit)
            {
                var besideItem = ItemCells().Any(entry => entry.Row.Contains(point));
                foreach (var item in Items.Entries)
                    item.IsSelected = !besideItem && ReferenceEquals(item, SelectedItem);
            }
        }
        InvalidateVisual();
        e.Handled = true;
    }

    private async Task MouseSelect(ListViewItem item, KeyModifiers modifiers)
    {
        var shift = MultiSelect && modifiers.HasFlag(KeyModifiers.Shift);
        var control = MultiSelect && (modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        var previous = anchor is not null && Items.Entries.Contains(anchor) ? anchor : SelectedItem;
        if (shift && previous is not null)
        {
            var start = Items.Entries.IndexOf(previous);
            var end = Items.Entries.IndexOf(item);
            for (var i = 0; i < Items.Entries.Count; i++)
                Items.Entries[i].IsSelected = i >= Math.Min(start, end) && i <= Math.Max(start, end);
        }
        else if (control)
            item.IsSelected = true;
        else
        {
            foreach (var other in Items.Entries)
                other.IsSelected = false;
            item.IsSelected = true;
        }
        SelectedItem = shift || control ? null : item;
        try
        {
            if (shift && previous is not null)
            {
                RaiseItemEvent("ItemClick", previous);
                await WaitForMouseEvents();
                RaiseItemEvent("ItemClick", item);
                await WaitForMouseEvents();
            }
            RaiseItemEvent("ItemClick", item);
            await WaitForMouseEvents();
        }
        finally { SelectedItem = item; anchor = item; }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        AttachedEvents.RaiseMouse(this, e, ComponentBaseClass.MouseMoveEvent);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        DispatchMouse(async () =>
        {
            if (headerPress)
            {
                headerPress = false;
                return;
            }
            AttachedEvents.RaiseMouse(this, e, ComponentBaseClass.MouseUpEvent);
            await WaitForMouseEvents();
            if (!secondPress && e.InitialPressMouseButton is MouseButton.Left or MouseButton.Right or MouseButton.Middle)
                this.ExecuteSub(ComponentBaseClass.ClickEvent);
            e.Pointer.Capture(null);
        });
        e.Handled = true;
    }
}
