using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Globalization;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

// Measured: #1087, issuecomment-6427372779. Pixel metrics themselves are not oracle assertions.
public class StatusBarDrawingAndEventsTests : WindowTestFixture
{
    private static (VBStatusBar Bar, ContainerRuntimeHarness.RecordingRoot Recorder) Loaded()
    {
        var (canvas, recorder, _, _) = ContainerRuntimeHarness.Recorded(StatusBarRuntimeTests.Form);
        return ((VBStatusBar)ContainerRuntimeHarness.Child(canvas, "StatusBar1"), recorder);
    }

    private static void Click(VBStatusBar bar, int count, bool right = false, double x = 20)
    {
        var window = TopLevel.GetTopLevel(bar)!;
        var point = bar.TranslatePoint(new Point(x, 12), window)!.Value;
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        bar.RaiseEvent(new PointerPressedEventArgs(bar, pointer, window, point, 0,
            new PointerPointProperties(right ? RawInputModifiers.RightMouseButton : RawInputModifiers.LeftMouseButton,
                right ? PointerUpdateKind.RightButtonPressed : PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, count));
        bar.RaiseEvent(new PointerReleasedEventArgs(bar, pointer, window, point, 1,
            new PointerPointProperties(RawInputModifiers.None,
                right ? PointerUpdateKind.RightButtonReleased : PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, right ? MouseButton.Right : MouseButton.Left));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PanelClick_is_between_MouseDown_and_MouseUp_for_either_button(bool right)
    {
        var (bar, recorder) = Loaded();
        Click(bar, 1, right);
        recorder.Calls.Select(c => c.Name).Should().Equal("StatusBar1_MouseDown", "StatusBar1_PanelClick", "StatusBar1_MouseUp", "StatusBar1_Click");
        recorder.Calls[0].Args!.Select(v => v.Value).Should().Equal(right ? 2 : 1, 0, 300f, 180f);
        recorder.Calls[1].Args!.Single().Value.Should().BeSameAs(bar.Panels.Items[0]);
        recorder.Calls.Clear();
        var window = (Window)TopLevel.GetTopLevel(bar)!;
        window.MouseMove(bar.TranslatePoint(new Point(20, 12), window)!.Value);
        recorder.Calls.Select(c => c.Name).Should().Equal("StatusBar1_MouseMove");
        recorder.Calls[0].Args!.Select(v => v.Value).Should().Equal(0, 0, 300f, 180f);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Double_click_has_the_measured_order_and_simple_style_has_no_panel_events(bool simple)
    {
        var (bar, recorder) = Loaded();
        bar.Style = simple ? 1 : 0;
        Click(bar, 1);
        Click(bar, 2);
        recorder.Calls.Select(c => c.Name.Replace("StatusBar1_", "")).Should().Equal(simple
            ? ["MouseDown", "MouseUp", "Click", "DblClick", "MouseUp"]
            : new[] { "MouseDown", "PanelClick", "MouseUp", "Click", "DblClick", "PanelDblClick", "MouseUp" });
    }

    [AvaloniaFact]
    public void The_grip_raises_no_events()
    {
        var (bar, recorder) = Loaded();
        Click(bar, 1, x: 390);
        recorder.Calls.Should().BeEmpty();
    }

    private static VBStatusBar DrawingBar(Func<long, bool>? keys = null)
    {
        var bar = new VBStatusBar(keys ?? (_ => false)) { Width = 400, Height = 25, FontSize = 11 };
        bar.Measure(new Size(400, 25));
        bar.Arrange(new Rect(0, 0, 400, 25));
        return bar;
    }

    private static byte[] Draw(VBStatusBar bar)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(400, 25));
        bitmap.Render(bar);
        var pixels = new byte[400 * 25 * 4];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(0, 0, 400, 25), pin.AddrOfPinnedObject(), pixels.Length, 400 * 4); }
        finally { pin.Free(); }
        return pixels;
    }

