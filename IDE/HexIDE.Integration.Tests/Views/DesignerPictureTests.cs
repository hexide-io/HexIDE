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
        public Thickness ClientInset(ComponentInstance instance) => default;
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

    private static byte[] PixelsOf(Bitmap bitmap)
    {
        var stride = bitmap.PixelSize.Width * 4;
        var pixels = new byte[stride * bitmap.PixelSize.Height];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pin.AddrOfPinnedObject(), pixels.Length, stride); }
        finally { pin.Free(); }
        return pixels;
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

    [Fact]
    public void Rgba_pixels_become_Bgra() =>
        InProcessDesignerPictures.SwapRedAndBlue([1, 2, 3, 4, 5, 6, 7, 8]).Should().Equal(3, 2, 1, 4, 7, 6, 5, 8);
}
