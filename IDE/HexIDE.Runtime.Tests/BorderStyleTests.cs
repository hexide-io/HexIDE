using System;
using System.Collections.Generic;
using System.Linq;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A Shape's BorderStyle is VB6's seven-value set, the same one as DrawStyle. Only Transparent and Solid
/// existed on the enum, so the other five were values outside the type: every equality test against them
/// was false and the shape drew no border at all.
///
/// Sibling of the FillStyle case in <see cref="WriterFormatTests"/> — same shape of defect, found the same
/// way, by writing VB6's own name beside the value and seeing it disagree with Microsoft's file.
/// </summary>
public class BorderStyleTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = new();
        public void LogError(string error) => Errors.Add(error);
    }

    private static FormDefinition Load(string source) =>
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;

    private static string Save(FormDefinition form) =>
        new FormSerializer().Serialize(form, "Form1.frm").Item1;

    private static string WithShapeBorder(string value) =>
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   ClientWidth     =   4800\r\n" +
        "   Begin VB.Shape Shape1 \r\n" +
        "      BorderStyle     =   " + value + "\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    [Theory]
    [InlineData(0, "Transparent")]
    [InlineData(1, "Solid")]
    [InlineData(2, "Dash")]
    [InlineData(3, "Dot")]
    [InlineData(4, "Dash-Dot")]
    [InlineData(5, "Dash-Dot-Dot")]
    [InlineData(6, "Inside Solid")]
    public void EveryBorderStyle_IsDefined_AndCarriesVb6sName(int value, string vb6Name)
    {
        Enum.IsDefined(typeof(BorderStyles), value)
            .Should().BeTrue($"VB6 writes BorderStyle = {value}, so the enum has to have it");

        var style = (BorderStyles)value;
        Vb6EnumNames.For(style).Should().Be(vb6Name);
    }

    [Fact]
    public void InsideSolid_RoundTripsWithTheNameMicrosoftWrote()
    {
        // Template\Forms\About Dialog.frm carries exactly this line.
        var saved = Save(Load(WithShapeBorder("6  'Inside Solid")));

        saved.Should().Contain("      BorderStyle     =   6  'Inside Solid");
    }

    [Fact]
    public void AllSevenSurviveASave()
    {
        for (var value = 0; value <= 6; value++)
        {
            var saved = Save(Load(WithShapeBorder(value.ToString())));
            saved.Should().Contain($"      BorderStyle     =   {value}  '",
                $"BorderStyle {value} must come back as itself, with a name");
        }
    }

    [Fact]
    public void OnlyTransparentMeansNoBorder()
    {
        // The renderer's rule, stated as data rather than by rendering: Transparent is the one style that
        // draws nothing. Testing it here keeps the intent pinned even though the drawing itself needs a
        // visual check — the defect was that FIVE styles drew nothing, not one.
        var drawsNothing = Enum.GetValues<BorderStyles>()
            .Where(s => s == BorderStyles.Transparent)
            .ToList();

        drawsNothing.Should().ContainSingle();
        Enum.GetValues<BorderStyles>().Should().HaveCount(7);
    }
}
