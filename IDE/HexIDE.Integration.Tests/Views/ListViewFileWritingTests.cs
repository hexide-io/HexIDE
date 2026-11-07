using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

public class ListViewFileWritingTests
{
    private sealed class Sink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static string Corpus(string folder)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Join(directory.FullName, "corpus")))
            directory = directory.Parent;
        directory.Should().NotBeNull();
        var root = Path.Join(directory!.FullName, "corpus", "inbox-controls");
        var saved = Path.Join(root, "saved", folder);
        return Directory.Exists(saved) ? saved : Path.Join(root, "pending", folder);
    }

    private static FormDefinition Read(string source, byte[]? companion = null) =>
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink(),
            companion is null ? null : FrxDeserializer.Read(companion, source))!;

    [AvaloniaTheory]
    [InlineData("listview")]
    [InlineData("listview-columns")]
    public void VB6_saves_round_trip_with_their_companion(string folder)
    {
        var path = Corpus(folder);
        var source = File.ReadAllText(Path.Join(path, "Form1.frm"));
        var companion = File.ReadAllBytes(Path.Join(path, "Form1.frx"));
        var form = Read(source, companion);
        form.CanSaveFaithfully.Should().BeTrue();
        var output = new FormSerializer().Serialize(form, "Form1.frm");
        output.frmText.Should().Be(source);
        output.frxContent.Should().Equal(companion);
    }

    [AvaloniaFact]
    public void Designer_geometry_and_tag_edits_preserve_columns_bindings_and_binary_records()
    {
        var path = Corpus("listview-columns");
        var source = File.ReadAllText(Path.Join(path, "Form1.frm"));
        var companion = File.ReadAllBytes(Path.Join(path, "Form1.frx"));
        var form = Read(source, companion);
        var view = form.Components.First(c => c.BaseClass is ListViewComponentClass);
        view.SetProperty(VBProperties.WidthProperty, 400d);
        view.SetProperty(VBProperties.HeightProperty, 200d);
        view.SetProperty(VBProperties.TagProperty, "edited");
        var output = new FormSerializer().Serialize(form, "Form1.frm");
        output.frmText.Should().Contain("      Width           =   6000\r\n")
            .And.Contain("      _ExtentX        =   10583\r\n")
            .And.Contain("      _ExtentY        =   5292\r\n")
            .And.Contain("      Tag             =   \"edited\"\r\n");
        var before = new VbFrmFormatDeserializer().Deserialize(source).Item1.SubComponents[0];
        var after = new VbFrmFormatDeserializer().Deserialize(output.frmText).Item1.SubComponents[0];
        foreach (var entry in before.OrderedRawProperties.Where(p => p.Name.StartsWith("ColumnHeader", StringComparison.Ordinal)
                     || p.Name is "Icons" or "SmallIcons" or "ColHdrIcons"))
            after.OrderedRawProperties.Should().Contain(p => p.Name == entry.Name && p.RawLines.SequenceEqual(entry.RawLines));
        output.frxContent.Should().Equal(companion);
    }

    [AvaloniaFact]
    public void Newly_placed_control_writes_measured_defaults_and_pixel_extents()
    {
        ListViewComponentClass.Instance.DefaultExtent.Should().Be((2646, 1323));
        var form = Read("VERSION 5.00\r\nBegin VB.Form Form1\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n");
        var view = new ComponentInstance(ListViewComponentClass.Instance, "ListView1");
        view.SetProperty(VBProperties.WidthProperty, 100d);
        view.SetProperty(VBProperties.HeightProperty, 50d);
        form.UpdateComponents([.. form.Components, view]);
        var output = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        output.Should().Contain("      _ExtentX        =   2646\r\n      _ExtentY        =   1323\r\n")
            .And.Contain("      LabelWrap       =   -1  'True\r\n      HideSelection   =   -1  'True\r\n")
            .And.Contain("      BorderStyle     =   1\r\n      Appearance      =   1\r\n      NumItems        =   0\r\n");
        ContainerRuntimeHarness.Spawn(output).canvas.Children.Should().ContainSingle();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Designer_shows_name_and_optional_box_without_loading_unimplemented_bindings(bool checkboxes)
    {
        var path = Corpus("listview");
        var source = File.ReadAllText(Path.Join(path, "Form1.frm"));
        var form = Read(source, File.ReadAllBytes(Path.Join(path, "Form1.frx")));
        var view = form.Components.First(c => c.BaseClass is ListViewComponentClass);
        var fields = view.GetPropertyOrDefault(ListViewComponentClass.FilePropertiesProperty)!;
        fields.Properties["Checkboxes"] = checkboxes ? -1 : 0;
        var preview = ListViewComponentClass.Instance.InstantiateForDesigner(view).Should().BeOfType<Border>().Subject;
        if (checkboxes)
            preview.Child.Should().BeOfType<CheckBox>().Which.Content.Should().Be("ListView1");
        else
            preview.Child.Should().BeOfType<TextBlock>().Which.Text.Should().Be("ListView1");
    }

    [AvaloniaFact]
    public void Own_properties_and_columns_are_not_editable_designer_properties()
    {
        var properties = ListViewComponentClass.Instance.Properties;
        properties.Select(p => p.Name).Should().NotIntersectWith(
            ["ColumnHeaders", "ListItems", "Picture", "MouseIcon", "Icons", "SmallIcons", "ColumnHeaderIcons", "Font", "View"]);
    }
}
