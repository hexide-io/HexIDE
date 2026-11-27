using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Headless.XUnit;

namespace HexIDE.Runtime.Integration.Tests.Views;

// Every VB6 expectation was measured on SP6, native and p-code alike, in the coordinate oracle's #4693 sections
// (oracle PRs #4723 and #4751).
public partial class CoordinateSystemTests
{
    [AvaloniaFact]
    public void Saved_User_scales_without_extents_use_client_twips()
    {
        var frm = Form.Replace("ClientWidth = 4680", "ClientWidth = 2400\nScaleMode = 0")
            .Replace("ClientHeight = 3195", "ClientHeight = 1800")
            .Replace("Width = 2415", "Width = 1215\nScaleMode = 0").Replace("Height = 1815", "Height = 615");
        var run = RunLoad(frm, "Debug.Print " + Snapshot("Me.") + "\nDebug.Print CStr(ScaleX(1, vbInches, vbUser))\nDebug.Print " + Snapshot("Picture1."));
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("0|0|0|2400|1800", "1440", "0|0|0|1155|555");
    }

    [AvaloniaTheory]
    [InlineData("Me", "ScaleX")]
    [InlineData("Me", "ScaleY")]
    [InlineData("Picture1", "ScaleX")]
    public void Late_bound_conversions_return_the_measured_length(string target, string method)
    {
        var run = RunLoad(Form, $"Dim o As Object\nSet o = {target}\nDebug.Print CStr(o.{method}(1, vbInches, vbTwips))");
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("1440");
    }

    [AvaloniaFact]
    public void Late_bound_PictureBox_Scale_uses_its_mapping()
    {
        var run = RunLoad(Form, "Dim o As Object\nSet o = Picture1\no.Scale (0, 0)-(10, 10)\nDebug.Print CStr(Picture1.ScaleMode) & \"|\" & CStr(Picture1.ScaleWidth)");
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("0|10");
    }

    [AvaloniaTheory]
    [InlineData("o.Scale (0, 0)-(10, 10)")]
    [InlineData("o.ScaleX 1, vbInches, vbTwips")]
    [InlineData("Call o.ScaleY(1, vbInches, vbTwips)")]
    [InlineData("Dim v\nv = o.ScaleX(1, vbInches, vbTwips)")]
    public void Late_bound_TextBox_scale_members_raise_438(string statement)
    {
        var frm = Form.Replace("VB.PictureBox Picture1", "VB.TextBox Text1");
        var run = RunLoad(frm, "Dim o As Object\nSet o = Text1\nOn Error Resume Next\n" + statement + "\nDebug.Print CStr(Err.Number)");
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("438");
    }

    private const string MetricLineForm = """
        VERSION 5.00
        Begin VB.Form Form1
           ClientWidth = 6000
           ClientHeight = 4000
           Begin VB.PictureBox Picture2
              Width = 3000
              Height = 2000
              ScaleMode = 7
              Begin VB.Line Line2
                 X1 = 0.5
                 Y1 = 0.5
                 X2 = 2
                 Y2 = 1.5
              End
           End
        End
        Attribute VB_Name = "Form1"

        """;

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Centimetre_Line_endpoints_match_the_approved_precision(int coordinate)
    {
        var member = new[] { "X1", "Y1", "X2", "Y2" }[coordinate];
        var run = RunLoad(MetricLineForm, $"Debug.Print CStr(CDbl(Line2.{member}))");
        run.Errors.Should().BeEmpty();
        run.Printed.Should().ContainSingle();
        var actual = float.Parse(run.Printed[0], CultureInfo.InvariantCulture);
        if (coordinate < 2)
        {
            // VB6 measured X1 and Y1: Single 0.499181061983109. Exactly one ULP is approved by
            // Desk card q-990-centimetres; see the coordinate oracle's "Centimetres and millimetres,
            // to the last bit" section. Do not pin HexIDE's lower value as VB6's value.
            const float measured = (float)0.499181061983109;
            new[] { MathF.BitDecrement(measured), measured, MathF.BitIncrement(measured) }.Should().Contain(actual);
        }
        else
            actual.Should().Be(coordinate == 2 ? (float)2.00025200843811 : (float)1.49930703639984);
    }

