using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Avalonia.Threading;
using HexIDE.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

// VB6 SP6 designer measurements: #5121's supervisor comment, 2026-11-20 (measuring PR #5131).
public class SavedListDesignerPictureTests
{
    private sealed class Host(ControlRenderer renderer) : IDesignerDrawingSurface
    {
        internal Control? Visual;
        public void Attach(object visual) { Visual = (Control)visual; ((IDesignerDrawingSurface)renderer).Attach(visual); }
        public void Detach(object visual) => ((IDesignerDrawingSurface)renderer).Detach(visual);
        public void UpdateLayout() => renderer.UpdateLayout();
    }

    private static ComponentInstance List(bool combo, params string[] items)
    {
        var instance = new ComponentInstance(combo ? ComboBoxComponentClass.Instance : ListBoxComponentClass.Instance, "List2");
        if (combo) instance.SetProperty(VBProperties.ComboStyleProperty, VBComboStyle.SimpleCombo);
        if (items.Length > 0)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                writer.Write((ushort)items.Length);
                writer.Write((ushort)2);
                foreach (var item in items) { writer.Write((ushort)item.Length); writer.Write(Encoding.ASCII.GetBytes(item)); }
            }
            instance.SetProperty(VBProperties.ListProperty, stream.ToArray());
        }
        return instance;
    }

    private sealed class PictureSource(Host host) : IDesignerPictures
    {
        internal DesignerPicture? Picture;
        public DesignerPicture Draw(ComponentInstance instance, DesignerDrawRequest request) =>
            Picture = new InProcessDesignerPictures(host).Draw(instance, request);
    }

    private sealed class Sink : IDeserializeErrorSink { public void LogError(string error) => throw new InvalidOperationException(error); }

    private static void Draw(ComponentInstance instance, int height, Action<DesignerPicture, Control> check, double scaling)
    {
        var renderer = new ControlRenderer { Width = 160, Height = height };
        var host = new Host(renderer);
        var source = new PictureSource(host);
        renderer.Pictures = source;
        var project = new ProjectDefinition(VBProjectType.EXE, "Preview");
        var form = new FormDeserializer().Deserialize(project, "VERSION 5.00\nBegin VB.Form Form1\nEnd\n", new Sink())!;
        var owner = new FormEditViewModel(null!, new EventBus(), null!, null!, null!, null!, Substitute.For<ILocalizationService>()).Initialize(form);
        renderer.Component = new ComponentInstanceViewModel(owner, instance);
        var window = new Window { Width = 240, Height = 200, Content = renderer };
        window.SetRenderScaling(scaling);
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            renderer.Component.PreviewFailure.Should().BeNull();
            source.Picture.Should().NotBeNull();
            check(source.Picture!, host.Visual!);
        }
        finally { window.Close(); }
    }

    private static int Dark(DesignerPicture picture, int top, int bottom, int left = 3, int right = 65)
    {
        var count = 0;
        for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                var at = (y * picture.PixelWidth + x) * 4;
                if (picture.Pixels[at] + picture.Pixels[at + 1] + picture.Pixels[at + 2] < 600) count++;
            }
        return count;
    }

    // Ink means R+G+B < 600. Both captures must use greyscale antialiasing: colour fringes can
    // cross that threshold at different pixels on Windows, even with identical text geometry (#5121).
    private static (int Top, int Bottom, int Left, int Right) Ink(byte[] pixels, int width, int top, int bottom)
    {
        var points = Enumerable.Range(top, bottom - top).SelectMany(y =>
            Enumerable.Range(3, 62).Where(x =>
            {
                var at = (y * width + x) * 4;
                return pixels[at] + pixels[at + 1] + pixels[at + 2] < 600;
            }).Select(x => (X: x, Y: y))).ToArray();
        points.Should().NotBeEmpty();
        return (points.Min(p => p.Y), points.Max(p => p.Y), points.Min(p => p.X), points.Max(p => p.X));
    }

    [AvaloniaTheory]
    [InlineData(false, 1)]
    [InlineData(false, 1.5)]
    [InlineData(true, 1)]
    [InlineData(true, 1.5)]
    public void Designer_text_has_the_running_rows_ink_extents(bool arial, double scaling)
    {
        var instance = List(false, "s2", "s1", "s3");
        if (arial) instance.SetProperty(VBProperties.FontProperty, new VBFont("Arial", 12));
        Draw(instance, 90, (picture, _) =>
        {
            var runtime = (VBListBox)ListBoxComponentClass.Instance.Instantiate(instance);
            var window = new Window { Content = runtime, Width = 160, Height = 90 };
            window.SetRenderScaling(scaling);
            window.Show();
            try
            {
                runtime.UpdateLayout();
                TextOptions.SetTextRenderingMode(runtime, TextRenderingMode.Antialias);
                using var target = new RenderTargetBitmap(new PixelSize(160, 90), new Vector(96, 96));
                target.Render(runtime);
                var pixels = new byte[160 * 90 * 4];
                var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try { target.CopyPixels(new PixelRect(0, 0, 160, 90), pin.AddrOfPinnedObject(), pixels.Length, 160 * 4); }
                finally { pin.Free(); }
                var height = arial ? 18 : 13;
                for (var row = 0; row < 3; row++)
                    Ink(picture.Pixels, picture.PixelWidth, 2 + row * height, 2 + (row + 1) * height).Should()
                        .Be(Ink(pixels, 160, 2 + row * height, 2 + (row + 1) * height), $"row {row}, Arial={arial}, scaling={scaling}");
            }
            finally { window.Close(); }
        }, scaling);
    }

    [AvaloniaTheory]
    [InlineData(false, 1)]
    [InlineData(false, 1.5)]
    [InlineData(true, 1)]
    [InlineData(true, 1.5)]
    public void Saved_items_draw_every_row_at_the_running_controls_spacing(bool arial, double scaling)
    {
        var instance = List(false, "s2", "s1", "s3");
        if (arial) instance.SetProperty(VBProperties.FontProperty, new VBFont("Arial", 12));
        var height = arial ? 18 : 13;
        Draw(instance, 90, (picture, visual) =>
        {
            var list = (VBListBox)visual;
            list.Items.Cast<string>().Should().Equal("s2", "s1", "s3");
            list.SelectedIndex.Should().Be(-1);
            for (var row = 0; row < 3; row++)
                Dark(picture, 2 + row * height, 2 + (row + 1) * height).Should().BeGreaterThan(0, $"row {row} holds text");
            Dark(picture, 2 + 3 * height, 2 + 4 * height).Should().Be(0);
            var runtime = (VBListBox)ListBoxComponentClass.Instance.Instantiate(instance);
            var window = new Window { Content = runtime, Width = 160, Height = 90 };
            window.Show();
            try
            {
                runtime.UpdateLayout();
                runtime.GetRealizedContainers().Take(3).Select(c => c.Bounds.Height).Should().Equal(height, height, height);
                list.GetRealizedContainers().Take(3).Select(c => c.Bounds.Y).Should()
                    .Equal(runtime.GetRealizedContainers().Take(3).Select(c => c.Bounds.Y));
                list.GetRealizedContainers().Take(3).Select(c => c.Bounds.Height).Should().Equal(height, height, height);
            }
            finally { window.Close(); }
        }, scaling);
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.5)]
    public void A_sorted_lists_preview_keeps_saved_order(double scaling)
    {
        var instance = List(false, "b", "a", "c");
        instance.UnknownRawProperties.Add(new ComponentInstance.UnknownRawProperty("Sorted", ["Sorted = -1"]));
        Draw(instance, 70, (picture, visual) =>
        {
            ((VBListBox)visual).Items.Cast<string>().Should().Equal("b", "a", "c");
            for (var row = 0; row < 3; row++) Dark(picture, 2 + row * 13, 15 + row * 13).Should().BeGreaterThan(0);
        }, scaling);
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.5)]
    public void An_empty_lists_name_draws_only_in_the_designer(double scaling)
    {
        var instance = List(false);
        Draw(instance, 60, (picture, _) => Dark(picture, 2, 15).Should().BeGreaterThan(0), scaling);
        ((VBListBox)ListBoxComponentClass.Instance.Instantiate(instance)).ItemCount.Should().Be(0);
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.5)]
    public void An_overflowing_list_draws_three_rows_and_a_vertical_bar(double scaling)
    {
        Draw(List(false, Enumerable.Range(1, 12).Select(i => $"item{i}").ToArray()), 43, (picture, visual) =>
        {
            for (var row = 0; row < 3; row++) Dark(picture, 2 + row * 13, 15 + row * 13).Should().BeGreaterThan(0);
            visual.GetVisualDescendants().OfType<ScrollBar>().Should().Contain(b => b.IsVisible && b.Orientation == Avalonia.Layout.Orientation.Vertical);
            Dark(picture, 4, 39, 145, 158).Should().BeGreaterThan(0);
        }, scaling);
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.5)]
    public void A_simple_combos_picture_draws_three_list_rows(double scaling)
    {
        var instance = List(true, "p", "q", "r");
        instance.SetProperty(VBProperties.TextProperty, "Text");
        Draw(instance, 81, (picture, _) =>
        {
            Dark(picture, 2, 19).Should().BeGreaterThan(0);
            for (var row = 0; row < 3; row++) Dark(picture, 22 + row * 13, 35 + row * 13).Should().BeGreaterThan(0, $"combo row {row} holds text");
        }, scaling);
    }
}
