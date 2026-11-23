using System.Globalization;
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

    [AvaloniaFact]
    public void Line_write_in_current_pixels_stores_whole_twips()
    {
        var run = RunLoad(MetricLineForm, "Picture2.ScaleMode = 3\nLine2.X1 = 10\nDebug.Print CStr(Line2.X1)\nPicture2.ScaleMode = 1\nDebug.Print CStr(Line2.X1)");
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("10", "150");
    }

}
