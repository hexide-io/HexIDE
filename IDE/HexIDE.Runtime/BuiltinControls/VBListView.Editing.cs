using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

public sealed partial class VBListView
{
    private TextBox? labelEditor;
    private ListViewItem? editingItem;
    private string originalLabel = "";
    private Rect editorBounds;
    private DispatcherTimer? labelTimer;

    private sealed class EventArgument(Vb6Value value, Vb6Value.ValueType type, bool writable = true) : IByRefTarget
    {
        public Vb6Value.ValueType? DeclaredType => type;
        public bool IsVariant => false;
        public Vb6Value Read() => value;
        public void Write(Vb6Value next)
        {
            if (!writable)
                throw new NotImplementedException("ListView event argument mutation is unmeasured (#1085)");
            value = next;
        }
    }

    private async Task<bool> LabelEvent(string name, string? text = null)
    {
        var cancel = new EventArgument(new Vb6Value(0), Vb6Value.ValueType.Integer);
        foreach (var handler in ControlEvents.Handlers(this, this.FindLogicalAncestorOfType<IModuleExecutionRoot>(), name))
        {
            var args = new List<CallArg>();
            if (handler.Own && VBProps.GetIndex(this) is { } index)
                args.Add(new(new Vb6Value(index), null));
            args.Add(new(cancel.Read(), null, cancel));
            if (text is not null)
                args.Add(new(new Vb6Value(text), null, new EventArgument(new Vb6Value(text), Vb6Value.ValueType.String, false)));
            await handler.Root.ExecuteControlEventAsync(handler.Name, args);
        }
        return Convert.ToInt32(cancel.Read().Value, System.Globalization.CultureInfo.InvariantCulture) != 0;
    }

    private async Task RaiseKeyEvent(string name, KeyEventArgs e)
    {
        var handlers = ControlEvents.Handlers(this, this.FindLogicalAncestorOfType<IModuleExecutionRoot>(), name);
        // Key codes are the measured VBRUN KeyCodeConstants, not Avalonia's enum ordinals.
        var code = e.Key switch
        {
            Key.Up => 38, Key.Down => 40, Key.Home => 36, Key.Space => 32,
            Key.Enter => 13, Key.Escape => 27, Key.LeftCtrl or Key.RightCtrl => 17,
            _ => -1,
        };
        foreach (var handler in handlers)
        {
            if (!handler.Root.Handles(handler.Name))
                continue;
            if (code < 0)
                throw new NotImplementedException("ListView key event for this key is unmeasured (#1085)");
            var shift = (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : 0)
                | ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0 ? 2 : 0)
                | (e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? 4 : 0);
            var args = new List<CallArg>();
            if (handler.Own && VBProps.GetIndex(this) is { } index)
                args.Add(new(new Vb6Value(index), null));
            args.Add(new(new Vb6Value(code), null, new EventArgument(new Vb6Value(code), Vb6Value.ValueType.Integer, false)));
            args.Add(new(new Vb6Value(shift), null, new EventArgument(new Vb6Value(shift), Vb6Value.ValueType.Integer, false)));
            await handler.Root.ExecuteControlEventAsync(handler.Name, args);
        }
    }

    internal async Task StartLabelEdit()
    {
        labelTimer?.Stop();
        if (labelEditor is not null || SelectedItem is not { } item || !Items.Entries.Contains(item))
            return;
        if (await LabelEvent("BeforeLabelEdit"))
            return;
        editingItem = item;
        originalLabel = item.Caption;
        labelEditor = new TextBox { Text = originalLabel, AcceptsReturn = false };
        ScrollViewer.SetHorizontalScrollBarVisibility(labelEditor, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(labelEditor, ScrollBarVisibility.Disabled);
        labelEditor.AddHandler(KeyDownEvent, (_, e) =>
        {
            // Editing keys belong to the child, including Enter/Escape, never to the ListView.
            if (e.Key is Key.Enter or Key.Escape)
            {
                e.Handled = true;
                DispatchMouse(() => FinishLabelEdit(e.Key == Key.Enter));
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Child = labelEditor;
        InvalidateMeasure();
        labelEditor.Focus();
        labelEditor.SelectAll();
    }

    private async Task FinishLabelEdit(bool commit)
    {
        if (labelEditor is not { } editor || editingItem is not { } item)
            return;
        var text = editor.Text ?? "";
        if (commit && text != originalLabel && !await LabelEvent("AfterLabelEdit", text) && Items.Entries.Contains(item))
            ((ICSharpPropertyBag)item).TrySetProperty("Text", new Vb6Value(text));
        Child = null;
        labelEditor = null;
        editingItem = null;
        Focus();
        InvalidateVisual();
    }

    private void ScheduleLabelEdit(ListViewItem item)
    {
        labelTimer?.Stop();
        labelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        labelTimer.Tick += (_, _) =>
        {
            labelTimer.Stop();
            if (ReferenceEquals(SelectedItem, item) && IsFocused)
                DispatchMouse(StartLabelEdit);
        };
        labelTimer.Start();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        labelEditor?.Measure(Size.Infinity);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (editingItem is { } item)
        {
            var cell = ItemCells().FirstOrDefault(entry => ReferenceEquals(entry.Item, item)).Cell;
            editorBounds = ItemLabel(item, cell);
            if (Checkboxes)
                editorBounds = new Rect(editorBounds.X + 16, editorBounds.Y, Math.Max(0, editorBounds.Width - 16), editorBounds.Height);
        }
        if (labelEditor is { } editor)
            editor.Arrange(new Rect(editorBounds.Position,
                new Size(Math.Max(editor.DesiredSize.Width, editorBounds.Width),
                    Math.Max(editor.DesiredSize.Height, editorBounds.Height))));
        return finalSize;
    }
}
