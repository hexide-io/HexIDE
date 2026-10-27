using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.LogicalTree;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>SSTab runtime pages and tab strip (#1090).</summary>
public sealed class VBSSTab : Decorator, IVBContainerControl
{
    private SSTabPages? pages;
    private readonly List<string> captions = ["Tab 0", "Tab 1", "Tab 2"];
    private readonly List<bool> enabled = [true, true, true];
    private readonly List<bool> visible = [true, true, true];

    private readonly SSTabStrip strip;
    static VBSSTab()
    {
        AttachedEvents.AttachMouse<VBSSTab>(AttachedEvents.DblClick.InPlaceOfMouseDown);
        AttachedEvents.AttachFocusEvents<VBSSTab>();
    }

    public VBSSTab(Canvas host)
    {
        ChildHost = host;
        strip = new SSTabStrip(this) { IsHitTestVisible = false };
        Child = new Grid { Background = Brushes.Transparent, Children = { strip, host } };
        Focusable = true;
        ClipToBounds = true;
        strip.Refresh();
        AddHandler(KeyDownEvent, HandleKey, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, HandlePress);
    }

    private void HandlePress(object? sender, PointerPressedEventArgs e)
    {
        if (!AttachedEvents.OwnsThePointerEvent(this, e.Source))
            return;
        e.Pointer.Capture(this);
        if (strip.TabAt(e.GetPosition(strip)) is { } index)
        {
            Focus();
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.ClickCount % 2 == 1 && TabEnabled(index))
            {
                // MouseDown may yield in the interpreter. Switch on its queue, before the later MouseUp (#2358).
                if (this.FindLogicalAncestorOfType<IModuleExecutionRoot>()?.Events is { } events)
                    events.Raise("SSTab select", () =>
                    {
                        Tab = index;
                        return Task.CompletedTask;
                    });
                else
                    Tab = index;
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (ReferenceEquals(e.Pointer.Captured, this))
            e.Pointer.Capture(null);
    }

    private void HandleKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
            return;
        if (e.KeyModifiers == KeyModifiers.Alt)
        {
            for (var i = 0; i < Tabs; i++)
            {
                var caption = Caption(i);
                for (var c = 0; c + 1 < caption.Length; c++)
                {
                    if (caption[c] != '&') continue;
                    if (caption[c + 1] == '&') { c++; continue; }
                    if (string.Equals(caption[c + 1].ToString(), e.Key.ToString(), StringComparison.OrdinalIgnoreCase)
                        && TabVisible(i) && TabEnabled(i))
                    {
                        Tab = i;
                        Focus();
                        e.Handled = true;
                        return;
                    }
                    break;
                }
            }
        }
        var command = Avalonia.Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers;
        if (!IsFocused)
        {
            if ((e.Key == Key.Tab && (e.KeyModifiers == command || e.KeyModifiers == (command | KeyModifiers.Shift)))
                || (e.Key == Key.PageDown && e.KeyModifiers == command))
                e.Handled = true;
            return;
        }
        var direction = e.KeyModifiers == KeyModifiers.None
            ? e.Key == Key.Right ? 1 : e.Key == Key.Left ? -1 : 0
            : e.Key == Key.Tab && e.KeyModifiers == command ? 1
            : e.Key == Key.Tab && e.KeyModifiers == (command | KeyModifiers.Shift) ? -1 : 0;
        if (direction == 0) return;
        var next = (Tab + direction + Tabs) % Tabs;
        if (TabVisible(next) && TabEnabled(next))
            Tab = next;
        e.Handled = true;
    }

    public Canvas ChildHost { get; }
    private SSTabPages Pages
    {
        get
        {
            if (pages is null)
            {
                pages = new SSTabPages(ChildHost, 0, [[], [], []]);
                AttachPageEvents();
            }
            return pages;
        }
    }

    private void AttachPageEvents()
    {
        pages!.Changed += previous =>
        {
            strip.Refresh();
            foreach (var handler in ControlEvents.Handlers(this, this.FindLogicalAncestorOfType<IModuleExecutionRoot>(), "Click"))
            {
                var args = handler.Own && VBProps.GetIndex(this) is { } index
                    ? new[] { new Vb6Value(index), new Vb6Value((short)previous) }
                    : new[] { new Vb6Value((short)previous) };
                handler.Root.ExecuteSub(handler.Name, args);
            }
        };
    }

    internal void LoadPages(int showing, IReadOnlyList<IReadOnlyList<Control>> lists, IReadOnlyList<bool> flags)
    {
        pages = new SSTabPages(ChildHost, showing, lists, flags);
        AttachPageEvents();
        strip.Refresh();
    }

    public int Tab
    {
        get => Pages.Tab;
        set
        {
            if (value < 0 || value >= Tabs || !visible[value])
                throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
            Pages.Select(value);
        }
    }

    public int Tabs
    {
        get => Pages.Tabs;
        set
        {
            var oldRows = Rows;
            Pages.Resize(value);
            while (captions.Count < value)
            {
                captions.Add($"Tab {captions.Count}");
                enabled.Add(true);
                visible.Add(true);
            }
            if (captions.Count > value)
            {
                captions.RemoveRange(value, captions.Count - value);
                enabled.RemoveRange(value, enabled.Count - value);
                visible.RemoveRange(value, visible.Count - value);
            }
            MoveChildren(Pages.RecordedChildren.Where(c => ChildHost.Children.Contains(c)),
                (Rows - oldRows) * Math.Round(TabHeight), TabOrientation);
            strip.Refresh();
        }
    }

    private int tabsPerRow = 3;
    public int TabsPerRow
    {
        get => tabsPerRow;
        set { tabsPerRow = value; strip.Refresh(); }
    }
    public int Rows => (visible.Count(v => v) + TabsPerRow - 1) / TabsPerRow;
    private int style;
    public int VbStyle
    {
        get => style;
        set { style = value; strip.Refresh(); }
    }
    private int orientation;
    public int TabOrientation
    {
        get => orientation;
        set
        {
            var thickness = Rows * Math.Round(TabHeight);
            MoveChildren(ChildHost.Children, -thickness, orientation);
            orientation = value;
            MoveChildren(ChildHost.Children, thickness, orientation);
            strip.Refresh();
        }
    }

    private static void MoveChildren(IEnumerable<Control> children, double distance, int side)
    {
        foreach (var child in children)
        {
            if (side == 0)
                Canvas.SetTop(child, Canvas.GetTop(child) + distance);
            else if (side == 2)
                Canvas.SetLeft(child, Canvas.GetLeft(child) + distance);
        }
    }
    private double tabHeight = 295d / 15;
    public double TabHeight
    {
        get => tabHeight;
        set { tabHeight = value; strip.Refresh(); }
    }
    private double tabMaxWidth;
    public double TabMaxWidth
    {
        get => tabMaxWidth;
        set { tabMaxWidth = value; strip.Refresh(); }
    }
    private bool wordWrap = true;
    public bool WordWrap
    {
        get => wordWrap;
        set { wordWrap = value; strip.Refresh(); }
    }
    private bool showFocusRect = true;
    public bool ShowFocusRect
    {
        get => showFocusRect;
        set { showFocusRect = value; strip.Refresh(); }
    }

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEffectivelyEnabledProperty)
            strip?.Refresh();
        else if (change.Property == IsFocusedProperty)
            strip?.RefreshFocus();
    }

    public void CheckIndex(int index)
    {
        if (index < 0 || index >= Tabs)
            throw new VBRunTimeException(VBStandardError.InvalidPropertyArrayIndex);
    }

    public string Caption(int index) { CheckIndex(index); return captions[index]; }
    public void Caption(int index, string value) { CheckIndex(index); captions[index] = value; strip.Refresh(); }
    public bool TabEnabled(int index) { CheckIndex(index); return enabled[index]; }
    public void TabEnabled(int index, bool value) { CheckIndex(index); enabled[index] = value; strip.Refresh(); }
    public bool TabVisible(int index) { CheckIndex(index); return visible[index]; }
    public void TabVisible(int index, bool value)
    {
        CheckIndex(index);
        visible[index] = value;
        if (index == Tab && !value && visible.IndexOf(true) is >= 0 and var first)
            Tab = first;
        strip.Refresh();
    }
}
