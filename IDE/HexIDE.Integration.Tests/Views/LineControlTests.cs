using System.Linq;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The Line control at run time and in the designer (#163). Every expectation was measured on vb6.exe, native and
/// p-code agreeing, by reading members back and by reading pixels with <c>Point</c> on a painted form (the oracle's
/// "The Line control").
/// </summary>
public class LineControlTests : WindowTestFixture
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

    private static string Line(string name, string props = "", int? index = null) =>
        $"   Begin VB.Line {name} \r\n"
      + (index is { } i ? $"      Index           =   {i}\r\n" : "")
      + props
      + "   End\r\n";

    private static string At(double x1, double y1, double x2, double y2) =>
        $"      X1              =   {x1}\r\n      X2              =   {x2}\r\n      Y1              =   {y1}\r\n      Y2              =   {y2}\r\n";

    private static string Black(int style, int width) =>
        $"      BorderColor     =   &H00000000&\r\n      BorderStyle     =   {style}\r\n      BorderWidth     =   {width}\r\n";

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
    public void The_designer_stacks_lines_as_a_running_form_does()
    {
        var form = Form(Pixel, Line("LineA", Black(1, 1) + At(20, 20, 220, 20)) + Line("LineB", Black(1, 1) + At(20, 30, 220, 30)));
        var vm = new FormEditViewModel(null!, Substitute.For<IEventBus>(), null!, null!, null!, Substitute.For<ISettingsService>(),
            Substitute.For<ILocalizationService>()).Initialize(
            new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), form, NullSink.Instance)!);

        // The canvas draws its list in order, the last on top, so the first Line in the file is drawn last (#1481, #1497).
        vm.Components.Select(c => c.Name).Should().Equal("LineB", "LineA");
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
