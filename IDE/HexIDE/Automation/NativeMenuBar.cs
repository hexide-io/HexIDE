using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using HexIDE.IDE;

namespace HexIDE.Automation;

// Avalonia's menu model is available even when no platform exporter is running.
internal sealed class NativeMenuBar
{
    internal const string WindowPathError = "A MenuBar path needs window: \"menubar\"; use a Window path for a window.";
    internal const string Unsupported = "macOS draws the menu bar outside HexIDE's windows. Use dump_visual_tree, inspect_element or interact with window: \"menubar\" to reach its menus.";
    internal static bool IsScope(string? window) => string.Equals(window, "menubar", StringComparison.OrdinalIgnoreCase);
    internal static string AppTitle => string.IsNullOrEmpty(Application.Current?.Name) ? "Application" : Application.Current.Name;
    internal string Note { get; }
    /// <summary>The window whose menus follow the app menu, or null; an action waits for its program.</summary>
    internal Window? BarWindow { get; }
    private readonly Entry root;
    private sealed record Entry(string Name, string? AutomationId, NativeMenuItemBase? Item, NativeMenu? Menu, bool Top, List<Entry> Children);

    internal NativeMenuBar()
    {
        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var windows = lifetime?.Windows ?? [];
        var active = windows.Where(w => w.IsVisible && w.IsActive).ToArray();
        var window = active.Length == 1 ? active[0] : lifetime?.MainWindow is { } main ? ForegroundWindow.Pick(main, windows) : null;
        BarWindow = window;
        root = new Entry("", null, null, null, false, []);
        var app = Application.Current is { } application ? NativeMenu.GetMenu(application) : null;
        if (app is not null)
            root.Children.Add(new Entry(AppTitle, null, null, app, true, Read(app, false)));
        if (window is not null && NativeMenu.GetMenu(window) is { } menu)
            root.Children.AddRange(Read(menu, true));
        Note = $"Bar window: '{window?.Title ?? "none"}', picked by {(active.Length == 1 ? "the single visible active window" : "auto (fallback when zero or several visible windows are active)")}. ";
        if (root.Children.Count == 0)
            Note += "HexIDE puts menus in the macOS menu bar only on macOS. Here they are in their windows. Windows: " + string.Join(", ", windows.Where(w => w.IsVisible).Select(w => w.Title)) + ". ";
        if (root.Children.Any(e => e.Top && e.Item is not null && e.Menu is null))
            Note += "macOS doesn't draw a top-level item without a submenu; it is marked isHidden. ";
    }

    private static List<Entry> Read(NativeMenu menu, bool top) => menu.Items.Select(item =>
        new Entry(item is NativeMenuItem i ? MenuPath.StripAccessKey(i.Header ?? "") : "", IdOf(item), item,
            (item as NativeMenuItem)?.Menu, top,
            item is NativeMenuItem { Menu: { } sub } ? Read(sub, false) : [])).ToList();
    // A running form's mirror sets each item's AutomationId to its menu's code name (FormNativeMenu); other items set none.
    private static string? IdOf(NativeMenuItemBase item) =>
        item.GetValue(AutomationProperties.AutomationIdProperty)?.Trim() is { Length: > 0 } id ? id : null;
    private static string Type(Entry e) => e.Item is NativeMenuItemSeparator ? "Separator" : "MenuItem";
    private static bool Hidden(Entry e) => e.Item is NativeMenuItem { IsVisible: false } || e.Top && e.Menu is null;
    private static bool Enabled(Entry e) => e.Item is not NativeMenuItem i || i.IsEnabled;
    // Why a hidden node isn't shown: its own IsVisible, or macOS leaving out a top-level item with no submenu (K13).
    private static string HiddenReason(Entry e) => e.Item is NativeMenuItem { IsVisible: false }
        ? "its IsVisible is False, so macOS doesn't show it"
        : "macOS doesn't draw a top-level item without a submenu";
    private static string? HiddenRefusal(string path, Entry e, List<Entry> ancestors) =>
        ancestors.Append(e).FirstOrDefault(Hidden) is { } hidden
            ? $"'{path}' is hidden: {HiddenReason(hidden)}{(hidden == e ? "" : $" ('{hidden.Name}' is)")}."
            : null;
    private static string[] Providers(Entry e) => e.Item is NativeMenuItemSeparator ? [] : e.Menu is null ? ["invoke"] : ["expandCollapse"];
    private static string Segment(Entry e, List<Entry> siblings) => Type(e) + "[" +
        (e.Name.Length == 0 || siblings.Count(s => Type(s) == Type(e) && string.Equals(s.Name, e.Name, StringComparison.OrdinalIgnoreCase)) > 1
            ? "#" + siblings.Where(s => Type(s) == Type(e)).ToList().IndexOf(e) : e.Name) + "]";