    [AvaloniaFact]
    public void Centimetre_Line_ends_are_stored_as_whole_twips()
    {
        var run = RunLoad(MetricLineForm, "Picture2.ScaleMode = 1\nDebug.Print CStr(Line2.X1) & \"|\" & CStr(Line2.Y1) & \"|\" & CStr(Line2.X2) & \"|\" & CStr(Line2.Y2)");
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("283|283|1134|850");
    }
    // Issue #4693 final comment, oracle PR #5514: SP6 native/p-code and IDE agree.
    [AvaloniaTheory]
    [InlineData("", "0.4991811|1.000126|2.499433|3.000378")]
    [InlineData("Picture2.ScaleMode = 1", "283|567|1417|1701")]
    [InlineData("Picture2.ScaleMode = 3", "18.86667|37.8|94.46667|113.4")]
    [InlineData("Picture2.ScaleMode = 5", "0.1965278|0.39375|0.9840278|1.18125")]
    [InlineData("Picture2.ScaleMode = 6", "4.99181|10.00126|24.99433|30.00378")]
    [InlineData("Picture2.ScaleMode = 2", "14.15|28.35|70.85|85.05")]
    [InlineData("Picture2.ScaleMode = 4", "2.358333|2.3625|11.80833|7.0875")]
    [InlineData("Picture2.ScaleMode = 3\nPicture2.ScaleMode = 7", "0.4991811|1.000126|2.499433|3.000378")]
    [InlineData("Picture2.Scale (0, 0)-(100, 50)", "9.982363|10|49.98236|30")]
    // The container's origin is added (#5530, oracle PR #5534, measured on Lines loaded in other units).
    [InlineData("Picture2.Scale (10, 20)-(110, 70)", "19.98236|30|59.98236|50")]
    [InlineData("Picture2.ScaleMode = 3\nPicture2.ScaleLeft = -5", "13.86667|37.8|89.46667|113.4")]
    public void Line_ends_follow_the_current_container_scale(string setup, string expected)
    {
        var frm = MetricLineForm.Replace("Width = 3000", "Width = 2835\nBorderStyle = 0")
            .Replace("Height = 2000", "Height = 2835")
            .Replace("Y1 = 0.5", "Y1 = 1").Replace("X2 = 2", "X2 = 2.5").Replace("Y2 = 1.5", "Y2 = 3");
        var centimetres = setup == "" || setup.EndsWith("= 7", StringComparison.Ordinal);
        var x1 = centimetres ? "CStr(CDbl(Line2.X1))" : "CStr(Line2.X1)";
        var run = RunLoad(frm, setup + "\nDebug.Print " + x1 + " & \"|\" & CStr(Line2.Y1) & \"|\" & CStr(Line2.X2) & \"|\" & CStr(Line2.Y2)"
            + "\nDebug.Print TypeName(Line2.X1) & \"|\" & TypeName(Line2.Y1) & \"|\" & TypeName(Line2.X2) & \"|\" & TypeName(Line2.Y2)");
        run.Errors.Should().BeEmpty();
        run.Printed.Should().HaveCount(2);
        run.Printed[1].Should().Be("Single|Single|Single|Single");
        if (centimetres)
        {
            // VB6's measured X1 is Single 0.499181061983109; q-990-centimetres accepts exactly one step.
            const float measured = (float)0.499181061983109;
            var parts = run.Printed[0].Split('|');
            new[] { MathF.BitDecrement(measured), measured, MathF.BitIncrement(measured) }
                .Should().Contain(float.Parse(parts[0], CultureInfo.InvariantCulture));
            parts[1..].Should().Equal(expected.Split('|')[1..]);
        }
        else
            run.Printed[0].Should().Be(expected);
    }

