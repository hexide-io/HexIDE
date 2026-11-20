using System.Collections.Generic;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

// VB6 SP6 measurements: #4988, oracle PR #4987.
public class FormNumericValueTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = [];
        public void LogError(string error) => Errors.Add(error);
    }

    private static string Source(string type, string properties) =>
        $"VERSION 5.00\r\nBegin VB.Form Form1\r\n   Begin VB.{type} Command1\r\n{properties}   End\r\nEnd\r\n";

    private static FormDefinition Load(string source, Sink sink) => new FormDeserializer().Deserialize(
        new ProjectDefinition(VBProjectType.EXE, "P"), source, sink)!;

    [Theory]
    [InlineData("CommandButton", "Height", "1,5")]
    [InlineData("CommandButton", "TabIndex", "1.5")]
    [InlineData("Label", "Alignment", "1.5")]
    // VB6 reads 5. Preserve the text until the Single prefix rule is implemented.
    [InlineData("CommandButton", "Left", "5-")]
    [InlineData("CommandButton", "Left", "(5)")]
    [InlineData("CommandButton", "Left", "NaN")]
    [InlineData("CommandButton", "Left", "Infinity")]
    [InlineData("CommandButton", "MousePointer", "3.")]
    [InlineData("CommandButton", "MousePointer", "1.0")]
    [InlineData("CommandButton", "MousePointer", "(3)")]
    [InlineData("CommandButton", "MousePointer", "3-")]
    // Refused for safety. VB6 behavior for these property/text pairs is unmeasured.
    [InlineData("CommandButton", "Left", "-Infinity")]
    [InlineData("CommandButton", "Left", "1e400")]
    [InlineData("CommandButton", "Left", "¤5")]
    [InlineData("CommandButton", "MousePointer", "NaN")]
    [InlineData("CommandButton", "MousePointer", "Infinity")]
    [InlineData("CommandButton", "TabIndex", "3.")]
    [InlineData("CommandButton", "TabIndex", "1.0")]
    [InlineData("CommandButton", "TabIndex", "(3)")]
    [InlineData("CommandButton", "TabIndex", "3-")]
    [InlineData("CommandButton", "TabIndex", "Infinity")]
    [InlineData("CommandButton", "TabIndex", "1e3")]
    // Existing refusals.
    [InlineData("CommandButton", "Left", "&H10")]
    [InlineData("CommandButton", "Left", "&HFF")]
    [InlineData("CommandButton", "MousePointer", "&H8000")]
    [InlineData("CommandButton", "TabIndex", "&H3")]
    [InlineData("CommandButton", "BackColor", "-1")]
    [InlineData("CommandButton", "BackColor", "16777216")]
    [InlineData("CommandButton", "BackColor", "&O377")]
    [InlineData("CommandButton", "BackColor", "&HFFFF")]
    public void Refused_number_leaves_default_logs_and_saves_raw_line(string type, string name, string value)
    {
        var sink = new Sink();
        var line = $"      {name} = {value}";
        var form = Load(Source(type, line + "\r\n"), sink);
        var absent = Load(Source(type, ""), new Sink());
        var property = form.Components[1].BaseClass.PropertiesByName[name];
        form.Components[1].GetBoxedPropertyOrDefault(property).Should().Be(absent.Components[1].GetBoxedPropertyOrDefault(property));
        sink.Errors.Should().Contain($"Property {name} in Command1 had an invalid value.");
        var saved = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        saved.ReplaceLineEndings("\n").Split('\n').Should().Contain(line);
    }

    [Theory]
    [InlineData("1e3", 1000d)]
    [InlineData("-1", -1d)]
    [InlineData("1.5E+18", 1.5E+18)]
    [InlineData(" 7 ", 7d)]
    public void Numeric_spellings_still_parse(string text, double expected)
    {
        var (component, _) = new VbFrmFormatDeserializer().Deserialize(Source("CommandButton", $"Height = {text}\r\n"));
        Convert.ToDouble(component.SubComponents[0].Properties["Height"]).Should().Be(expected);
    }

    [Theory]
    [InlineData("&H3", 3)]
    [InlineData("&H3&", 3)]
    [InlineData("&O7", 7)]
    public void Short_radix_literal_reads_as_enum(string text, int expected)
    {
        var sink = new Sink();
        var form = Load(Source("CommandButton", $"MousePointer = {text}\r\n"), sink);
        form.Components[1].GetPropertyOrDefault(VBProperties.MousePointerProperty).Should().Be((VBCursorType)expected);
        sink.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("255", 255, "&H000000FF&")]
    [InlineData("&HFF", 255, "&H000000FF&")]
    [InlineData("&HFF&", 255, "&H000000FF&")]
    [InlineData("16777215", 16777215, "&H00FFFFFF&")]
    public void Colour_reads_and_saves_in_eight_digit_form(string text, int expected, string savedColor)
    {
        var sink = new Sink();
        var form = Load(Source("CommandButton", $"BackColor = {text}\r\n"), sink);
        form.Components[1].GetPropertyOrDefault(VBProperties.BackColorProperty).Should().Be(VBColor.FromOle(expected));
        sink.Errors.Should().BeEmpty();
        var saved = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        saved.ReplaceLineEndings("\n").Split('\n').Should().Contain(line =>
            line.TrimStart().StartsWith("BackColor ", StringComparison.Ordinal)
            && line.EndsWith($"=   {savedColor}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("+5")]
    [InlineData("5.")]
    public void Single_spellings_still_read_as_twips(string text)
    {
        var sink = new Sink();
        var form = Load(Source("CommandButton", $"Left = {text}\r\n"), sink);
        form.Components[1].GetPropertyOrDefault(VBProperties.LeftProperty).Should().Be(5d / 15);
        sink.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Font_size_still_reads_a_fraction()
    {
        var form = Load(Source("CommandButton", "BeginProperty Font\r\nName = \"Arial\"\r\nSize = 8.25\r\nEndProperty\r\n"), new Sink());
        form.Components[1].GetPropertyOrDefault(VBProperties.FontProperty).Size.Should().Be(8.25);
    }
}
