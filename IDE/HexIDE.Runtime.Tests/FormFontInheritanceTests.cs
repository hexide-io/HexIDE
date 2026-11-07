using System.Linq;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Tests;

public class FormFontInheritanceTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) => error.Should().BeNullOrEmpty();
    }

    private static string Font(string name, double size, int weight = 400) => $"""
        BeginProperty Font
           Name = "{name}"
           Size = {size.ToString(System.Globalization.CultureInfo.InvariantCulture)}
           Weight = {weight}
           Italic = 0
        EndProperty
        """;

    private static FormDefinition Load() => new FormDeserializer().Deserialize(
        new ProjectDefinition(VBProjectType.EXE, "P"), $"""
        VERSION 5.00
        Begin VB.Form Form1
           {Font("Arial", 12)}
           Begin VB.CommandButton Command1
           End
           Begin VB.Label Label1
           End
           Begin VB.TextBox Text1
           End
           Begin VB.PictureBox Picture1
              {Font("Courier New", 9.75)}
              Begin VB.CommandButton Command2
              End
              Begin VB.Frame Frame2
                 Begin VB.CommandButton Command4
                 End
              End
           End
           Begin VB.Frame Frame1
              {Font("Times New Roman", 9.75, 700)}
              Begin VB.CommandButton Command3
              End
           End
        End
        Attribute VB_Name = "Form1"
        """, new Sink())!;

    [Fact]
    public void Missing_Font_blocks_take_the_forms_font_at_every_depth_and_explicit_blocks_stay()
    {
        var form = Load();
        foreach (var name in new[] { "Command1", "Label1", "Text1", "Command2", "Frame2", "Command4", "Command3" })
            form.Components.Single(c => c.GetPropertyOrDefault(NameProperty) == name).GetPropertyOrDefault(FontProperty)
                .Should().Be(new VBFont("Arial", 12), name);
        form.Components.Single(c => c.GetPropertyOrDefault(NameProperty) == "Picture1").GetPropertyOrDefault(FontProperty)
            .Should().Be(new VBFont("Courier New", 9.75));
        form.Components.Single(c => c.GetPropertyOrDefault(NameProperty) == "Frame1").GetPropertyOrDefault(FontProperty)
            .Should().Be(new VBFont("Times New Roman", 9.75, bold: true));
    }

    [Fact]
    public void Inherited_fonts_are_load_time_copies()
    {
        var form = Load();
        form.Components.Single(c => c.GetPropertyOrDefault(NameProperty) == "Form1").SetProperty(FontProperty, new VBFont("Tahoma", 12));
        foreach (var name in new[] { "Command1", "Command2" })
            form.Components.Single(c => c.GetPropertyOrDefault(NameProperty) == name).GetPropertyOrDefault(FontProperty)
                .Should().Be(new VBFont("Arial", 12));
        form.Components.Single(c => c.GetPropertyOrDefault(NameProperty) == "Command1").SetProperty(FontProperty, new VBFont("Tahoma", 12));
        form.Components.Single(c => c.GetPropertyOrDefault(NameProperty) == "Label1").GetPropertyOrDefault(FontProperty)
            .Should().Be(new VBFont("Arial", 12));
    }
}