    private static List<Entry>? FindId(Entry parent, string id)
    {
        foreach (var child in parent.Children)
        {
            if (string.Equals(child.AutomationId, id, StringComparison.OrdinalIgnoreCase)) return [child];
            if (FindId(child, id) is { } descendants) return [child, .. descendants];
        }
        return null;
    }

    internal string? PathOf(string id)
    {
        if (FindId(root, id) is not { } chain) return null;
        var path = "MenuBar";
        var parent = root;
        foreach (var entry in chain)
        {
            path += "/" + Segment(entry, parent.Children);
            parent = entry;
        }
        return path;
    }

    internal static string? WindowPathHint(Window window, string windowName, string error)
    {
        if (NativeMenu.GetMenu(window) is not { } menu) return null;
        var end = error.IndexOf("' under '", StringComparison.Ordinal);
        if (!error.StartsWith("no child matching '", StringComparison.Ordinal) || end < 0) return null;
        var segment = error["no child matching '".Length..end];
        string? id = null;
        if (segment.StartsWith('#'))
        {
            var source = new Entry("", null, null, menu, false, Read(menu, true));
            id = FindId(source, segment[1..])?.Last().AutomationId;
            if (id is null) return null;
        }
        else
        {
            var bracket = segment.IndexOf('[');
            var type = bracket < 0 ? segment : segment[..bracket];
            if (!string.Equals(type, "Menu", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(type, "MenuItem", StringComparison.OrdinalIgnoreCase)) return null;
        }
        var bar = new NativeMenuBar();
        var hint = $"{windowName}'s menus are in the system menu bar, not the window";
        if (bar.BarWindow != window)
            return hint + $". window: \"menubar\" reaches the bar window's menus, and the bar window is now '{bar.BarWindow?.Title ?? "none"}', not {windowName}";
        return id is not null && bar.PathOf(id) is { } path
            ? hint + $": use window: \"menubar\", as {path} or MenuBar/#{id}"
            : hint + ": use window: \"menubar\"; dump_visual_tree(window: \"menubar\") lists them";
    }

    private (Entry? Entry, List<Entry> Ancestors, string? Error) Resolve(string path)
    {
        var parts = path.Split('/');
        if (parts[0] != "MenuBar") return (null, [], "Use a MenuBar path with window: \"menubar\"; Window paths need a window such as \"ide\".");
        var current = root;
        var ancestors = new List<Entry>();
        foreach (var part in parts.Skip(1))
        {
            if (part.StartsWith('#') && FindId(current, part[1..]) is { } chain)
            {
                foreach (var entry in chain)
                {
                    ancestors.Add(current);
                    current = entry;
                }
                continue;
            }
            var next = current.Children.FirstOrDefault(e => string.Equals(Segment(e, current.Children), part, StringComparison.OrdinalIgnoreCase));
            var bracket = part.IndexOf("[#", StringComparison.Ordinal);
            if (bracket > 0 && part.EndsWith(']') && int.TryParse(part[(bracket + 2)..^1], out var index) && index >= 0)
                next = current.Children.Where(e => string.Equals(Type(e), part[..bracket], StringComparison.OrdinalIgnoreCase)).Skip(index).FirstOrDefault();
            if (next is null) return (null, ancestors, $"No menu item '{part}' under '{current.Name}'. Discover paths with dump_visual_tree(window: \"menubar\").");
            ancestors.Add(current);
            current = next;
        }
        return (current, ancestors, null);
    }

    internal (UiNode? Tree, string? Error) Dump(string? path, int depth, bool interactiveOnly)
    {
        var (entry, _, error) = Resolve(path ?? "MenuBar");
        return (entry is null ? null : Node(entry, path ?? "MenuBar", depth, interactiveOnly), error);
    }
    private UiNode Node(Entry e, string path, int depth, bool interactiveOnly)
    {
        var children = depth == 0 ? [] : e.Children.Where(c => !interactiveOnly || c.Item is not NativeMenuItemSeparator)
            .Select(c => Node(c, path + "/" + Segment(c, e.Children), depth - 1, interactiveOnly)).ToArray();
        return new UiNode(path, e == root ? "MenuBar" : Type(e), e.Name, e.AutomationId,
            e == root ? "NativeMenu" : e.Item is NativeMenuItemSeparator ? "NativeMenuItemSeparator" : "NativeMenuItem",
            null, e == root ? [] : Providers(e), Enabled(e), false, Hidden(e) ? true : null, children,
            Truncated: depth == 0 && e.Children.Count > 0 ? true : null);
    }
    internal (UiNodeDetail? Element, string? Error) Inspect(string path)
    {
        var (e, _, error) = Resolve(path);
        if (e is null) return (null, error);
        var n = Node(e, path, 0, false);
        return (new UiNodeDetail(path, n.ControlType, n.Name, n.AutomationId, n.ClassName, null, n.Providers,
            n.IsEnabled, false, false, false, n.IsHidden, null, [], null,
            e.Item is NativeMenuItem { ToggleType: not MenuItemToggleType.None } i ? i.IsChecked : null, [],
            BoundingRectNote: "The native menu bar is outside the window's control tree. " + Note), null);
    }
    // Avalonia hides these PrivateApi members in its reference assembly; the runtime interface exposes them.
    private static void Raise(NativeMenu menu, string method) =>
        typeof(INativeMenuExporterEventsImplBridge).GetMethod(method)!.Invoke(menu, null);

    private static void Open(NativeMenu menu)
    {
        Raise(menu, "RaiseNeedsUpdate");
        Raise(menu, "RaiseOpening");
    }
    internal InteractOutcome Interact(string path, string action)
    {
        var (e, ancestors, error) = Resolve(path);
        InteractOutcome Refuse(string reason) => new(false, "native-menu", null, reason + " " + Note);
        if (e is null) return Refuse(error!);
        if (e == root || e.Item is NativeMenuItemSeparator) return Refuse("This node has no actions.");
        if (!Enabled(e) || ancestors.Any(a => !Enabled(a))) return Refuse($"'{path}' is disabled.");
        if (HiddenRefusal(path, e, ancestors) is { } hiddenBefore) return Refuse(hiddenBefore);
        action = action.ToLowerInvariant();
        if (e.Menu is not null && action == "invoke") return Refuse($"'{path}' is a submenu; use expand.");
        if (e.Menu is null ? action != "invoke" : action is not ("expand" or "collapse"))
            return Refuse(e.Menu is null ? "Use invoke for this item." : "Use expand or collapse for this submenu.");
        if (action == "collapse") Raise(e.Menu!, "RaiseClosed");
        else
        {
            foreach (var ancestor in ancestors.Where(a => a.Menu is not null)) Open(ancestor.Menu!);
            // Opening handlers may change whether the leaf can be chosen.
            if (!Enabled(e) || ancestors.Any(a => !Enabled(a))) return Refuse($"'{path}' is disabled.");
            if (HiddenRefusal(path, e, ancestors) is { } hiddenAfter) return Refuse(hiddenAfter);
            if (action == "expand") Open(e.Menu!);
            else ((INativeMenuItemExporterEventsImplBridge)e.Item!).RaiseClicked();
        }
        // No closing period: a program's wait notes are appended to it as to a window's interact detail.
        return new(true, "native-menu", $"{action}: '{path}'", null);
    }

    /// <summary>The item's state as it reads now, for a reply sent once the program has handled the action.</summary>
    internal string StateOf(string path)
    {
        var (e, _, _) = Resolve(path);
        if (e?.Item is not NativeMenuItem item)
            return $"'{path}' is no longer in the menu bar.";
        return $"Now: enabled {item.IsEnabled}"
            + (item.ToggleType == MenuItemToggleType.None ? "" : $", checked {item.IsChecked}")
            + (Hidden(e) ? ", hidden" : "") + ".";
    }

    private static string WithoutEllipsis(string text)
    {
        text = text.TrimEnd();
        if (text.EndsWith("...", StringComparison.Ordinal)) return text[..^3].TrimEnd();
        return text.EndsWith('…') ? text[..^1].TrimEnd() : text;
    }

    internal InteractOutcome InvokeApp(string path)
    {
        var current = root.Children.First(e => e.Item is null);
        var target = "MenuBar/" + Segment(current, root.Children);
        foreach (var name in path.Split('/').Skip(1))
        {
            Entry? Named(string text) => current.Children.FirstOrDefault(e =>
                string.Equals(e.Name, text, StringComparison.OrdinalIgnoreCase));
            var stripped = MenuPath.StripAccessKey(name);
            var next = Named(name) ?? Named(stripped) ?? current.Children.FirstOrDefault(e =>
                string.Equals(WithoutEllipsis(e.Name), WithoutEllipsis(name), StringComparison.OrdinalIgnoreCase)
                || string.Equals(WithoutEllipsis(e.Name), WithoutEllipsis(stripped), StringComparison.OrdinalIgnoreCase));
            if (next is null) return new(false, "native-menu", null, $"No item '{name}' in '{current.Name}'. It holds: {string.Join(", ", current.Children.Select(c => c.Name))}");
            target += "/" + Segment(next, current.Children);
            current = next;
        }
        return Interact(target, "invoke");
    }
}
