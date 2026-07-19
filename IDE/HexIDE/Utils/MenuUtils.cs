using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Labs.Input;
using Avalonia.Media;

namespace HexIDE.Utils;

public class MenuUtils
{
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
            if (e.NewValue as RoutedCommand is {} routedCommand)
            {
                if (routedCommand.Gestures.Count >= 1)
                    menuItem.InputGesture = routedCommand.Gestures[0];
            }
        });
    }
}