    private const string LineScaleForm = """
        VERSION 5.00
        Begin VB.Form Form1
           ClientWidth = 9200
           ClientHeight = 4800
           Begin VB.PictureBox Picture2
              Width = 2835
              Height = 2835
              BorderStyle = 0
              Begin VB.Line Line2
                 X1 = 300
                 Y1 = 300
                 X2 = 1500
                 Y2 = 1500
              End
           End
           Begin VB.PictureBox Picture3
              Width = 2835
              Height = 2835
              BorderStyle = 0
              ScaleMode = 3
              ScaleWidth = 189
              ScaleHeight = 189
              Begin VB.Line Line3
                 X1 = 20
                 Y1 = 20
                 X2 = 100
                 Y2 = 100
              End
           End
           Begin VB.PictureBox Picture4
              Width = 2835
              Height = 2835
              BorderStyle = 0
              ScaleMode = 5
              ScaleWidth = 1.969
              ScaleHeight = 1.969
              Begin VB.Line Line4
                 X1 = 0.25
                 Y1 = 0.5
                 X2 = 1
                 Y2 = 1.25
              End
           End
           Begin VB.Line Line1
              X1 = 300
              Y1 = 3200
              X2 = 1200
              Y2 = 4000
           End
        End
        Attribute VB_Name = "Form1"

        """;

    private static string LineEnds(string line) =>
        $"CStr({line}.X1) & \"|\" & CStr({line}.Y1) & \"|\" & CStr({line}.X2) & \"|\" & CStr({line}.Y2)";

    // #5530: the-line-control.md records the scale changes; the issue's final comment records the origin rows.
    private static readonly (string Statement, string Twips, string Pixels, string Inches)[] PictureBoxLineSteps =
    [
        ("", "300|300|1500|1500", "20|20|100|100", "0.25|0.5|1|1.25"),
        ("p.ScaleMode = 1", "300|300|1500|1500", "300|300|1500|1500", "360|720|1440|1800"),
        ("p.ScaleMode = 3", "20|20|100|100", "20|20|100|100", "24|48|96|120"),
        ("p.ScaleMode = 5", "0.2083333|0.2083333|1.041667|1.041667", "0.2083333|0.2083333|1.041667|1.041667", "0.25|0.5|1|1.25"),
        ("p.ScaleMode = 6", "5.291672|5.291672|26.45836|26.45836", "5.291672|5.291672|26.45836|26.45836", "6.350006|12.70001|25.40002|31.75003"),
        ("p.ScaleMode = 7", "0.5291672|0.5291672|2.645836|2.645836", "0.5291672|0.5291672|2.645836|2.645836", "0.6350006|1.270001|2.540003|3.175003"),
        ("p.ScaleMode = 4", "2.5|1.25|12.5|6.25", "2.5|1.25|12.5|6.25", "3|3|12|7.5"),
        ("p.ScaleMode = 2", "15|15|75|75", "15|15|75|75", "18|36|72|90"),
        ("p.Scale (0, 0)-(100, 50)", "10.58201|5.291005|52.91005|26.45503", "10.58201|5.291005|52.91005|26.45503", "12.69841|12.69841|50.79365|31.74603"),
        ("p.ScaleMode = 3\nln.X1 = 10", "10|20|100|100", "10|20|100|100", "10|48|96|120"),
        ("p.ScaleMode = 1", "150|300|1500|1500", "150|300|1500|1500", "150|720|1440|1800"),
        ("p.Scale (10, 20)-(110, 70)", "15.29101|25.29101|62.91005|46.45502", "15.29101|25.29101|62.91005|46.45502", "15.29101|32.69841|60.79365|51.74603"),
        ("ln.Y1 = 30", "15.29101|30|62.91005|46.45502", "15.29101|30|62.91005|46.45502", "15.29101|30|60.79365|51.74603"),
        ("p.ScaleMode = 1", "150|567|1500|1500", "150|567|1500|1500", "150|567|1440|1800"),
        ("p.ScaleMode = 3\np.ScaleLeft = -5", "5|37.8|95|100", "5|37.8|95|100", "5|37.8|91|120"),
        ("p.ScaleTop = 7", "5|44.8|95|107", "5|44.8|95|107", "5|44.8|91|127"),
    ];

