using System.Globalization;
using System.Linq;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6 measurements: https://github.com/hexide-io/HexIDE/issues/4096#issuecomment-6915523429
public class DamagedFormPropertyTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) { }
    }

    private static FormDefinition Load(string properties, bool button = false) => LoadSource(
        "VERSION 5.00\r\nBegin VB.Form Form1\r\n" +
        (button ? "   Begin VB.CommandButton Command1\r\n" + properties + "   End\r\n" : properties) +
        "End\r\nAttribute VB_Name = \"Form1\"\r\n");

    private static FormDefinition LoadSource(string source) => new FormDeserializer().Deserialize(
        new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;

    private static void Stable(FormDefinition form, string name, string winningValue)
    {
        form.CanSaveFaithfully.Should().BeTrue();
        var first = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        var second = new FormSerializer().Serialize(LoadSource(first), "Form1.frm").frmText;
        second.Should().Be(first);
        var lines = first.ReplaceLineEndings("\n").Split('\n')
            .Where(line => line.TrimStart().StartsWith(name + " ", StringComparison.OrdinalIgnoreCase)).ToArray();
        lines.Should().ContainSingle();
        var value = lines[0].Split('=')[1].Trim();
        if (double.TryParse(winningValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            double.Parse(value, CultureInfo.InvariantCulture).Should().Be(number);
        else
            value.Should().Be(winningValue);
    }

    [Theory]
    [InlineData("ClientHeight")]
    [InlineData("ClientHeigHt")]
    public void ReadableDuplicate_LastWins(string firstName)
    {
        var form = Load($"{firstName} = 3000\r\nClientHeight = 4000\r\n");
        form.Components[0].GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(4000d / 15);
        Stable(form, "ClientHeight", "4000");
    }

    [Theory]
    [InlineData("Caption")]
    [InlineData("caption")]
    public void CaptionDuplicate_LastWins(string firstName)
    {
        var form = Load($"{firstName} = \"First\"\r\nCaption = \"Second\"\r\n", true);
        form.Components[1].GetPropertyOrDefault(VBProperties.CaptionProperty).Should().Be("Second");
        Stable(form, "Caption", "\"Second\"");
    }

    [Fact]
    public void UnreadableDuplicate_KeepsReadableValueAndRefusesSave()
    {
        const string good = "ClientHeight = 2000\r\n";
        const string bad = "ClientHeight = abc\r\n";
        var form = Load(good + bad);
        form.Components[0].GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(2000d / 15);
        form.UnfaithfulSaveCauses.Should().HaveFlag(UnfaithfulSaveCause.UnreadableDuplicateProperty);
        form.CanSaveFaithfully.Should().BeFalse();
        form.UnfaithfulSaveReason.Should().Contain("ClientHeight");
    }

    [Fact]
    public void LargestMeasuredHeight_LoadsAndSavesStably()
    {
        var form = Load("Height = 2147483520\r\n", true);
        form.Components[1].GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(2147483520d / 15);
        Stable(form, "Height", "2147483520");
    }

    [Theory]
    [InlineData("Height", "2147483584", true)]
    [InlineData("Height", "2147483647", true)]
    [InlineData("Height", "3.7E+18", true)]
    [InlineData("Height", "1E+30", true)]
    [InlineData("Left", "2147483648", true)]
    [InlineData("ClientHeight", "3.7E+18", false)]
    [InlineData("Height", "-1", true)]
    public void RefusedMeasurement_LeavesPropertyAbsentAndRefusesSave(string name, string value, bool button)
    {
        var form = Load($"{name} = {value}\r\n", button);
        var absent = Load("", button);
        var index = button ? 1 : 0;
        var property = name == "Left" ? VBProperties.LeftProperty : VBProperties.HeightProperty;
        form.Components[index].GetPropertyOrDefault(property).Should().Be(absent.Components[index].GetPropertyOrDefault(property));
        form.UnfaithfulSaveCauses.Should().HaveFlag(UnfaithfulSaveCause.RefusedMeasurement);
        form.CanSaveFaithfully.Should().BeFalse();
        form.UnfaithfulSaveReason.Should().Contain(name).And.Contain(value);
    }
}
