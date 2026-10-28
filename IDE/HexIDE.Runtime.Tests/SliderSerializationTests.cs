using System;
using System.IO;
using System.Linq;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

public class SliderSerializationTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) => throw new InvalidOperationException(error);
    }

    [Fact]
    public void VB6_saved_slider_form_round_trips_byte_for_byte()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Join(root.FullName, "corpus"))) root = root.Parent;
        root.Should().NotBeNull();
        var path = Path.Join(root!.FullName, "corpus", "inbox-controls", "saved", "slider", "Form1.frm");
        var bytes = File.ReadAllBytes(path);
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            System.Text.Encoding.Latin1.GetString(bytes), new Sink())!;
        var written = new FormSerializer().Serialize(form, "Form1.frm").Item1;
        System.Text.Encoding.Latin1.GetBytes(written).Should().Equal(bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Explicit_SelStart_is_preserved_and_Text_is_never_saved(bool explicitStart)
    {
        var text = "VERSION 5.00\nBegin VB.Form Form1\nBegin MSComctlLib.Slider Slider1\nHeight = 675\nWidth = 4500\n_ExtentX = 7938\n_ExtentY = 1191\n_Version = 393216\n      Value           =   6\nText = \"ignored\"\n"
            + (explicitStart ? "SelStart = 6\n" : "") + "End\nEnd\nAttribute VB_Name = \"Form1\"\n";
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), text, new Sink())!;
        var written = new FormSerializer().Serialize(form, "Form1.frm").Item1.ReplaceLineEndings("\n");
        written.Should().NotContain("Text ");
        written.Contains("SelStart").Should().Be(explicitStart);
        written.Should().Contain("Value           =   6");
        written.IndexOf("Value", StringComparison.Ordinal).Should().BeGreaterThan(written.IndexOf("_Version", StringComparison.Ordinal));
    }
}
