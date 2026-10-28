using System.Text;
using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Serialization;

public class FlexGridSerializationTests
{
    private static string Fixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Join(dir.FullName, "corpus", "inbox-controls")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        var root = Path.Join(dir!.FullName, "corpus", "inbox-controls");
        var saved = Path.Join(root, "saved", "msflexgrid", "Form1.frm");
        return File.ReadAllText(saved)
            .ReplaceLineEndings("\r\n");
    }

    private static FormDefinition Read(string source) => new FormDeserializer().Deserialize(
        new ProjectDefinition(VBProjectType.EXE, "P"), source, Substitute.For<IDeserializeErrorSink>())!;

    [Fact]
    public void The_VB6_save_round_trips_every_byte_and_has_no_companion()
    {
        var source = Fixture();
        var form = Read(source);
        form.CanSaveFaithfully.Should().BeTrue();
        form.Components.Count(c => c.BaseClass is FlexGridComponentClass).Should().Be(3);
        var (text, companion) = new FormSerializer().Serialize(form, "Form1.frm");
        Encoding.ASCII.GetBytes(text).Should().Equal(Encoding.ASCII.GetBytes(source));
        companion.Should().BeNull("VB6 saved no cell text or pictures in this fixture");
    }

    [Fact]
    public void A_form_edit_keeps_every_grid_block_intact()
    {
        var source = Fixture();
        var form = Read(source);
        form.Components.Single(c => c.BaseClass is FormComponentClass)
            .SetProperty(VBProperties.CaptionProperty, "Edited");
        var (text, companion) = new FormSerializer().Serialize(form, "Form1.frm");
        text.Should().Be(source.Replace("Caption         =   \"Form1\"", "Caption         =   \"Edited\""));
        companion.Should().BeNull();
    }

    [Fact]
    public void Own_properties_use_the_measured_order_even_when_read_in_reverse()
    {
        var source = Fixture();
        var form = Read(source);
        foreach (var grid in form.Components.Where(c => c.BaseClass is FlexGridComponentClass))
            grid.UnknownRawProperties.Reverse();
        new FormSerializer().Serialize(form, "Form1.frm").frmText.Should().Be(source);
    }

    [Fact]
    public void Carried_grid_properties_cannot_be_edited_as_designer_properties()
    {
        var grid = Read(Fixture()).Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "MSFlexGrid1");
        foreach (var raw in grid.UnknownRawProperties)
            grid.BaseClass.PropertiesByName.ContainsKey(raw.Name).Should().BeFalse();
        grid.UnknownRawProperties.Select(p => p.Name).Should().Contain("Font").And.Contain("RightToLeft");
    }
}
