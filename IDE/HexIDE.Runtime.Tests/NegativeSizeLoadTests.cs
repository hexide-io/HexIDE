using System.Collections.Generic;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

// VB6 SP6 IDE measurements: #4179, oracle PR #4350. Begin lines, not property lines.
public class NegativeSizeLoadTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = [];
        public List<string> Keys { get; } = [];
        public void LogError(string error) => Errors.Add(error);
        public void LogError(string key, string fallback, params object[] arguments)
        {
            Keys.Add(key);
            LogError(string.Format(fallback, arguments));
        }
    }

    [Theory]
    [InlineData("Width")]
    [InlineData("Height")]
    public void Negative_sizes_recover_to_zero_and_report_the_control_begin(string axis)
    {
        var sink = new Sink();
        var source = "VERSION 5.00\nBegin VB.Form Form1\n" + new string('\n', 8)
            + $"Begin VB.Label Label1\n{axis} = -150\nEnd\n" + new string('\n', 13)
            + $"Begin VB.CommandButton Command2\n{axis} = -150\nEnd\nEnd\n";
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, sink)!;
        var property = axis == "Width" ? VBProperties.WidthProperty : VBProperties.HeightProperty;
        form.Components.Skip(1).Select(c => c.GetPropertyOrDefault(property)).Should().Equal(0, 0);
        sink.Errors.Should().Equal($"Line 11: Property {axis} in Label1 could not be set.",
            $"Line 27: Property {axis} in Command2 could not be set.");
        sink.Keys.Should().Equal("Str.ProjectLoad.PropertyNotSet", "Str.ProjectLoad.PropertyNotSet");
        form.LoadedWithErrors.Should().BeTrue();
        form.CanSaveFaithfully.Should().BeTrue();
        form.Components.Skip(1).Should().OnlyContain(c => c.UnknownRawProperties.Count == 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    public void Zero_and_small_sizes_and_negative_positions_are_unchanged(int size)
    {
        var sink = new Sink();
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            $"VERSION 5.00\nBegin VB.Form Form1\nBegin VB.CommandButton Command2\nHeight = {size}\nLeft = -150\nTop = -150\nEnd\nEnd\n", sink)!;
        form.Components[1].GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(size / 15d);
        form.Components[1].GetPropertyOrDefault(VBProperties.LeftProperty).Should().Be(-10);
        form.Components[1].GetPropertyOrDefault(VBProperties.TopProperty).Should().Be(-10);
        form.LoadedWithErrors.Should().BeFalse();
        sink.Errors.Should().BeEmpty();
    }
}
