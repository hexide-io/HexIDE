using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Classic.Avalonia.Theme;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using ContainerRuntimeHarness = HexIDE.Integration.Tests.Views.ContainerRuntimeHarness;
using Shapes = Avalonia.Controls.Shapes;

namespace HexIDE.Runtime.Integration.Tests.Controls;

/// <summary>
/// Guards two render defects that shipped invisibly.
///
/// <para><b>The option button killed the process.</b> Classic.Avalonia.Theme 11.3.0.3 was compiled
/// against Avalonia 11, and its ClassicBorderDecorator.DrawRadioButtonBorder called
/// StreamGeometryContext.ArcTo(Point, Size, double, bool, SweepDirection) — an overload Avalonia 12
/// replaced. Every VB6 option button therefore threw MissingMethodException on the render thread, in
/// the designer and at runtime. It is NOT catchable in the app, and nothing reached the log, so the
/// assertion here is simply "a frame came back and we are still running".</para>
///
/// <para><b>The check box was always ticked.</b> SimpleTheme's CheckBox theme drives a part it calls
/// Path#checkMark; HexIDE's copied template names its tick CheckMarkPath, so the visibility rules
/// never matched and the tick painted in every state.</para>
///
/// <para>These are a class of bug — a precompiled-against-Avalonia-11 assembly failing only at render
/// time — that compiles cleanly and cannot be caught by unit tests of view models. Rendering for real
/// under Skia is the only thing that finds them.</para>
///
/// <para>The IDE's own drawing, the form designer's control bitmaps and the code window's syntax palette,
/// is guarded by <c>HexIDE.Integration.Tests</c>' <c>ClassicRenderTests</c>.</para>
/// </summary>
public class ClassicRenderTests
{
    // Measured at 96 dpi: docs/vb6-fidelity-oracle/a-rounded-shapes-corners.md (#916).
    [AvaloniaTheory]
    [InlineData(100, 60, ShapeTypes.RoundedRectangle, 0, 5, false)]
    [InlineData(100, 60, ShapeTypes.RoundedRectangle, 0, 5, true)]
    [InlineData(300, 300, ShapeTypes.RoundedRectangle, 0, 33, false)]
    [InlineData(300, 300, ShapeTypes.RoundedRectangle, 0, 33, true)]
    [InlineData(100, 60, ShapeTypes.RoundedSquare, 20, 5, false)]
    [InlineData(100, 60, ShapeTypes.RoundedSquare, 20, 5, true)]
    [InlineData(100, 60, ShapeTypes.Rectangle, 0, 0, false)]
    public void Shape_CornersMatchMeasuredInsets(int width, int height, ShapeTypes type, int left, int inset, bool backFill)
    {
        var shape = new VBShape
        {
            Width = width, Height = height, ShapeType = type,
            BorderWidth = 1, BorderStyle = BorderStyles.Solid,
            BorderColor = VBColor.FromColor(0, 0, 0), FillColor = VBColor.FromColor(0, 0, 0), FillStyle = backFill ? FillStyles.Transparent : FillStyles.Solid,
            BackStyle = backFill ? BackStyles.Opaque : BackStyles.Transparent, BackColor = VBColor.FromColor(0, 0, 0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        var window = BuildHost(shape);
        window.Width = width;
        window.Height = height;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            frame.Should().NotBeNull();
            var pixels = new int[width * height];
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                frame!.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), pixels.Length * 4, width * 4);
            }
            finally
            {
                handle.Free();
            }
            bool Black(int x, int y) => (pixels[y * width + x] & 0xff) < 128;
            var first = Enumerable.Range(0, width).First(x => Black(x, 0));
            first.Should().BeInRange(left + System.Math.Max(0, inset - 1), left + inset + 1);
            Black(width / 2, height / 2).Should().BeTrue();
            if (type == ShapeTypes.RoundedSquare)
            {
                Enumerable.Range(0, width).First(x => Black(x, height / 2)).Should().Be(left);
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < height; x++)
                    {
                        Black(left + x, y).Should().Be(Black(left + height - 1 - x, y));
                        Black(left + x, y).Should().Be(Black(left + x, height - 1 - y));
                    }
            }
        }
        finally
        {
            window.Close();
        }
    }

    // Measured grids: docs/vb6-fidelity-oracle/the-3-d-and-flat-edges-of-a-checkbox-optionbutton-textbox-and-commandbutton.md.
    [AvaloniaTheory]
    [InlineData("CheckBox", false)]
    [InlineData("CheckBox", true)]
    [InlineData("OptionButton", false)]
    [InlineData("OptionButton", true)]
    [InlineData("TextBox", false)]
    [InlineData("TextBox", true)]
    [InlineData("CommandButton", false)]
    public void MeasuredEdges_RenderWholePixelBands(string kind, bool flat)
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            $"VERSION 5.00\r\nBegin VB.Form Form1\r\n   Begin VB.{kind} Control1\r\n      Appearance = {(flat ? 0 : 1)}\r\n   End\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n", new NullSink())!;
        var instance = form.Components.Single(c => c.BaseClass.VBTypeName == "VB." + kind);
        var control = ((ComponentBaseClass)instance.BaseClass).Instantiate(instance);
        control.Width = 100;
        control.Height = 30;
        control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        control.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        control.Focusable = false;
        var window = BuildHost(control);
        SetEdgeColours(window);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            frame.Should().NotBeNull();
            var glyph = control.GetVisualDescendants().OfType<ClassicBorderDecorator>().FirstOrDefault();
            var origin = glyph?.TranslatePoint(default, window) ?? control.TranslatePoint(default, window)!.Value;
            var width = (int)(glyph?.Bounds.Width ?? control.Bounds.Width);
            var height = (int)(glyph?.Bounds.Height ?? control.Bounds.Height);
            var x = (int)origin.X;
            var y = (int)origin.Y;
            var top = width / 2;
            var left = height / 2;
            var raised = kind == "CommandButton";
            var outer = flat ? "#646464" : raised ? "#FFFFFF" : "#A0A0A0";
            var inner = flat ? "#646464" : raised ? "#E3E3E3" : "#696969";
            Pixel(frame!, x + top, y).Should().Be(Color.Parse(outer), "the outer top band");
            Pixel(frame!, x, y + left).Should().Be(Color.Parse(outer), "the outer left band");
            if (!flat || kind != "TextBox")
            {
                Pixel(frame!, x + top, y + 1).Should().Be(Color.Parse(inner), "the inner top band");
                Pixel(frame!, x + 1, y + left).Should().Be(Color.Parse(inner), "the inner left band");
            }
            if (kind == "CheckBox")
            {
                Pixel(frame!, x, y).Should().Be(Color.Parse(outer), "the outer top-left corner");
                Pixel(frame!, x + 1, y + 1).Should().Be(Color.Parse(inner), "the inner top-left corner");
            }
            if (kind is "CheckBox" or "CommandButton")
            {
                var bottomOuter = flat ? "#646464" : raised ? "#696969" : "#FFFFFF";
                var bottomInner = flat ? "#646464" : raised ? "#A0A0A0" : "#E3E3E3";
                Pixel(frame!, x + width / 2, y + height - 1).Should().Be(Color.Parse(bottomOuter));
                Pixel(frame!, x + width / 2, y + height - 2).Should().Be(Color.Parse(bottomInner));
                Pixel(frame!, x + width - 1, y + height / 2).Should().Be(Color.Parse(bottomOuter));
                Pixel(frame!, x + width - 2, y + height / 2).Should().Be(Color.Parse(bottomInner));
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(VBAppearance.Flat)]
    [InlineData(VBAppearance._3D)]
    public void UnmeasuredBorderStyleZero_KeepsStockTextBoxDrawing(VBAppearance appearance)
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            $"VERSION 5.00\r\nBegin VB.Form Form1\r\n   Begin VB.TextBox Text1\r\n      BorderStyle = 0\r\n      Appearance = {(int)appearance}\r\n   End\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n", new NullSink())!;
        var instance = form.Components.Single(c => c.BaseClass is TextBoxComponentClass);
        var box = ((ComponentBaseClass)instance.BaseClass).Instantiate(instance);
        box.Width = 100;
        box.Height = 30;
        box.Focusable = false;
        var stock = new TextBox { Padding = new Thickness(1), Width = 100, Height = 30, Focusable = false };
        var panel = new StackPanel { Children = { box, stock } };
        var window = BuildHost(panel);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            frame.Should().NotBeNull();
            box.GetVisualDescendants().OfType<ClassicBorderDecorator>().Should().BeEmpty();
            var a = box.TranslatePoint(default, window)!.Value;
            var b = stock.TranslatePoint(default, window)!.Value;
            for (var band = 0; band < 3; band++)
            {
                Pixel(frame!, (int)a.X + 50, (int)a.Y + band).Should()
                    .Be(Pixel(frame!, (int)b.X + 50, (int)b.Y + band));
                Pixel(frame!, (int)a.X + band, (int)a.Y + 15).Should()
                    .Be(Pixel(frame!, (int)b.X + band, (int)b.Y + 15));
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PressedCommandButton_KeepsSunkenEdge()
    {
        var button = new VBCommandButton { Width = 100, Height = 30 };
        var window = BuildHost(button);
        SetEdgeColours(window);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var point = button.TranslatePoint(new Point(50, 15), window)!.Value;
            window.MouseDown(point, Avalonia.Input.MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            frame.Should().NotBeNull();
            button.IsPressed.Should().BeTrue();
            var edge = button.GetVisualDescendants().OfType<ClassicBorderDecorator>().Single();
            edge.BorderStyle.Should().Be(ClassicBorderStyle.Sunken);
            var origin = edge.TranslatePoint(default, window)!.Value;
            Pixel(frame!, (int)origin.X + 10, (int)origin.Y).Should().Be(Color.Parse("#A0A0A0"));
            Pixel(frame!, (int)origin.X + 10, (int)origin.Y + 1).Should().Be(Color.Parse("#696969"));
            window.MouseUp(point, Avalonia.Input.MouseButton.Left);
        }
        finally { window.Close(); }
    }

    private static void SetEdgeColours(Window window)
    {
        window.Resources[Classic.CommonControls.SystemColors.ControlDarkBrushKey] = new SolidColorBrush(Color.Parse("#A0A0A0"));
        window.Resources[Classic.CommonControls.SystemColors.ControlDarkDarkBrushKey] = new SolidColorBrush(Color.Parse("#696969"));
        window.Resources[Classic.CommonControls.SystemColors.ControlLightBrushKey] = new SolidColorBrush(Color.Parse("#E3E3E3"));
        window.Resources[Classic.CommonControls.SystemColors.ControlLightLightBrushKey] = Brushes.White;
        window.Resources[Classic.CommonControls.SystemColors.WindowFrameBrushKey] = new SolidColorBrush(Color.Parse("#646464"));
    }

    private static Color Pixel(Bitmap bitmap, int x, int y)
    {
        var pixel = new byte[4];
        var handle = GCHandle.Alloc(pixel, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(x, y, 1, 1), handle.AddrOfPinnedObject(), 4, 4);
            // In the bitmap's own order: a RenderTargetBitmap is RGBA on macOS and BGRA elsewhere.
            return bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888
                ? Color.FromArgb(pixel[3], pixel[0], pixel[1], pixel[2])
                : Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
        }
        finally { handle.Free(); }
    }

    [AvaloniaFact]
    public void SingleLineTextBox_ShortHeight_HoldsItsTextWithoutAVerticalBar()
    {
        foreach (var height in new[] { 19, 21, 24, 25 })
        {
            // Keep the line small enough to fit on Windows too; the next test covers taller fonts being clipped.
            var box = new VBTextBox { Text = "hello", FontSize = 11, Width = 120, Height = height };
            var window = BuildHost(box);
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame.Should().NotBeNull();

                var viewer = box.GetVisualDescendants().OfType<ScrollViewer>().Single();
                viewer.Extent.Height.Should().BeLessThanOrEqualTo(viewer.Viewport.Height);
                var presenter = box.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Single();
                presenter.TextLayout.Height.Should().BeLessThanOrEqualTo(viewer.Viewport.Height);
                box.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>()
                    .Where(bar => bar.Orientation == Avalonia.Layout.Orientation.Vertical)
                    .Should().OnlyContain(bar => !bar.IsVisible);
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void SingleLineTextBox_InsetFollowsFontAndHeight()
    {
        var box = new VBTextBox { Text = "hello", Width = 120, Height = 19 };
        var window = BuildHost(box);
        try
        {
            window.Show();
            foreach (var fontSize in new[] { 13d, 11d })
            {
                box.FontSize = fontSize;
                foreach (var height in new[] { 19, 21, 24, 25, 19 })
                {
                    box.Height = height;
                    Dispatcher.UIThread.RunJobs();
                    using var frame = window.CaptureRenderedFrame();
                    frame.Should().NotBeNull();
                    var viewer = box.GetVisualDescendants().OfType<ScrollViewer>().Single();
                    var presenter = box.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Single();
                    // Where the line can fit inside the borders it must; where it cannot (Windows' default font at 13
                    // lays out 17.3 tall, more than a 19-high box holds), it is clipped, never scrolled.
                    var room = height - box.BorderThickness.Top - box.BorderThickness.Bottom;
                    if (presenter.TextLayout.Height <= room)
                        presenter.TextLayout.Height.Should().BeLessThanOrEqualTo(viewer.Viewport.Height);
                    box.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>()
                        .Where(bar => bar.Orientation == Avalonia.Layout.Orientation.Vertical)
                        .Should().OnlyContain(bar => !bar.IsVisible);
                    if (height == 25)
                        box.Padding.Should().Be(new Thickness(1));
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// A window carrying the VB6 control themes plus the SystemColors brushes they resolve. This project's
    /// TestApp merges the runner's dictionaries as well (<c>TestApp.cs:31</c>), and the window still adds
    /// its own, as <c>HexIDE.Integration.Tests</c>' copy does.
    /// </summary>
    private static Window BuildHost(Control content)
    {
        var window = new Window { Width = 240, Height = 120, Background = Brushes.White };

        window.Resources.MergedDictionaries.Add(
            new ResourceInclude(new Uri("avares://HexIDE.Runtime/"))
            {
                Source = new Uri("avares://HexIDE.Runtime/BuiltinControls/Resources.axaml"),
            });

        window.Resources[Classic.CommonControls.SystemColors.WindowBrushKey] = new SolidColorBrush(Colors.White);
        window.Resources[Classic.CommonControls.SystemColors.WindowTextBrushKey] = new SolidColorBrush(Colors.Black);
        window.Resources[Classic.CommonControls.SystemColors.GrayTextBrushKey] = new SolidColorBrush(Color.Parse("#808080"));

        window.Content = content;
        return window;
    }

    private static Avalonia.Media.Imaging.WriteableBitmap? RenderOnce(Control content)
    {
        var window = BuildHost(content);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        window.Close();
        return frame;
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeCaption_HasAntialiasedPixels(bool option)
    {
        Control control = option
            ? new VBOptionButton { Content = "Option0", FontFamily = new FontFamily("Liberation Sans"), FontSize = 11 }
            : new VBCheckBox { Content = "Check0", FontFamily = new FontFamily("Liberation Sans"), FontSize = 11 };
        control.Width = 160;
        control.Height = 32;
        control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        control.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        using var frame = RenderOnce(control);
        frame.Should().NotBeNull();
        CaptionColours(frame!, new PixelRect(25, 8, 120, 16)).Should().BeGreaterThan(2,
            "caption pixels must include intermediate greys, excluding the bullet and border");
    }

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private const string AccessKeyForm = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.CommandButton Command1
              Caption = "&OK"
              Height = 480
              Width = 2400
           End
           Begin VB.Label Label1
              Caption = "Centred &label"
              Alignment = 2
              Top = 600
              Height = 480
              Width = 2400
           End
        End
        Attribute VB_Name = "Form1"
        """;

    // CommandButton and Label measurements: docs/vb6-fidelity-oracle/a-captions-access-key.md (#919).
    [AvaloniaTheory]
    [InlineData("Command1", false, false)]
    [InlineData("Command1", false, true)]
    [InlineData("Command1", true, false)]
    [InlineData("Command1", true, true)]
    [InlineData("Label1", false, false)]
    [InlineData("Label1", false, true)]
    [InlineData("Label1", true, false)]
    [InlineData("Label1", true, true)]
    public void ControlCaption_ShowsAccessKey(string name, bool designer, bool afterPointerPress)
    {
        Control content;
        Control control;
        if (designer)
        {
            var component = FindComponent(Deserialize(AccessKeyForm), name);
            content = control = ((IDesignerComponentClass)component.BaseClass).InstantiateForDesigner(component);
        }
        else
        {
            var (root, canvas, _, _) = ContainerRuntimeHarness.Spawn(AccessKeyForm);
            content = root;
            control = canvas.Children.Single(c => VBProps.GetName(c) == name);
        }

        var window = BuildHost(content);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            if (afterPointerPress)
            {
                window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.None, null);
                window.GetValue(AccessText.ShowAccessKeyProperty).Should().BeTrue();
                window.MouseDown(new Point(200, 100), MouseButton.Left);
                window.MouseUp(new Point(200, 100), MouseButton.Left);
                window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.None, null);
                Dispatcher.UIThread.RunJobs();
                window.GetValue(AccessText.ShowAccessKeyProperty).Should().BeFalse();
            }

            control.GetVisualDescendants().OfType<AccessText>().Single().ShowAccessKey.Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    private static FormDefinition Deserialize(string source) =>
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source.ReplaceLineEndings("\r\n"), new NullSink())!;

    private static ComponentInstance FindComponent(FormDefinition form, string name) =>
        form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == name);

    // #913: the runtime paint (#752) and designer paint (oracle PR #5217) agree.
    private static FormDefinition ButtonStyleForm(int style) => new FormDeserializer().Deserialize(
        new ProjectDefinition(VBProjectType.EXE, "P"), $$"""
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.CommandButton Command1
              BackColor = &H000000FF&
              Style = {{style}}
              Height = 480
              Width = 2400
           End
        End
        Attribute VB_Name = "Form1"

        """.ReplaceLineEndings("\r\n"), new NullSink())!;

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void CommandButton_Style_selects_the_runtime_face(int style)
    {
        var button = CommandButtonComponentClass.Instance.Instantiate(ButtonStyleForm(style).Components.Single(c => c.BaseClass is CommandButtonComponentClass));
        button.Width = 160;
        button.Height = 32;
        button.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        button.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        using var frame = RenderOnce(button);
        frame.Should().NotBeNull();
        var expected = style == 1 ? Colors.Red
            : ((ISolidColorBrush)VBColor.FromSystemColor(VbSystemColor.Btnface).ToBrush()).Color;
        Pixel(frame!, 10, 10).Should().Be(expected);
    }

    [AvaloniaFact]
    public void CommandButton_Style_changes_repaint_the_stored_BackColor()
    {
        var button = (VBCommandButton)CommandButtonComponentClass.Instance.Instantiate(
            ButtonStyleForm(1).Components.Single(c => c.BaseClass is CommandButtonComponentClass));
        button.Width = 160;
        button.Height = 32;
        button.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        button.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        var window = BuildHost(button);
        try
        {
            window.Show();
            button.Style = VBButtonStyle.Standard;
            Dispatcher.UIThread.RunJobs();
            using var standard = window.CaptureRenderedFrame();
            var face = ((ISolidColorBrush)VBColor.FromSystemColor(VbSystemColor.Btnface).ToBrush()).Color;
            Pixel(standard!, 10, 10).Should().Be(face);

            button.Style = VBButtonStyle.Graphical;
            Dispatcher.UIThread.RunJobs();
            using var graphical = window.CaptureRenderedFrame();
            Pixel(graphical!, 10, 10).Should().Be(Colors.Red);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CommandButton_Standard_runtime_BackColor_assignment_keeps_the_face_and_reads_back_red()
    {
        var component = ButtonStyleForm(0).Components.Single(c => c.BaseClass is CommandButtonComponentClass);
        component.SetProperty(VBProperties.BackColorProperty, VBColor.FromSystemColor(VbSystemColor.Btnface));
        var button = CommandButtonComponentClass.Instance.Instantiate(component);
        button.Width = 160;
        button.Height = 32;
        button.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        button.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        var window = BuildHost(button);
        try
        {
            window.Show();
            // This assignment is unmeasured; #913 directs the same paint rule for loaded and assigned colours.
            HexIDE.Runtime.AvaloniaInterop.AvaloniaInteroperability.TrySet(button, VBProperties.BackColorProperty,
                new HexIDE.Runtime.Interpreter.Vb6Value(255L)).Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            using var assigned = window.CaptureRenderedFrame();
            Pixel(assigned!, 10, 10).Should().Be(
                ((ISolidColorBrush)VBColor.FromSystemColor(VbSystemColor.Btnface).ToBrush()).Color);
            HexIDE.Runtime.AvaloniaInterop.AvaloniaInteroperability.TryGet(button, VBProperties.BackColorProperty,
                out var backColor).Should().BeTrue();
            backColor.Value.Should().Be(255L);
        }
        finally { window.Close(); }
    }

    private static int CaptionColours(Bitmap bitmap, PixelRect region) => new HashSet<int>(ReadPixels(bitmap, region)).Count;

    private static int[] ReadPixels(Bitmap bitmap, PixelRect region)
    {
        var pixels = new int[region.Width * region.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(region, handle.AddrOfPinnedObject(), pixels.Length * 4, region.Width * 4);
            return pixels;
        }
        finally
        {
            handle.Free();
        }
    }

    // ── The crash ────────────────────────────────────────────────────────────────────────────────

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void OptionButton_Renders_WithoutKillingTheProcess(bool? isChecked)
    {
        // Before the package bump this did not fail an assertion — it terminated the test host.
        var frame = RenderOnce(new VBOptionButton { Content = "Standard delivery", IsChecked = isChecked });
        frame.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void OptionButton_Flat_Renders()
    {
        // The Flat appearance swaps to the FlatVBOptionButton theme, which is BasedOn the crashing one.
        var frame = RenderOnce(new VBOptionButton { Content = "Express", Appearance = VBAppearance.Flat });
        frame.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void CheckBox_Renders_WithoutKillingTheProcess()
    {
        var frame = RenderOnce(new VBCheckBox { Content = "Signature required" });
        frame.Should().NotBeNull();
    }

    // ── The always-ticked check box ──────────────────────────────────────────────────────────────

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(null, true)]   // VB6 Value = 2 (Greyed): tick shown, in grey
    public void CheckBox_TickVisibility_FollowsState(bool? isChecked, bool expectedVisible)
    {
        var box = new VBCheckBox { Content = "Priority handling", IsChecked = isChecked };
        var window = BuildHost(box);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tick = box.GetVisualDescendants().OfType<Shapes.Path>()
            .FirstOrDefault(p => p.Name == "CheckMarkPath");

        tick.Should().NotBeNull("the check box template must still contain CheckMarkPath");
        // Before the fix this was true in every state — an unticked box rendered ticked.
        tick!.IsVisible.Should().Be(expectedVisible);

        window.Close();
    }

    [AvaloniaFact]
    public void CheckBox_Glyph_StaysThirteenSquare_WhenTickHidden()
    {
        // Hiding the tick collapsed the decorator to its border, because it was measured from its
        // child. The explicit 13x13 is what keeps an unticked box the same size as a ticked one.
        var box = new VBCheckBox { Content = "Priority handling", IsChecked = false };
        var window = BuildHost(box);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var glyph = box.GetVisualDescendants().OfType<ClassicBorderDecorator>()
            .FirstOrDefault(d => d.Name == "CheckMark");

        glyph.Should().NotBeNull();
        glyph!.Bounds.Width.Should().Be(13);
        glyph.Bounds.Height.Should().Be(13);

        window.Close();
    }
}
