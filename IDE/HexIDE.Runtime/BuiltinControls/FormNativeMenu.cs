using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;

namespace HexIDE.Runtime.BuiltinControls;

// Per-window menus, measured in #5011 K1–K14; #5065 keeps the VB tree as the program's source of truth.
internal static class FormNativeMenu
{
    internal static bool UseSystemMenuBar { get; set; } = OperatingSystem.IsMacOS();
    private sealed record Setting(bool Enabled);
    private static readonly ConditionalWeakTable<Window, Setting> settings = new();
    private static readonly ConditionalWeakTable<Menu, Setting> bars = new();

    internal static bool ForWindow(Window window) => settings.GetValue(window, _ => new(UseSystemMenuBar)).Enabled;
    internal static void Register(Window window, bool enabled) => settings.Add(window, new(enabled));
    internal static bool IsNative(Menu bar) => bars.TryGetValue(bar, out var setting) && setting.Enabled;

    internal static void Attach(Window window, Menu bar, Action<Control> click)
    {
        bars.Add(bar, new(true));
        var mirror = new Mirror(bar, true, click);
        NativeMenu.SetMenu(window, mirror.Menu);
        window.Closed += Closed;
        void Closed(object? sender, EventArgs args)
        {
            window.Closed -= Closed;
            NativeMenu.SetMenu(window, null);
            mirror.Dispose();
        }
    }

    private sealed class Mirror : IDisposable
    {
        internal NativeMenu Menu { get; } = new();
        private readonly ItemsControl source;
        private readonly bool topLevel;
        private readonly Action<Control> click;
        private readonly Dictionary<Control, Entry> entries = new();

        internal Mirror(ItemsControl source, bool topLevel, Action<Control> click)
        {
            this.source = source;
            this.topLevel = topLevel;
            this.click = click;
            source.Items.CollectionChanged += Changed;
            Sync();
        }

        private void Changed(object? sender, NotifyCollectionChangedEventArgs args) => Sync();

        private void Sync()
        {
            var controls = source.Items.OfType<Control>().ToArray();
            foreach (var removed in entries.Keys.Except(controls).ToArray())
            {
                entries[removed].Dispose();
                entries.Remove(removed);
            }
            foreach (var control in controls)
                if (!entries.ContainsKey(control))
                    entries.Add(control, new Entry(control, topLevel, click, Sync));
            var shown = controls.Where(c => c is not VBMenuSeparator || c.IsVisible).Select(c => entries[c].Item).ToArray();
            // Keep the native items themselves stable when an array element or a separator changes the collection.
            for (var i = Menu.Items.Count - 1; i >= 0; i--)
                if (!shown.Contains(Menu.Items[i])) Menu.Items.RemoveAt(i);
            for (var i = 0; i < shown.Length; i++)
                if (i >= Menu.Items.Count || !ReferenceEquals(Menu.Items[i], shown[i]))
                {
                    Menu.Items.Remove(shown[i]);
                    Menu.Items.Insert(i, shown[i]);
                }
        }

        public void Dispose()
        {
            source.Items.CollectionChanged -= Changed;
            foreach (var entry in entries.Values) entry.Dispose();
            entries.Clear();
        }
    }

    private sealed class Entry : IDisposable
    {
        internal NativeMenuItemBase Item { get; }
        private readonly Control source;
        private readonly Action<Control> click;
        private readonly Action sync;
        private readonly Mirror? children;

        internal Entry(Control source, bool topLevel, Action<Control> click, Action sync)
        {
            this.source = source;
            this.click = click;
            this.sync = sync;
            if (source is VBMenu menu)
            {
                var item = new NativeMenuItem { ToggleType = MenuItemToggleType.CheckBox };
                Item = item;
                if (topLevel || menu.ItemCount > 0)
                {
                    children = new Mirror(menu, false, click);
                    item.Menu = children.Menu;
                    children.Menu.NeedsUpdate += NeedsUpdate;
                }
                item.Click += Click;
                Update();
            }
            else
                Item = new NativeMenuItemSeparator();
            source.PropertyChanged += Changed;
        }

        private void Update()
        {
            if (source is VBMenu menu && Item is NativeMenuItem item)
            {
                item.Header = VBCaption.Parse(menu.Header as string ?? "").Shown;
                item.IsEnabled = menu.IsEnabled;
                item.IsVisible = menu.IsVisible;
                item.IsChecked = menu.Checked;
            }
        }

        private void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            Update();
            if (source is VBMenuSeparator && args.Property == Control.IsVisibleProperty) sync();
        }
        private void Click(object? sender, EventArgs args)
        {
            Update(); // Native clicks must not toggle the program's Checked value.
            click(source);
        }
        private void NeedsUpdate(object? sender, EventArgs args) => click(source);
        public void Dispose()
        {
            source.PropertyChanged -= Changed;
            if (Item is NativeMenuItem item) item.Click -= Click;
            if (children is not null)
            {
                children.Menu.NeedsUpdate -= NeedsUpdate;
                children.Dispose();
            }
        }
    }
}
