using System.Collections.Generic;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

// Refusals measured on VB6 SP6: #4988, oracle PR #4987. Guards retain today's unmeasured spellings.
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
    public void Refused_number_leaves_default_logs_and_saves_raw_line(string type, string name, string value)
    {
        var sink = new Sink();
        var line = $"      {name} = {value}";
        var form = Load(Source(type, line + "\r\n"), sink);
        var absent = Load(Source(type, ""), new Sink());
        if (name == "Height")
            form.Components[1].GetPropertyOrDefault(VBProperties.HeightProperty).Should()
                .Be(absent.Components[1].GetPropertyOrDefault(VBProperties.HeightProperty));
        else if (name == "TabIndex")
            form.Components[1].GetPropertyOrDefault(VBProperties.TabIndexProperty).Should()
                .Be(absent.Components[1].GetPropertyOrDefault(VBProperties.TabIndexProperty));
        else
            form.Components[1].GetPropertyOrDefault(VBProperties.AlignmentProperty).Should()
                .Be(absent.Components[1].GetPropertyOrDefault(VBProperties.AlignmentProperty));
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

    [Fact]
    public void Whole_double_still_reads_as_Integer()
    {
        var form = Load(Source("CommandButton", "TabIndex = 1.0\r\n"), new Sink());
        form.Components[1].GetPropertyOrDefault(VBProperties.TabIndexProperty).Should().Be(1);
    }

    [Fact]
    public void Font_size_still_reads_a_fraction()
    {
        var form = Load(Source("CommandButton", "BeginProperty Font\r\nName = \"Arial\"\r\nSize = 8.25\r\nEndProperty\r\n"), new Sink());
        form.Components[1].GetPropertyOrDefault(VBProperties.FontProperty).Size.Should().Be(8.25);
    }
}
