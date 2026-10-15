using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The Line control at run time and in the designer (#163). Every expectation was measured on vb6.exe, native and
/// p-code agreeing, by reading members back and by reading pixels with <c>Point</c> on a painted form (the oracle's
/// "The Line control").
/// </summary>
public class LineControlTests
{
    private static string Form(string scale, string controls) =>
        "VERSION 5.00\r\n"
      + "Begin VB.Form Form1 \r\n"
      + "   Caption         =   \"Form1\"\r\n"
      + scale
      + controls
      + "End\r\n"
      + "Attribute VB_Name = \"Form1\"\r\n";

    private const string Pixel =
        "   ClientHeight    =   3600\r\n   ClientWidth     =   4800\r\n   ScaleHeight     =   240\r\n"
      + "   ScaleMode       =   3  'Pixel\r\n   ScaleWidth      =   320\r\n";

    private const string Twips = "   ClientHeight    =   3000\r\n   ClientWidth     =   4000\r\n";

    // The About Dialog's.
    private const string User =
        "   ClientHeight    =   3555\r\n   ClientWidth     =   5730\r\n   ScaleHeight     =   2453.724\r\n"
      + "   ScaleMode       =   0  'User\r\n   ScaleWidth      =   5380.766\r\n";

    private static string Line(string name, string props = "", int? index = null) =>
        $"   Begin VB.Line {name} \r\n"
      + (index is { } i ? $"      Index           =   {i}\r\n" : "")
      + props
      + "   End\r\n";

    private static string At(double x1, double y1, double x2, double y2) =>
        $"      X1              =   {x1}\r\n      X2              =   {x2}\r\n      Y1              =   {y1}\r\n      Y2              =   {y2}\r\n";

    private static async Task<List<string>> Run(string frm, string code)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(frm);
        return (await ContainerRuntimeHarness.Run(ctx, env, code + "\r\n")).Select(v => v.Value?.ToString() ?? "").ToList();
    }

    [AvaloniaFact]
    public async Task A_line_with_nothing_set_reads_VB6s_defaults_with_VB6s_types()
    {
        var printed = await Run(Form(Pixel, Line("LineBare")), """
            Debug.Print TypeName(LineBare)
            Debug.Print LineBare.X1 & " " & LineBare.Y1 & " " & LineBare.X2 & " " & LineBare.Y2
            Debug.Print Hex(LineBare.BorderColor) & " " & LineBare.BorderStyle & " " & LineBare.BorderWidth & " " & LineBare.DrawMode & " " & LineBare.Visible
            Debug.Print TypeName(LineBare.X1) & " " & TypeName(LineBare.BorderWidth) & " " & TypeName(LineBare.BorderStyle) & " " & TypeName(LineBare.BorderColor) & " " & TypeName(LineBare.DrawMode)
            """);

        printed.Should().Equal("Line", "0 0 0 0", "80000008 1 1 13 True", "Single Integer Integer Long Integer");
    }

    [AvaloniaFact]
    public async Task On_a_pixel_form_the_file_is_in_pixels_and_a_coordinate_is_held_as_whole_twips()
    {
        var printed = await Run(Form(Pixel, Line("LineP", At(150, 45, 300, 45))), """
            Debug.Print LineP.X1 & " " & LineP.X2 & " " & LineP.Y1
            LineP.X1 = 10.5
            Debug.Print CStr(LineP.X1)
            LineP.X1 = 10.4
            Debug.Print CStr(LineP.X1)
            LineP.X1 = 11.5
            Debug.Print CStr(LineP.X1)
            LineP.X1 = -5
            Debug.Print CStr(LineP.X1)
            LineP.X1 = "12"
            Debug.Print CStr(LineP.X1)
            """);

        // 10.5 pixels is 157.5 twips, held as 158; 11.5 is 172.5, held as 172. Half to even.
        printed.Should().Equal("150 300 45", "10.53333", "10.4", "11.46667", "-5", "12");
    }

    [AvaloniaFact]
    public async Task On_a_user_scale_form_a_coordinate_reads_back_through_the_scales_Single_factor()
    {
        var printed = await Run(Form(User, Line("LineU", At(84.515, 1687.583, 5309.398, 1687.583))), """
            Debug.Print CStr(LineU.X1) & " " & CStr(LineU.X2) & " " & CStr(LineU.Y1)
            """);

        // 84.515 is 90 twips, and 1687.583 is 2445. Divided by the twips per unit in Single precision they read
        // 84.51466 and 1687.583, as VB6 reads them; in Double they would be 84.51465 and 1687.582.
        printed.Should().Equal("84.51466 5309.398 1687.583");
    }

    [AvaloniaFact]
    public async Task On_a_twips_form_a_fractional_coordinate_rounds_half_to_even()
    {
        var printed = await Run(Form(Twips, Line("LineT", At(100.5, 500.75, 2000.25, 1500))), """
            Debug.Print LineT.X1 & " " & LineT.X2 & " " & LineT.Y1 & " " & LineT.Y2
            """);

        printed.Should().Equal("100 2000 501 1500");
    }

    [AvaloniaFact]
    public async Task What_a_Line_refuses_is_380_and_what_it_takes_it_converts()
    {
        var printed = await Run(Form(Pixel, Line("LineP", At(150, 45, 300, 45))), """
            On Error Resume Next
            LineP.BorderWidth = 0
            Debug.Print "width 0: " & Err.Number & " " & LineP.BorderWidth
            Err.Clear
            LineP.BorderWidth = 2.5
            Debug.Print "width 2.5: " & Err.Number & " " & LineP.BorderWidth
            Err.Clear
            LineP.BorderWidth = 8193
            Debug.Print "width 8193: " & Err.Number & " " & LineP.BorderWidth
            Err.Clear
            LineP.BorderStyle = 7
            Debug.Print "style 7: " & Err.Number & " " & LineP.BorderStyle
            Err.Clear
            LineP.BorderStyle = -1
            Debug.Print "style -1: " & Err.Number & " " & LineP.BorderStyle
            Err.Clear
            LineP.BorderStyle = 6
            Debug.Print "style 6: " & Err.Number & " " & LineP.BorderStyle
            Err.Clear
            LineP.DrawMode = 0
            Debug.Print "mode 0: " & Err.Number & " " & LineP.DrawMode
            Err.Clear
            LineP.DrawMode = 17
            Debug.Print "mode 17: " & Err.Number & " " & LineP.DrawMode
            Err.Clear
            LineP.DrawMode = 16
            Debug.Print "mode 16: " & Err.Number & " " & LineP.DrawMode
            Err.Clear
            LineP.BorderColor = -1
            Debug.Print "color -1: " & Err.Number & " " & Hex(LineP.BorderColor)
            Err.Clear
            LineP.Visible = False
            Debug.Print "visible: " & Err.Number & " " & LineP.Visible
            Err.Clear
            LineP.Tag = "t"
            Debug.Print "tag: " & Err.Number & " " & LineP.Tag
            """);

        printed.Should().Equal(
            "width 0: 380 1", "width 2.5: 0 2", "width 8193: 0 8193",
            "style 7: 380 1", "style -1: 380 1", "style 6: 0 6",
            "mode 0: 380 13", "mode 17: 380 13", "mode 16: 0 16",
            "color -1: 380 80000008", "visible: 0 False", "tag: 0 t");
    }

    [AvaloniaFact]
    public async Task A_line_has_no_Left()
    {
        // vb6.exe refuses `LineP.Left` at compile time, "Method or data member not found", and late-bound it is 438.
        var act = () => Run(Form(Pixel, Line("LineP", At(150, 45, 300, 45))), "Debug.Print LineP.Left");

        await act.Should().ThrowAsync<VBMethodOrDataMemberNotFoundException>();
    }

    [AvaloniaFact]
    public async Task A_line_control_array_counts_loads_from_its_first_element_and_unloads()
    {
        var printed = await Run(Form(Pixel, Line("Line1", At(10, 30, 20, 30), 0) + Line("Line1", At(11, 31, 21, 31), 1)), """
            Debug.Print Line1.Count & " " & Line1.UBound & " " & Line1(1).X1
            Load Line1(2)
            Debug.Print Line1(2).Visible & " " & Line1(2).X1 & " " & Line1(2).Y1 & " " & Line1.Count
            Unload Line1(2)
            Debug.Print Line1.Count
            """);

        printed.Should().Equal("2 1 11", "False 10 30 3", "2");
    }

    // ── Drawing, read from a real Skia frame ─────────────────────────────────────────────────────────

    private static readonly Color Backdrop = Color.FromRgb(0, 0, 255);

    private static (Window window, Canvas canvas) Painted(string frm)
    {
        var (canvas, window, _, _) = ContainerRuntimeHarness.Laid(frm);
        canvas.Background = new SolidColorBrush(Backdrop);
        Dispatcher.UIThread.RunJobs();
        return (window, canvas);
    }

    // What each pixel of a run is: '#' black, '.' white, 'g' grey, 'b' the backdrop, '?' anything else.
    private static string Scan(Window window, Canvas canvas, IEnumerable<(int X, int Y)> pixels)
    {
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame was rendered");
        using var buffer = frame.Lock();
        var origin = canvas.TranslatePoint(new Point(0, 0), window) ?? throw new InvalidOperationException("not in the window");
        var chars = new List<char>();
        foreach (var (x, y) in pixels)
        {
            var offset = ((int)origin.Y + y) * buffer.RowBytes + ((int)origin.X + x) * 4;
            byte At(int i) => Marshal.ReadByte(buffer.Address, offset + i);
            var colour = buffer.Format == Avalonia.Platform.PixelFormat.Bgra8888
                ? Color.FromRgb(At(2), At(1), At(0))
                : Color.FromRgb(At(0), At(1), At(2));
            chars.Add(colour == Colors.Black ? '#' : colour == Colors.White ? '.' : colour == Color.FromRgb(0x80, 0x80, 0x80) ? 'g'
                : colour == Backdrop ? 'b' : '?');
        }
        return new string(chars.ToArray());
    }

    private static IEnumerable<(int, int)> Row(int y, int from, int to) => Enumerable.Range(from, to - from + 1).Select(x => (x, y));

    private static IEnumerable<(int, int)> Column(int x, int from, int to) => Enumerable.Range(from, to - from + 1).Select(y => (x, y));

    private static string Black(int style, int width) =>
        $"      BorderColor     =   &H00000000&\r\n      BorderStyle     =   {style}\r\n      BorderWidth     =   {width}\r\n";

    [AvaloniaFact]
    public void The_About_Dialogs_two_lines_draw_its_etched_rule()
    {
        var (window, canvas) = Painted(Form(User,
            Line("Line1", "      BorderColor     =   &H00808080&\r\n      BorderStyle     =   6  'Inside Solid\r\n"
                          + At(84.515, 1687.583, 5309.398, 1687.583), 1)
          + Line("Line1", "      BorderColor     =   &H00FFFFFF&\r\n      BorderWidth     =   2\r\n"
                          + At(98.6, 1697.936, 5309.398, 1697.936), 0)));

        // Measured on the console session: grey from 6 to 376 on row 163, white from 7 to 377 on row 164, and the white
        // line's end showing on row 163 at 377, where the grey line, drawn over it, stops.
        Scan(window, canvas, Row(162, 4, 12)).Should().Be("bbbbbbbbb");
        Scan(window, canvas, Row(163, 4, 12)).Should().Be("bbggggggg");
        Scan(window, canvas, Row(164, 4, 12)).Should().Be("bbb......");
        Scan(window, canvas, Row(165, 4, 12)).Should().Be("bbbbbbbbb");
        Scan(window, canvas, Row(163, 372, 380)).Should().Be("ggggg.bbb");
        Scan(window, canvas, Row(164, 372, 380)).Should().Be("......bbb");
    }

    [AvaloniaTheory]
    [InlineData(1, "########################################")]
    [InlineData(2, "##################......################")]
    [InlineData(3, "###...###...###...###...###...###...###.")]
    [InlineData(4, "#########......###......#########......#")]
    [InlineData(5, "#########...###...###...#########...###.")]
    [InlineData(6, "########################################")]
    [InlineData(0, "........................................")]
    public void A_one_pixel_line_is_dashed_by_its_BorderStyle_from_its_first_endpoint(int style, string expected)
    {
        var (window, canvas) = Painted(Form(Pixel, Line("Ln", Black(style, 1) + At(20, 20, 220, 20))));
        canvas.Background = Brushes.White;
        Dispatcher.UIThread.RunJobs();

        Scan(window, canvas, Row(20, 20, 59)).Should().Be(expected);
    }

    [AvaloniaFact]
    public void A_one_pixel_line_leaves_out_its_second_endpoint()
    {
        var (window, canvas) = Painted(Form(Pixel, Line("Ln", Black(1, 1) + At(20, 20, 220, 20))));

        Scan(window, canvas, Row(20, 16, 24)).Should().Be("bbbb#####");
        Scan(window, canvas, Row(20, 216, 224)).Should().Be("####bbbbb");
        Scan(window, canvas, Column(120, 16, 24)).Should().Be("bbbb#bbbb");
    }

    [AvaloniaTheory]
    [InlineData(2, "bbb##bbbb", "#####bbbb")]
    [InlineData(3, "bbb###bbb", "#####bbbb")]
    public void A_wider_line_is_solid_covers_its_second_endpoint_and_sits_across_its_first(int width, string across, string end)
    {
        // Dashed, and drawn solid anyway, as every style is past one pixel.
        var (window, canvas) = Painted(Form(Pixel, Line("Ln", Black(2, width) + At(20, 20, 220, 20))));

        Scan(window, canvas, Column(120, 16, 24)).Should().Be(across);
        Scan(window, canvas, Row(20, 216, 224)).Should().Be(end);
        Scan(window, canvas, Row(20, 20, 59)).Should().Be(new string('#', 40));
    }

    [AvaloniaFact]
    public void A_diagonal_steps_as_GDI_steps()
    {
        var (window, canvas) = Painted(Form(Pixel, Line("Ln", Black(1, 1) + At(300, 200, 310, 205))));

        var rows = Enumerable.Range(199, 8).Select(y => Scan(window, canvas, Row(y, 298, 312))).ToList();

        rows.Should().Equal(
            "bbbbbbbbbbbbbbb",
            "bb##bbbbbbbbbbb",
            "bbbb##bbbbbbbbb",
            "bbbbbb##bbbbbbb",
            "bbbbbbbb##bbbbb",
            "bbbbbbbbbb##bbb",
            "bbbbbbbbbbbbbbb",
            "bbbbbbbbbbbbbbb");
    }

    [AvaloniaFact]
    public void A_line_earlier_in_the_file_is_drawn_over_a_later_one()
    {
        var (window, canvas) = Painted(Form(Twips,
            Line("LineA", "      BorderColor     =   &H00FFFFFF&\r\n" + At(300, 1500, 1500, 1500))
          + Line("LineB", "      BorderColor     =   &H00000000&\r\n" + At(300, 1500, 1500, 1500))));

        Scan(window, canvas, Row(100, 50, 50)).Should().Be(".");
    }

    [AvaloniaFact]
    public void A_line_takes_no_clicks()
    {
        var (_, canvas) = Painted(Form(Pixel, Line("Ln", Black(1, 3) + At(20, 20, 220, 20))));

        canvas.Children.OfType<VBLine>().Single().IsHitTestVisible.Should().BeFalse();
    }

    // ── The designer ─────────────────────────────────────────────────────────────────────────────────

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private static ComponentInstanceViewModel Designed(string frm, string name)
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), frm, NullSink.Instance)!;
        var vm = new FormEditViewModel(null!, Substitute.For<IEventBus>(), null!, null!, null!, Substitute.For<ISettingsService>(),
            Substitute.For<ILocalizationService>()).Initialize(form);
        return vm.AllComponents.First(c => c.Name == name);
    }

    [AvaloniaFact]
    public void The_designer_selects_a_line_by_the_rectangle_its_pen_covers()
    {
        // 20,20 to 220,20 in pixels, a pen of 1, and a pixel to spare: 18,18, 205 by 5.
        var line = Designed(Form(Pixel, Line("Ln", Black(1, 1) + At(20, 20, 220, 20))), "Ln");

        (line.Left, line.Top, line.Width, line.Height).Should().Be((18.0, 18.0, 205.0, 5.0));
    }

    [AvaloniaFact]
    public void Moving_a_line_in_the_designer_moves_both_endpoints_in_its_containers_scale()
    {
        var line = Designed(Form(Pixel, Line("Ln", Black(1, 1) + At(20, 20, 220, 20))), "Ln");

        line.Left += 10;
        line.Top += 5;

        line.Instance.GetPropertyOrDefault(VBProperties.X1Property).Should().Be(30);
        line.Instance.GetPropertyOrDefault(VBProperties.X2Property).Should().Be(230);
        line.Instance.GetPropertyOrDefault(VBProperties.Y1Property).Should().Be(25);
        line.Instance.GetPropertyOrDefault(VBProperties.Y2Property).Should().Be(25);
        (line.Left, line.Top).Should().Be((28.0, 23.0));
    }
}