    public static IEnumerable<object[]> PictureBoxLineReads()
    {
        for (var step = 0; step < PictureBoxLineSteps.Length; step++)
        {
            var (_, twips, pixels, inches) = PictureBoxLineSteps[step];
            yield return [2, step, twips];
            yield return [3, step, pixels];
            yield return [4, step, inches];
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(PictureBoxLineReads))]
    public void PictureBox_Line_ends_follow_the_current_scale_and_origin(int box, int step, string expected)
    {
        var setup = $"Dim p As PictureBox\nSet p = Picture{box}\nDim ln As Control\nSet ln = Line{box}\n"
            + string.Join('\n', PictureBoxLineSteps.Take(step + 1).Select(row => row.Statement));
        var run = RunLoad(LineScaleForm, setup + "\nDebug.Print " + LineEnds("ln")
            + "\nDebug.Print TypeName(ln.X1)"
            + (step == 11 ? "\nDebug.Print CStr(p.ScaleLeft) & \"|\" & CStr(p.ScaleTop)" : "")
            + (step == 14 ? "\nDebug.Print CStr(p.ScaleMode)" : ""));
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal(step switch
        {
            11 => new[] { expected, "Single", "10|20" },
            14 => new[] { expected, "Single", "0" },
            _ => new[] { expected, "Single" },
        });
    }

    private static readonly string[] FormLineSteps =
    [
        "Me.ScaleMode = 3", "Me.ScaleMode = 5", "Me.ScaleMode = 7", "Me.ScaleMode = 1",
        "Me.Scale (0, 0)-(100, 50)", "Me.Scale (10, 20)-(110, 70)",
    ];

    [AvaloniaTheory]
    [InlineData(0, "20|213.3333|80|266.6667")]
    [InlineData(1, "0.2083333|2.222222|0.8333333|2.777778")]
    [InlineData(2, "0.5291672|5.64445|2.116669|7.055562")]
    [InlineData(3, "300|3200|1200|4000")]
    [InlineData(4, "3.262643|33.33333|13.05057|41.66667")]
    [InlineData(5, "13.26264|53.33333|23.05057|61.66667")]
    public void Form_Line_ends_follow_the_current_scale_and_origin(int step, string expected)
    {
        var run = RunLoad(LineScaleForm, string.Join('\n', FormLineSteps.Take(step + 1))
            + "\nDebug.Print " + LineEnds("Line1") + "\nDebug.Print TypeName(Line1.X1)");
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal(expected, "Single");
    }

    [AvaloniaFact]
    public void Line_write_in_current_pixels_stores_whole_twips()
    {
        var run = RunLoad(MetricLineForm, "Picture2.ScaleMode = 3\nLine2.X1 = 10\nDebug.Print CStr(Line2.X1)\nPicture2.ScaleMode = 1\nDebug.Print CStr(Line2.X1)");
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("10", "150");
    }

    // A write takes the container's origin off before it scales (#5530, oracle PR #5534): (40 - 20) * 56.7 twips.
    [AvaloniaFact]
    public void Line_write_in_a_scale_with_an_origin_subtracts_it()
    {
        var frm = MetricLineForm.Replace("Width = 3000", "Width = 2835\nBorderStyle = 0").Replace("Height = 2000", "Height = 2835");
        var run = RunLoad(frm, "Picture2.Scale (10, 20)-(110, 70)\nLine2.Y1 = 40\nDebug.Print CStr(Line2.Y1)"
            + "\nPicture2.ScaleMode = 1\nDebug.Print CStr(Line2.Y1)");
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("40", "1134");
    }

}
