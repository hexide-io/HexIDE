using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

// #3505: the designer asks for a picture and shows the pixels it gets back, so what draws a control can be elsewhere.
public class DesignerPictureTests
{
    private sealed class Source(Func<DesignerDrawRequest, DesignerPicture> draw) : IDesignerPictures
    {
        public List<DesignerDrawRequest> Requests { get; } = [];

        public DesignerPicture Draw(ComponentInstance instance, DesignerDrawRequest request)
        {
            Requests.Add(request);
            return draw(request);
        }
    }

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private sealed class RefusesToMeasure : Control
    {
        protected override Size MeasureOverride(Size availableSize) => throw new InvalidOperationException("No room");
    }

    private sealed class Refusing : IDesignerComponentClass
    {
        public string Name => "Refusing";
        public string VBTypeName => "Test.Refusing";
        public string ClassName => "Refusing";
        public bool IsVisual => true;
        public IReadOnlyList<PropertyClass> Properties { get; } = [];
        public IReadOnlyDictionary<string, PropertyClass> PropertiesByName { get; } = new Dictionary<string, PropertyClass>();
        public IReadOnlyList<EventClass> Events { get; } = [];
        public IReadOnlyList<PropertyChange> ApplyDesignerConsequences(ComponentInstance instance, PropertyClass property) => [];
        public bool IsWindowless => false;
        public (int Width, int Height) AddedSize => (600, 300);
        public DesignerInset ClientInset(ComponentInstance instance) => default;
        public Control InstantiateForDesigner(ComponentInstance instance) => new RefusesToMeasure();
    }

