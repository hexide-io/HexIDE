using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Platform;
using Avalonia.Controls.Primitives.PopupPositioning;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

public class PopupBoundsTests
{
    [AvaloniaFact]
    public void Unavailable_bounds_explain_the_missing_position()
    {
        var detail = UiAutomationDriver.Inspect(new Button(), "Window/Button", new Border());
        detail.BoundingRect.Should().BeNull();
        detail.BoundingRectNote.Should().Contain("window");
        var json = JsonSerializer.SerializeToElement(detail, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
        if (json.TryGetProperty("boundingRect", out var bounds))
            bounds.ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("boundingRectNote").GetString().Should().Contain("window");
    }

    [AvaloniaTheory]
    [InlineData(false, 1.0)]
    [InlineData(true, 1.0)]
    [InlineData(false, 1.5)]
    [InlineData(true, 1.5)]
    public void Context_menu_bounds_are_relative_to_the_window(bool inspectMenu, double scaling)
    {
        var item = new MenuItem { Header = "Edit Watch..." };
        var menu = new ContextMenu
        {
            Items = { item }, Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft,
            PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight,
            HorizontalOffset = 170, VerticalOffset = 110,
        };
        var owner = new Border { ContextMenu = menu };
        var window = NativePopupWindow(scaling);
        window.Content = owner;
        window.Width = 600;
        window.Height = 400;
        window.Show();
        try
        {
            menu.Open(owner);
            Dispatcher.UIThread.RunJobs();
            Control target = inspectMenu ? menu : item;
            TopLevel.GetTopLevel(target).Should().NotBeSameAs(window, "the regression needs a separate popup top-level");
            var path = UiAutomationDriver.PathWithin(window, "Window", target)!;
            var bounds = UiAutomationDriver.Inspect(target, path, window).BoundingRect;
            bounds.Should().NotBeNull();
            bounds![0].Should().BeGreaterThanOrEqualTo(170);
            bounds[1].Should().BeGreaterThanOrEqualTo(110);
            var expected = window.PointToClient(target.PointToScreen(default));
            bounds[0].Should().BeApproximately(expected.X, 1);
            bounds[1].Should().BeApproximately(expected.Y, 1);
            var bottomRight = window.PointToClient(target.PointToScreen(new Point(target.Bounds.Width, target.Bounds.Height)));
            bounds[2].Should().BeApproximately(bottomRight.X - expected.X, 1);
            bounds[3].Should().BeApproximately(bottomRight.Y - expected.Y, 1);
        }
        finally { menu.Close(); window.Close(); }
    }

    // Headless CreatePopup returns null, and its screen conversion ignores Position. Forward its rendering/input
    // implementation but supply a separate popup at a known screen origin, as the desktop platform does (#3149).
    private static Window NativePopupWindow(double scaling)
    {
        Window? window = null;
        var backing = new Window();
        backing.SetRenderScaling(scaling);
        var parent = backing.PlatformImpl!;
        var platform = ForwardingPlatform<IWindowImpl>.Wrap((method, args) =>
        {
            if (method.Name != "CreatePopup") return method.Invoke(parent, args);
            var inner = (IPopupImpl)new Window().PlatformImpl!;
            var origin = window!.PointToScreen(new Point(170, 110));
            var positioner = Substitute.For<IPopupPositioner>();
            return ForwardingPlatform<IPopupImpl>.Wrap((popupMethod, popupArgs) =>
            {
                if (popupMethod.Name == "get_PopupPositioner") return positioner;
                if (popupMethod.Name == "PointToScreen")
                {
                    var point = (PixelPoint)popupMethod.Invoke(inner, popupArgs)!;
                    return new PixelPoint(origin.X + point.X, origin.Y + point.Y);
                }
                if (popupMethod.Name == "PointToClient")
                {
                    var point = (PixelPoint)popupArgs![0]!;
                    return popupMethod.Invoke(inner, [new PixelPoint(point.X - origin.X, point.Y - origin.Y)]);
                }
                return popupMethod.Invoke(inner, popupArgs);
            });
        });
        window = new Window(platform);
        return window;
    }

    public class ForwardingPlatform<T> : DispatchProxy where T : class
    {
        private Func<MethodInfo, object?[]?, object?> call = null!;

        public static T Wrap(Func<MethodInfo, object?[]?, object?> call)
        {
            var proxy = Create<T, ForwardingPlatform<T>>();
            ((ForwardingPlatform<T>)(object)proxy).call = call;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => call(targetMethod!, args);
    }

}
