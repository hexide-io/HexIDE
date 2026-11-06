using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Runtime.Components.InBox;
using Shapes = Avalonia.Controls.Shapes;
using SystemColors = Classic.CommonControls.SystemColors;

namespace HexIDE.Integration.Tests.Controls;

public class ImageListDesignerTests
{
    [AvaloniaFact]
    public void Designer_RendersRaisedBoxAndOriginalDrawing_WithThemeBrushes()
    {
        var control = ImageListComponentClass.Instance.InstantiateForDesigner(null!);
        var window = new Window { Width = 100, Height = 100, Content = control };
        var roles = new[] { SystemColors.ControlLightLightBrushKey, SystemColors.ControlLightBrushKey,
            SystemColors.ControlDarkBrushKey, SystemColors.ControlDarkDarkBrushKey, SystemColors.ControlBrushKey,
            SystemColors.ControlTextBrushKey };
        try
        {
            window.Show();
            foreach (var palette in new[] { Colors.Red, Colors.Blue })
            {
                for (var i = 0; i < roles.Length; i++)
                    window.Resources[roles[i]] = new SolidColorBrush(Color.FromRgb(palette.R, (byte)(i * 30), palette.B));
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame.Should().NotBeNull();
                control.Bounds.Size.Should().Be(new Size(38, 38));
                control.GetVisualDescendants().OfType<TextBlock>().Should().BeEmpty();
                var paths = control.GetVisualDescendants().OfType<Shapes.Path>().ToArray();
                paths.Length.Should().BeGreaterThanOrEqualTo(5);
                var origin = control.TranslatePoint(default, window)!.Value;
                var samples = new[] { new PixelPoint(10, 0), new PixelPoint(0, 10),
                    new PixelPoint(10, 1), new PixelPoint(1, 10), new PixelPoint(10, 36),
                    new PixelPoint(36, 10), new PixelPoint(10, 37), new PixelPoint(37, 10),
                    new PixelPoint(3, 3) };
                using (var pixels = frame!.Lock())
                {
                    for (var i = 0; i < samples.Length; i++)
                    {
                        var sample = samples[i];
                        var value = Marshal.ReadInt32(pixels.Address,
                            ((int)origin.Y + sample.Y) * pixels.RowBytes + ((int)origin.X + sample.X) * 4);
                        var expected = ((ISolidColorBrush)window.Resources[roles[i / 2]]!).Color;
                        var actual = Color.FromUInt32(unchecked((uint)value));
                        if (pixels.Format == PixelFormat.Rgba8888)
                            actual = Color.FromArgb(actual.A, actual.B, actual.G, actual.R);
                        actual.Should().Be(expected);
                    }
                }
                for (var i = 0; i < 4; i++)
                    paths[i].Fill.Should().BeSameAs(window.Resources[roles[i]]);
                control.GetVisualDescendants().OfType<Canvas>().First().Background.Should()
                    .BeSameAs(window.Resources[roles[4]]);
                var glyph = control.GetVisualDescendants().OfType<Canvas>().Single(c => c.Name == "Glyph");
                glyph.Bounds.Should().Be(new Rect(2, 2, 32, 32));
                glyph.Children.OfType<Shapes.Path>().Should().NotBeEmpty();
            }
        }
        finally
        {
            window.Close();
        }
    }
}