    private static FormEditViewModel Designer()
    {
        const string source = "VERSION 5.00\r\nBegin VB.Form Form1\r\n   ClientHeight = 4500\r\n   ClientWidth = 6000\r\nEnd\r\n";
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source, new NullSink())!;
        project.AddForm(form);
        return new FormEditViewModel(null!, new EventBus(), null!, null!, null!, null!, Substitute.For<ILocalizationService>())
            .Initialize(form);
    }

    private static ComponentInstanceViewModel Placed() =>
        new(Designer(), new ComponentInstance(new DesignerContractTests.InterfaceOnlyClass(), "Drawn1"));

    private static DesignerPicture Solid(DesignerDrawRequest request, byte blue, byte green, byte red)
    {
        var pixels = new byte[request.PixelWidth * request.PixelHeight * 4];
        for (var i = 0; i < pixels.Length; i += 4)
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (blue, green, red, 255);
        return new DesignerPicture(request.PixelWidth, request.PixelHeight, pixels, 0, 0, double.PositiveInfinity, double.PositiveInfinity);
    }

    // In BGRA whatever the bitmap's own order. A RenderTargetBitmap takes Skia's native order, which is RGBA on macOS,
    // so comparing its raw bytes with the designer's BGRA picture failed there with both showing red (#860).
    private static byte[] PixelsOf(Bitmap bitmap)
    {
        (bitmap.Format == PixelFormat.Bgra8888 || bitmap.Format == PixelFormat.Rgba8888)
            .Should().BeTrue($"the test reads 4-byte BGRA or RGBA pixels, and this bitmap is {bitmap.Format}");
        var stride = bitmap.PixelSize.Width * 4;
        var pixels = new byte[stride * bitmap.PixelSize.Height];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pin.AddrOfPinnedObject(), pixels.Length, stride); }
        finally { pin.Free(); }
        return bitmap.Format == PixelFormat.Rgba8888 ? InProcessDesignerPictures.SwapRedAndBlue(pixels) : pixels;
    }

    private static void Showing(ControlRenderer renderer, Action shown)
    {
        var window = new Window { Width = 200, Height = 80, Content = renderer };
        window.Show();
        try { shown(); }
        finally { window.Close(); }
    }

    private static ControlRenderer Renderer(IDesignerPictures pictures) => new() { Width = 100, Height = 30, Pictures = pictures };

    [AvaloniaFact]
    public void The_designer_shows_the_pixels_it_is_handed()
    {
        var source = new Source(request => Solid(request, 10, 20, 30));
        var renderer = Renderer(source);
        Showing(renderer, () =>
        {
            renderer.Component = Placed();
            Dispatcher.UIThread.RunJobs();

            var shown = renderer.Child.Should().BeOfType<Image>().Subject.Source.Should().BeAssignableTo<Bitmap>().Subject;
            shown.PixelSize.Should().Be(new PixelSize(100, 30));
            PixelsOf(shown).Should().Equal(Solid(new DesignerDrawRequest(100, 30, default), 10, 20, 30).Pixels);
        });
    }

    [AvaloniaFact]
    public void The_designer_asks_for_the_size_it_has_and_a_Line_s_scale()
    {
        var source = new Source(request => Solid(request, 0, 0, 0));
        var renderer = Renderer(source);
        var component = Placed();
        Showing(renderer, () =>
        {
            renderer.Component = component;
            Dispatcher.UIThread.RunJobs();

            source.Requests.Should().NotBeEmpty();
            source.Requests[^1].Should().Be(new DesignerDrawRequest(100, 30, component.LineTwipsPerUnit));
        });
    }

    [AvaloniaFact]
    public void A_picture_s_size_limits_are_the_control_s_on_the_designer()
    {
        var source = new Source(request => Solid(request, 0, 0, 0) with { MinWidth = 40, MinHeight = 20, MaxWidth = 500, MaxHeight = 200 });
        var renderer = Renderer(source);
        Showing(renderer, () =>
        {
            renderer.Component = Placed();
            Dispatcher.UIThread.RunJobs();

            (renderer.MinWidth, renderer.MinHeight, renderer.MaxWidth, renderer.MaxHeight).Should().Be((40d, 20d, 500d, 200d));
        });
    }

    [AvaloniaFact]
    public void A_refusal_to_draw_is_the_control_s_preview_failure()
    {
        var renderer = Renderer(new Source(_ => throw new InvalidOperationException("Not today")));
        var component = Placed();
        Showing(renderer, () =>
        {
            renderer.Component = component;
            Dispatcher.UIThread.RunJobs();

            component.PreviewFailure.Should().Be("Not today");
            renderer.Child.Should().BeOfType<Border>();
        });
    }

    [AvaloniaFact]
    public void In_process_the_picture_is_what_the_control_drew_and_the_control_is_gone()
    {
        var renderer = new ControlRenderer { Width = 100, Height = 30 };
        Showing(renderer, () =>
        {
            var picture = new InProcessDesignerPictures(renderer).Draw(
                new ComponentInstance(new DesignerContractTests.InterfaceOnlyClass(), "Drawn1"), new DesignerDrawRequest(10, 5, (1, 1)));

            (picture.PixelWidth, picture.PixelHeight, picture.Pixels.Length).Should().Be((10, 5, 200));
            picture.Pixels.Take(4).Should().Equal(0, 0, 255, 255);
            picture.Pixels.TakeLast(4).Should().Equal(0, 0, 255, 255);
            renderer.GetVisualChildren().Should().BeEmpty();
        });
    }

    [AvaloniaFact]
    public void In_process_a_control_that_refuses_is_removed_too()
    {
        var renderer = new ControlRenderer { Width = 100, Height = 30 };
        Showing(renderer, () =>
        {
            Action draw = () => new InProcessDesignerPictures(renderer).Draw(
                new ComponentInstance(new Refusing(), "Refused1"), new DesignerDrawRequest(10, 5, (1, 1)));

            draw.Should().Throw<InvalidOperationException>().WithMessage("No room");
            renderer.GetVisualChildren().Should().BeEmpty();
        });
    }

    // #5591: the drawer is the provider's, registered for the application, and the renderer asks it for each picture.
    [AvaloniaFact]
    public void Without_a_drawer_of_its_own_the_renderer_draws_with_the_applications()
    {
        var renderer = new ControlRenderer { Width = 100, Height = 30 };
        Showing(renderer, () =>
        {
            renderer.Pictures.Should().BeOfType<InProcessDesignerPictures>();
            renderer.Component = Placed();
            Dispatcher.UIThread.RunJobs();

            renderer.Child.Should().BeOfType<Image>();
        });
    }

    // #5591: the grant decides who draws; these rules keep the loan of a place in the designer's tree tidy.
    private sealed class Leaving(IDesignerDrawingSurface surface) : IDesignerPictures
    {
        public List<Border> Left { get; } = [];

        public DesignerPicture Draw(ComponentInstance instance, DesignerDrawRequest request)
        {
            var left = new Border();
            Left.Add(left);
            surface.Attach(left);
            return Solid(request, 10, 20, 30);
        }
    }

    [AvaloniaFact]
    public void A_control_the_drawer_left_attached_is_taken_out_when_the_draw_returns()
    {
        var renderer = new ControlRenderer { Width = 100, Height = 30 };
        var drawer = new Leaving(renderer);
        renderer.Pictures = drawer;
        Showing(renderer, () =>
        {
            var first = Placed();
            renderer.Component = first;
            Dispatcher.UIThread.RunJobs();
            var second = Placed();
            renderer.Component = second;
            Dispatcher.UIThread.RunJobs();

            drawer.Left.Should().HaveCount(2, "the second draw wasn't refused for the first one's control");
            drawer.Left.Should().AllSatisfy(left => left.GetVisualParent().Should().BeNull());
            renderer.GetVisualChildren().Should().ContainSingle().Which.Should().BeOfType<Image>();
            first.PreviewFailure.Should().BeNull();
            second.PreviewFailure.Should().BeNull();
        });
    }

    [AvaloniaFact]
    public void The_surface_takes_only_one_control_that_isnt_anywhere_and_lets_go_only_of_that()
    {
        var renderer = new ControlRenderer { Width = 100, Height = 30 };
        IDesignerDrawingSurface surface = renderer;
        Showing(renderer, () =>
        {
            FluentActions.Invoking(() => surface.Attach("a picture")).Should().Throw<ArgumentException>();
            var elsewhere = new Border();
            var parent = new Decorator { Child = elsewhere };
            FluentActions.Invoking(() => surface.Attach(elsewhere)).Should().Throw<ArgumentException>();
            parent.Child.Should().BeSameAs(elsewhere);
            renderer.GetVisualChildren().Should().BeEmpty();

            var drawn = new Border();
            surface.Attach(drawn);
            FluentActions.Invoking(() => surface.Attach(new Border())).Should().Throw<ArgumentException>();
            surface.Detach(new Border());
            renderer.GetVisualChildren().Should().ContainSingle().Which.Should().BeSameAs(drawn);

            surface.Detach(drawn);
            renderer.GetVisualChildren().Should().BeEmpty();
            drawn.GetVisualParent().Should().BeNull();
        });
    }

    // VB6 SP6: docs/vb6-fidelity-oracle/an-empty-listbox-an-empty-image-and-a-label-narrower-than-its-caption.md (#860).
    [AvaloniaFact]
    public void An_empty_Image_has_only_a_grey_dashed_edge_in_the_designer()
    {
        var renderer = new ControlRenderer { Width = 81, Height = 65 };
        Showing(renderer, () =>
        {
            renderer.Component = new ComponentInstanceViewModel(Designer(), new ComponentInstance(ImageComponentClass.Instance, "Image1"));
            Dispatcher.UIThread.RunJobs();
            var bitmap = renderer.Child.Should().BeOfType<Image>().Subject.Source.Should().BeAssignableTo<Bitmap>().Subject;
            var pixels = PixelsOf(bitmap);
            for (var y = 1; y < 64; y++)
                for (var x = 1; x < 80; x++)
                    pixels[(y * 81 + x) * 4 + 3].Should().Be(0);
            foreach (var edge in new[] {
                         Enumerable.Range(8, 60).Select(x => x * 4).ToArray(),
                         Enumerable.Range(8, 60).Select(x => (64 * 81 + x) * 4).ToArray(),
                         Enumerable.Range(8, 48).Select(y => y * 81 * 4).ToArray(),
                         Enumerable.Range(8, 48).Select(y => (y * 81 + 80) * 4).ToArray() })
            {
                edge.Count(i => pixels[i + 3] == 255).Should().Be(edge.Length / 2);
                foreach (var i in edge.Where(i => pixels[i + 3] != 0))
                    pixels.Skip(i).Take(4).Should().Equal(100, 100, 100, 255);
                // Ignore phase and corners: every complete interior run is three pixels on or off.
                var runs = new List<int>();
                var length = 1;
                for (var j = 1; j < edge.Length; j++)
                {
                    if (pixels[edge[j] + 3] == pixels[edge[j - 1] + 3]) length++;
                    else { runs.Add(length); length = 1; }
                }
                runs.Skip(1).Should().OnlyContain(run => run == 3);
            }
        });
    }

    [AvaloniaFact]
    public void An_Image_with_a_picture_keeps_its_picture_in_the_designer()
    {
        using var source = new RenderTargetBitmap(new PixelSize(81, 65), new Vector(96, 96));
        var red = new Border { Background = Brushes.Red, Width = 81, Height = 65 };
        red.Measure(new Size(81, 65));
        red.Arrange(new Rect(0, 0, 81, 65));
        source.Render(red);
        using var stream = new System.IO.MemoryStream();
        source.Save(stream, PngBitmapEncoderOptions.Default);
        var instance = new ComponentInstance(ImageComponentClass.Instance, "Image1");
        instance.SetProperty(VBProperties.PictureProperty, stream.ToArray());
        var renderer = new ControlRenderer { Width = 81, Height = 65 };
        Showing(renderer, () =>
        {
            renderer.Component = new ComponentInstanceViewModel(Designer(), instance);
            Dispatcher.UIThread.RunJobs();
            var bitmap = renderer.Child.Should().BeOfType<Image>().Subject.Source.Should().BeAssignableTo<Bitmap>().Subject;
            PixelsOf(bitmap).Should().Equal(PixelsOf(source));
        });
    }

    [Fact]
    public void Rgba_pixels_become_Bgra() =>
        InProcessDesignerPictures.SwapRedAndBlue([1, 2, 3, 4, 5, 6, 7, 8]).Should().Equal(3, 2, 1, 4, 7, 6, 5, 8);
}
