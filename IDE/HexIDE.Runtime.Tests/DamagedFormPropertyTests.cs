using System.Globalization;
using System.Linq;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6 measurements: https://github.com/hexide-io/HexIDE/issues/4096#issuecomment-6915523429
public class DamagedFormPropertyTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = new();
        public void LogError(string error) => Errors.Add(error);
    }

    private static FormDefinition Load(string properties, bool button = false, Sink? sink = null) => LoadSource(
        "VERSION 5.00\r\nBegin VB.Form Form1\r\n" +
        (button ? "   Begin VB.CommandButton Command1\r\n" + properties + "   End\r\n" : properties) +
        "End\r\nAttribute VB_Name = \"Form1\"\r\n", sink);

    private static FormDefinition LoadSource(string source, Sink? sink = null) => new FormDeserializer().Deserialize(
        new ProjectDefinition(VBProjectType.EXE, "P"), source, sink ?? new Sink())!;

    private static void Stable(FormDefinition form, string name, string winningValue, int expectedLines = 1)
    {
        form.CanSaveFaithfully.Should().BeTrue();
        var first = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        var second = new FormSerializer().Serialize(LoadSource(first), "Form1.frm").frmText;
        second.Should().Be(first);
        var lines = first.ReplaceLineEndings("\n").Split('\n')
            .Where(line => line.TrimStart().StartsWith(name + " ", StringComparison.OrdinalIgnoreCase)).ToArray();
        lines.Should().HaveCount(expectedLines);
        var value = lines[0].Split('=')[1].Trim();
        if (!value.StartsWith("\"")) value = value.Split('\'')[0].TrimEnd();
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

    // #4317's latest brief: IDE recovery measured in #4096; refused texts measured in oracle PR #4987.
    [Theory]
    [InlineData("ClientHeight = 2000\r\nClientHeight = abc\r\n", 2000)]
    [InlineData("clientheight = 2000\r\nClientHeight = abc\r\nClientHeight = 3000\r\n", 3000)]
    public void UnreadableDuplicate_KeepsReadableValueAndRequiresSaveAs(string properties, int expected)
    {
        var sink = new Sink();
        var form = Load(properties, sink: sink);
        sink.Errors.Should().Contain("Property ClientHeight in Form1 had an invalid value.");
        form.Components[0].GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(expected / 15d);
        form.LoadedWithErrors.Should().BeTrue();
        form.UnfaithfulSaveCauses.Should().NotHaveFlag(UnfaithfulSaveCause.UnreadableDuplicateProperty);
        Stable(form, "ClientHeight", expected.ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("CommandButton", "Height", "375", "abc")]
    [InlineData("CommandButton", "Height", "375", "\"500\"")]
    [InlineData("CommandButton", "Height", "375", "&H10")]
    [InlineData("CommandButton", "Height", "375", "&H10&")]
    [InlineData("CommandButton", "Height", "375", "&O17")]
    [InlineData("CommandButton", "Height", "375", "&H8000000F&")]
    [InlineData("CommandButton", "TabIndex", "1", "abc")]
    [InlineData("CommandButton", "TabIndex", "1", "\"3\"")]
    [InlineData("Label", "Alignment", "0", "abc")]
    [InlineData("CommandButton", "BackColor", "&H8000000F&", "abc")]
    [InlineData("CommandButton", "BackColor", "&H8000000F&", "\"255\"")]
    public void RefusedDuplicate_OnControl_KeepsReadableValueAndRequiresSaveAs(
        string type, string name, string readable, string refused)
    {
        // Keep TabIndex 1 in range so tab-order normalisation does not change the value under test (#4843).
        var sibling = name == "TabIndex" ? "Begin VB.CommandButton Command2\r\nTabIndex = 0\r\nEnd\r\n" : "";
        var form = LoadSource($"VERSION 5.00\r\nBegin VB.Form Form1\r\nBegin VB.{type} Command1\r\n" +
            $"{name} = {readable}\r\n{name.ToLowerInvariant()} = {refused}\r\nEnd\r\n" + sibling +
            "End\r\nAttribute VB_Name = \"Form1\"\r\n");
        var control = form.Components[1];
        if (name == "Height") control.GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(375d / 15);
        else if (name == "TabIndex") control.GetPropertyOrDefault(VBProperties.TabIndexProperty).Should().Be(1);
        else if (name == "Alignment") control.GetPropertyOrDefault(VBProperties.AlignmentProperty).Should().Be((VBTextAlignment)0);
        else control.GetPropertyOrDefault(VBProperties.BackColorProperty).Should().Be(VBColor.FromSystemColor(VbSystemColor.Btnface));
        form.LoadedWithErrors.Should().BeTrue();
        form.UnfaithfulSaveCauses.Should().NotHaveFlag(UnfaithfulSaveCause.UnreadableDuplicateProperty);
        Stable(form, name, readable, name == "TabIndex" ? 2 : 1);
    }

    // These preserve today's hold; VB6 recovery is unmeasured or the text is accepted by VB6 (#4317).
    [Theory]
    [InlineData("ClientHeight = abc\r\nClientHeight = 2000\r\n")]
    [InlineData("ClientHeight = abc\r\nClientHeight = xyz\r\n")]
    public void UnreadableDuplicate_WithoutEarlierReadableValue_RefusesSave(string properties)
    {
        Held(Load(properties));
    }

    [Theory]
    [InlineData("BackColor", "&H8000000F&", "&H02C0C0C0&")]
    [InlineData("BackColor", "&H8000000F&", "255")]
    [InlineData("TabIndex", "1", "&H3")]
    [InlineData("Height", "375", "--5")]
    [InlineData("Height", "375", "1.5.2")]
    [InlineData("TabIndex", "1", "5abc")]
    [InlineData("TabIndex", "1", "1D2")]
    [InlineData("BackColor", "&H8000000F&", "True")]
    [InlineData("BackColor", "&H8000000F&", "False")]
    [InlineData("BackColor", "&H8000000F&", "Infinity")]
    [InlineData("BackColor", "&H8000000F&", "NaN")]
    public void OtherUnreadableDuplicates_RefuseSave(string name, string readable, string unreadable)
    {
        Held(Load($"{name} = {readable}\r\n{name} = {unreadable}\r\n", true));
    }

    [Fact]
    public void Recovery_DoesNotClearAnEarlierUnreadableHold()
    {
        Held(Load("ClientHeight = --5\r\nClientHeight = 2000\r\nClientHeight = abc\r\n"));
    }

    private static void Held(FormDefinition form)
    {
        form.UnfaithfulSaveCauses.Should().HaveFlag(UnfaithfulSaveCause.UnreadableDuplicateProperty);
        form.CanSaveFaithfully.Should().BeFalse();
    }

    [Fact]
    public void LargestMeasuredHeight_LoadsAndSavesStably()
    {
        var form = Load("Height = 2147483520\r\n", true);
        form.Components[1].GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(2147483520d / 15);
        Stable(form, "Height", "2147483520");
    }

    [Theory]
    [InlineData("Left", "2147483648", true)]
    [InlineData("ClientHeight", "3.7E+18", false)]
    // A negative Height on a form's control recovers as zero with Save As (#4179): NegativeSizeLoadTests.
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
