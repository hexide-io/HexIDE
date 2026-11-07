using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HexIDE.Controls;

namespace HexIDE.Integration.Tests.Controls;

public class ResizeAdornerTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Drag_at_150_percent_preserves_the_declared_size(bool resizeRight)
    {
        var item = new Border { Width = 81, Height = 33, Background = Avalonia.Media.Brushes.Red };
        var adorner = new ResizeAdorner();
        AdornerLayer.SetAdornedElement(adorner, item);
        var canvas = new Canvas { Children = { item, adorner } };
        var window = new Window { Width = 400, Height = 300, Content = canvas };
        window.SetRenderScaling(1.5);
        item.PointerPressed += (_, e) => adorner.StartDrag(e);
        if (resizeRight)
            ResizeAdorner.SetResizeXDirection(item, ResizeXDirection.Right);
        try
        {
            window.Show();
            window.UpdateLayout();
            window.RenderScaling.Should().Be(1.5);
            item.Bounds.Width.Should().NotBe(item.Width, "layout rounds the half device pixel");
            window.MouseDown(new Point(5, 5), MouseButton.Left);
            window.MouseMove(new Point(21, 21), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(21, 21), MouseButton.Left);

            item.Height.Should().Be(33);
            if (resizeRight)
                item.Width.Should().Be(97, "the resize starts at the declared 81 DIP");
            else
            {
                item.Width.Should().Be(81);
                Canvas.GetLeft(item).Should().Be(16);
                Canvas.GetTop(item).Should().Be(16);
            }
        }
        finally { window.Close(); }
    }
}
