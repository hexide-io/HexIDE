using System;
using System.Linq;
using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

public class SliderDesignerPersistenceTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) => throw new InvalidOperationException(error);
    }

    private static FormDefinition Load(string properties) => new FormDeserializer().Deserialize(
        new ProjectDefinition(VBProjectType.EXE, "P"),
        "VERSION 5.00\nBegin VB.Form Form1\nBegin MSComctlLib.Slider Slider1\nHeight = 675\nWidth = 4500\n_ExtentX = 7938\n_ExtentY = 1191\n_Version = 393216\n"
        + properties + "\nEnd\nEnd\nAttribute VB_Name = \"Form1\"\n", new Sink())!;

    [Fact]
    public void Loaded_slider_refuses_saving_until_designer_edits_can_be_saved()
    {
        Load("Value = 6").CanSaveFaithfully.Should().BeFalse();
    }

    [Theory]
    [InlineData("Min = 20\nValue = 99\nTickStyle = 7")]
    [InlineData("SelectRange = 0\nSelStart = 2\nSelLength = 3\nValue = 6")]
    public void Untouched_own_lines_keep_file_values_and_order(string properties)
    {
        var written = new FormSerializer().Serialize(Load(properties), "Form1.frm").Item1.ReplaceLineEndings("\n");
        var names = properties.Split('\n').Select(p => p.Split('=')[0].Trim()).ToArray();
        var lines = written.Split('\n').Where(l => names.Contains(l.Trim().Split('=')[0].Trim())).ToArray();
        lines.Select(l => l.Trim()).Should().Equal(properties.Split('\n'));
    }

    [Fact]
    public void Missing_extents_are_not_invented_on_an_untouched_save()
    {
        var form = Load("Value = 6");
        var slider = form.Components.Single(c => c.BaseClass.Name == "Slider");
        slider.UnknownRawProperties.RemoveAll(p => p.Name.StartsWith("_Extent", StringComparison.Ordinal));
        var written = new FormSerializer().Serialize(form, "Form1.frm").Item1;
        written.Should().NotContain("_Extent");
    }

    [Fact]
    public void Resizing_updates_extents_and_keeps_own_lines()
    {
        var form = Load("TickStyle = 7\nValue = 99");
        var slider = form.Components.Single(c => c.BaseClass.Name == "Slider");
        slider.SetProperty(VBProperties.WidthProperty, 45d);
        slider.SetProperty(VBProperties.HeightProperty, 300d);
        var written = new FormSerializer().Serialize(form, "Form1.frm").Item1.ReplaceLineEndings("\n");
        written.Should().Contain("_ExtentX        =   1191\n");
        written.Should().Contain("_ExtentY        =   7938\n");
        written.Should().Contain("TickStyle = 7\nValue = 99\n");
    }
}
