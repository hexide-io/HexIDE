using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Labs.Input;
using Avalonia.Media;
using HexIDE.Keymaps;
using Serilog;

namespace HexIDE.Utils;

public class MenuUtils
{
    // Weak keys let closed menus be collected while still refreshing unattached context menus.
    private static readonly ConditionalWeakTable<MenuItem, object> CommandItems = new();

    private static void RefreshGesture(MenuItem item)
    {
        item.InputGesture = item.Command is RoutedCommand command && command.Gestures.Count > 0
            ? command.Gestures[0]
            : null;
    }

    // Menu icons are theme-tinted vector geometries (see Themes/IconGeometry.axaml), rendered as a
    // Path whose Fill tracks the IconInk theme resource so they invert with the IDE theme.
    public static readonly AttachedProperty<Geometry> MenuIconProperty = AvaloniaProperty.RegisterAttached<MenuUtils, MenuItem, Geometry>("MenuIcon");

    public static readonly AttachedProperty<RoutedCommand> CommandProperty = AvaloniaProperty.RegisterAttached<MenuUtils, MenuItem, RoutedCommand>("Command");

    public static Geometry GetMenuIcon(AvaloniaObject element) => element.GetValue(MenuIconProperty);

    public static void SetMenuIcon(AvaloniaObject element, Geometry value) => element.SetValue(MenuIconProperty, value);

    public static RoutedCommand GetCommand(AvaloniaObject element) => element.GetValue(CommandProperty);

    public static void SetCommand(AvaloniaObject element, RoutedCommand value) => element.SetValue(CommandProperty, value);

    static MenuUtils()
    {
        MenuIconProperty.Changed.AddClassHandler<MenuItem>((menuItem, e) =>
        {
            if (e.NewValue is not Geometry geometry)
            {
                menuItem.Icon = null;
                return;
            }

            var path = new Avalonia.Controls.Shapes.Path
            {
                Data = geometry,
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
            };
            // Live-bind Fill to the themed IconInk brush so menu icons invert with the IDE theme.
            path.Bind(Shape.FillProperty, menuItem.GetResourceObservable("IconInk"));
            menuItem.Icon = path;
        });
        MenuItem.CommandProperty.Changed.AddClassHandler<MenuItem>((menuItem, e) =>
        {
            if (e.NewValue is RoutedCommand)
                CommandItems.GetOrCreateValue(menuItem);
            else
                CommandItems.Remove(menuItem);
            RefreshGesture(menuItem);
        });
        KeymapService.GesturesChanged += () =>
        {
            // An item kept only for finalization remains in the weak table, but its template
            // bindings have lost their targets. Avalonia 12.1.x casts the UnsetValue they publish.
            // Remove this guard once our Avalonia release contains AvaloniaUI/Avalonia#21552. Its backport to 12.1.x
            // is requested in AvaloniaUI/Avalonia#22882 (#3867).
            List<MenuItem>? discarded = null;
            foreach (var entry in CommandItems)
            {
                try
                {
                    RefreshGesture(entry.Key);
                }
                catch (InvalidCastException e)
                {
                    (discarded ??= []).Add(entry.Key);
                    Log.Debug(e, "Discarding menu item with a collected gesture binding target");
                }
            }
            if (discarded is not null)
                foreach (var item in discarded)
                    CommandItems.Remove(item);
        };
    }
}