    private static string PictureFormPath()
    {
        var at = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (at != null && !System.IO.Directory.Exists(System.IO.Path.Join(at.FullName, "corpus"))) at = at.Parent;
        return System.IO.Path.Join(at!.FullName, "corpus", "inbox-controls", "saved", "statusbar-picture", "Form1.frm");
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void Saved_bitmap_and_icon_draw_and_runtime_assignment_can_clear_them(int index)
    {
        var path = PictureFormPath();
        var records = HexIDE.Runtime.Serialization.FrxDeserializer.Read(
            System.IO.File.ReadAllBytes(System.IO.Path.ChangeExtension(path, ".frx")), System.IO.File.ReadAllText(path));
        var record = records.OrderBy(p => p.Key).ElementAt(index);
        var bar = DrawingBar();
        var panel = bar.Panels.Items[0];
        var empty = Draw(bar);
        panel.Load(new System.Collections.Generic.Dictionary<string, object> { ["Picture"] = $"\"Form1.frx\":{record.Key:X4}" }, records);
        Draw(bar).Should().NotEqual(empty);
        panel.TryGetProperty("Picture", out var picture);
        panel.TrySetProperty("Picture", Vb6Value.Nothing);
        Draw(bar).Should().Equal(empty);
        panel.TrySetProperty("Picture", picture);
        Draw(bar).Should().NotEqual(empty);
        bar.Style = 1;
        var simple = Draw(bar);
        panel.TrySetProperty("Picture", Vb6Value.Nothing);
        Draw(bar).Should().Equal(simple);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async System.Threading.Tasks.Task LoadPicture_file_can_be_assigned_to_a_panel(int index)
    {
        var path = PictureFormPath();
        var records = HexIDE.Runtime.Serialization.FrxDeserializer.Read(
            System.IO.File.ReadAllBytes(System.IO.Path.ChangeExtension(path, ".frx")), System.IO.File.ReadAllText(path));
        var record = records.OrderBy(p => p.Key).ElementAt(index).Value;
        var file = System.IO.Path.Join(System.IO.Path.GetDirectoryName(path), "runtime-picture.bmp");
        try
        {
            System.IO.File.WriteAllBytes(file, record[28..]);
            var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(StatusBarRuntimeTests.Form);
            var values = await ContainerRuntimeHarness.Run(ctx, env, $"""
                Set p = LoadPicture("{file.Replace('/', '\\').Replace("\"", "\"\"")}")
                Set StatusBar1.Panels(1).Picture = p
                Debug.Print StatusBar1.Panels(1).Picture Is Nothing
                Set StatusBar1.Panels(1).Picture = Nothing
                Debug.Print StatusBar1.Panels(1).Picture Is Nothing
                """);
            values.Select(v => v.Value).Should().Equal(false, true);
        }
        finally { System.IO.File.Delete(file); }
    }

    [AvaloniaFact]
    public void Text_borders_alignment_visibility_and_simple_style_are_drawn()
    {
        var bar = DrawingBar();
        var panel = bar.Panels.Items[0];
        var empty = Draw(bar);
        panel.TrySetProperty("Text", new Vb6Value("Ready"));
        var text = Draw(bar);
        text.Should().NotEqual(empty);
        panel.TrySetProperty("Alignment", new Vb6Value(2));
        var aligned = Draw(bar);
        aligned.Should().NotEqual(text);
        panel.TrySetProperty("Bevel", new Vb6Value(0));
        var plain = Draw(bar);
        plain.Should().NotEqual(aligned);
        panel.TrySetProperty("Bevel", new Vb6Value(2));
        Draw(bar).Should().NotEqual(plain);
        panel.TrySetProperty("Visible", new Vb6Value(false));
        Draw(bar).Should().NotEqual(empty);
        bar.Style = 1;
        var simpleEmpty = Draw(bar);
        bar.SimpleText = "Simple";
        Draw(bar).Should().NotEqual(simpleEmpty);
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    public void Key_panels_use_their_own_Text_and_empty_Text_draws_nothing(int style)
    {
        var on = false;
        var bar = DrawingBar(_ => on);
        var panel = bar.Panels.Items[0];
        var empty = Draw(bar);
        panel.TrySetProperty("Style", new Vb6Value(style));
        Draw(bar).Should().Equal(empty);
        panel.TrySetProperty("Text", new Vb6Value("Own text"));
        Draw(bar).Should().NotEqual(empty);
        panel.TrySetProperty("Style", new Vb6Value(0));
        var enabledText = Draw(bar);
        panel.TrySetProperty("Style", new Vb6Value(style));
        Draw(bar).Should().NotEqual(enabledText, "the injected keyboard has its keys off; Kana stays greyed");
        if (style != 7)
        {
            on = true;
            Draw(bar).Should().Equal(enabledText);
            panel.TrySetProperty("Text", new Vb6Value(""));
            Draw(bar).Should().Equal(empty);
        }
    }

    [AvaloniaTheory]
    [InlineData(5)]
    [InlineData(6)]
    public void Time_and_date_draw_display_text_independently_of_Text(int style)
    {
        var bar = DrawingBar();
        var panel = bar.Panels.Items[0];
        var empty = Draw(bar);
        panel.TrySetProperty("Style", new Vb6Value(style));
        using (VbLocale.Use(CultureInfo.GetCultureInfo("en-GB")))
            bar.DisplayText(panel, new DateTime(2026, 10, 26, 20, 58, 0)).Should().Be(style == 5 ? "20:58" : "26/10/2026");
        var displayed = Draw(bar);
        displayed.Should().NotEqual(empty);
        panel.TrySetProperty("Text", new Vb6Value("Ignored"));
        Draw(bar).Should().Equal(displayed);
        panel.TryGetProperty("Text", out var text);
        text.Value.Should().Be("Ignored");
    }
}
