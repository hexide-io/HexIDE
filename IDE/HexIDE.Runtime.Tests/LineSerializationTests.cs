using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The Line is modelled (#163), so a form holding one loads it as a control rather than carrying it as text, and saves
/// it back as VB6 wrote it.
/// </summary>
public class LineSerializationTests
{
    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "P");

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    // VB6's own About Dialog template: its user scale, and its two lines, as the template holds them.
    private const string About =
        "VERSION 5.00\r\n" +
        "Begin VB.Form frmAbout \r\n" +
        "   BorderStyle     =   3  'Fixed Dialog\r\n" +
        "   Caption         =   \"About MyApp\"\r\n" +
        "   ClientHeight    =   3555\r\n" +
        "   ClientLeft      =   2340\r\n" +
        "   ClientTop       =   1935\r\n" +
        "   ClientWidth     =   5730\r\n" +
        "   ScaleHeight     =   2453.724\r\n" +
        "   ScaleMode       =   0  'User\r\n" +
        "   ScaleWidth      =   5380.766\r\n" +
        "   Begin VB.Line Line1 \r\n" +
        "      BorderColor     =   &H00808080&\r\n" +
        "      BorderStyle     =   6  'Inside Solid\r\n" +
        "      Index           =   1\r\n" +
        "      X1              =   84.515\r\n" +
        "      X2              =   5309.398\r\n" +
        "      Y1              =   1687.583\r\n" +
        "      Y2              =   1687.583\r\n" +
        "   End\r\n" +
        "   Begin VB.Line Line1 \r\n" +
        "      BorderColor     =   &H00FFFFFF&\r\n" +
        "      BorderWidth     =   2\r\n" +
        "      Index           =   0\r\n" +
        "      X1              =   98.6\r\n" +
        "      X2              =   5309.398\r\n" +
        "      Y1              =   1697.936\r\n" +
        "      Y2              =   1697.936\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"frmAbout\"\r\n";

    private static FormDefinition Load(string frm) => new FormDeserializer().Deserialize(Project, frm, NullSink.Instance)!;

    private static string Save(FormDefinition form) => new FormSerializer().Serialize(form, "frmAbout.frm").Item1;

    [Fact]
    public void The_About_Dialogs_lines_load_as_Line_controls_with_their_values_as_the_file_wrote_them()
    {
        var form = Load(About);

        form.UnknownChildSubtreeTexts.Should().BeEmpty("the Line is modelled now, not carried as text");
        var lines = form.Components.Where(c => c.BaseClass is LineComponentClass).ToList();
        lines.Should().HaveCount(2);
        lines[0].GetPropertyOrDefault(VBProperties.X1Property).Should().Be(84.515);
        lines[0].GetPropertyOrDefault(VBProperties.Y2Property).Should().Be(1687.583);
        lines[1].GetPropertyOrDefault(VBProperties.BorderWidthProperty).Should().Be(2);
    }

    [Fact]
    public void The_About_Dialog_saves_back_byte_for_byte()
    {
        Save(Load(About)).Should().Be(About);
    }

    [Fact]
    public void A_line_saves_no_Left_Top_Width_or_Height_and_no_default_it_was_not_given()
    {
        var form = Load(About);
        var line = form.Components.First(c => c.BaseClass is LineComponentClass);
        line.SetProperty(VBProperties.X1Property, 100.0);

        var saved = Save(form);

        saved.Should().Contain("      X1              =   100\r\n");
        foreach (var absent in new[] { "Left", "Top", "Width", "Height", "DrawMode", "Visible" })
            saved.Should().NotContain($"      {absent} ", $"a Line has no {absent} the file did not give it");
    }

    private static FormDefinition LoadWithDrawMode(string value) => Load(About.Replace(
        "      BorderWidth     =   2\r\n",
        $"      BorderWidth     =   2\r\n      DrawMode        =   {value}\r\n"));

    // VB6 SP6's saved comments, including the shifted names (oracle: the-line-control.md, #5263).
    [Theory]
    [InlineData("1", "      DrawMode        =   1  'Blackness")]
    [InlineData("2", "      DrawMode        =   2  'Blackness")]
    [InlineData("3", "      DrawMode        =   3  'Not Merge Pen")]
    [InlineData("4", "      DrawMode        =   4  'Mask Not Pen")]
    [InlineData("5", "      DrawMode        =   5  'Not Copy Pen")]
    [InlineData("6", "      DrawMode        =   6  'Mask Pen Not")]
    [InlineData("7", "      DrawMode        =   7  'Invert")]
    [InlineData("8", "      DrawMode        =   8  'Xor Pen")]
    [InlineData("9", "      DrawMode        =   9  'Not Mask Pen")]
    [InlineData("10", "      DrawMode        =   10  'Mask Pen")]
    [InlineData("11", "      DrawMode        =   11  'Not Xor Pen")]
    [InlineData("12", "      DrawMode        =   12  'Nop")]
    [InlineData("14", "      DrawMode        =   14  'Copy Pen")]
    [InlineData("15", "      DrawMode        =   15  'Merge Pen Not")]
    [InlineData("16", "      DrawMode        =   16  'Merge Pen")]
    [InlineData("20", "      DrawMode        =   20")] // Preserve HexIDE's existing output for unmeasured values.
    public void A_lines_DrawMode_saves_with_the_expected_comment(string value, string expected)
    {
        Save(LoadWithDrawMode(value)).Should().Contain(expected + "\r\n");
    }

    [Fact]
    public void A_line_omits_its_default_DrawMode_even_when_the_file_sets_it()
    {
        Save(LoadWithDrawMode("13")).Should().Be(About);
    }

    [Fact]
    public void A_lines_commented_DrawMode_reads_as_a_number_and_saves_back_byte_for_byte()
    {
        const string drawMode = "      DrawMode        =   7  'Invert\r\n";
        var frm = About.Replace("      BorderWidth     =   2\r\n", "      BorderWidth     =   2\r\n" + drawMode);
        var form = Load(frm);
        var line = form.Components.Last(c => c.BaseClass is LineComponentClass);

        line.GetPropertyOrDefault(VBProperties.DrawModeProperty).Should().Be(7);
        Save(form).Should().Be(frm);
    }

    [Fact]
    public void A_line_with_nothing_set_reads_VB6s_defaults()
    {
        var form = Load(About.Replace(
            "   Begin VB.Line Line1 \r\n      BorderColor     =   &H00808080&\r\n      BorderStyle     =   6  'Inside Solid\r\n      Index           =   1\r\n      X1              =   84.515\r\n      X2              =   5309.398\r\n      Y1              =   1687.583\r\n      Y2              =   1687.583\r\n   End\r\n",
            "   Begin VB.Line LineBare \r\n   End\r\n"));
        var bare = form.Components.First(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "LineBare");

        bare.GetPropertyOrDefault(VBProperties.BorderColorProperty).ToOle().Should().Be(unchecked((int)0x80000008));
        bare.GetPropertyOrDefault(VBProperties.ShapeBorderStyleProperty).Should().Be(BuiltinTypes.BorderStyles.Solid);
        bare.GetPropertyOrDefault(VBProperties.BorderWidthProperty).Should().Be(1);
        bare.GetPropertyOrDefault(VBProperties.DrawModeProperty).Should().Be(13);
        bare.GetPropertyOrDefault(VBProperties.X1Property).Should().Be(0);
    }
}
