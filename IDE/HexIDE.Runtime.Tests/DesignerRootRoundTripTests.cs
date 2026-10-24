using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A UserControl's or PropertyPage's root lists VB6's properties for its kind, not a form's (#1585), and a loaded file
/// saves as it was read. A line the root no longer models, such as the Caption a UserControl HexIDE made before #1538
/// carried, is kept as the file's own text, as any property HexIDE doesn't model is.
/// </summary>
public class DesignerRootRoundTripTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = [];
        public void LogError(string message) => Errors.Add(message);
    }

    private const string Attributes =
        "Attribute VB_Name = \"{0}\"\r\n"
      + "Attribute VB_GlobalNameSpace = False\r\n"
      + "Attribute VB_Creatable = {1}\r\n"
      + "Attribute VB_PredeclaredId = False\r\n"
      + "Attribute VB_Exposed = False\r\n";

    private static (FormDefinition File, string Saved, Sink Sink) RoundTrip(string source, string fileName)
    {
        var sink = new Sink();
        var file = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "Project1"), source, sink)!;
        return (file, new FormSerializer().Serialize(file, fileName).Item1, sink);
    }

    private static ComponentInstance Root(FormDefinition file) => file.Components.Single(c => c.BaseClass is FormComponentClass);

    // As HexIDE wrote a new UserControl before #1538: with a form's Caption, which VB6's UserControl doesn't have.
    [Fact]
    public void A_UserControls_Caption_line_is_kept_as_written()
    {
        var source =
            "VERSION 5.00\r\n"
          + "Begin VB.UserControl UserControl1 \r\n"
          + "   Caption         =   \"UserControl1\"\r\n"
          + "   ClientHeight    =   3600\r\n"
          + "   ClientLeft      =   0\r\n"
          + "   ClientTop       =   0\r\n"
          + "   ClientWidth     =   4800\r\n"
          + "   ScaleHeight     =   3600\r\n"
          + "   ScaleWidth      =   4800\r\n"
          + "End\r\n"
          + string.Format(Attributes, "UserControl1", "True");

        var (file, saved, sink) = RoundTrip(source, "UserControl1.ctl");

        saved.Should().Be(source);
        sink.Errors.Should().BeEmpty();
        Root(file).BaseClass.PropertiesByName.Should().NotContainKey("Caption");
        Root(file).UnknownRawProperties.Select(p => p.Name).Should().Contain("Caption");
    }

    // A UserControl's own design-time properties, which HexIDE doesn't model, are kept beside the ones it does.
    [Fact]
    public void A_UserControls_own_properties_are_kept_beside_the_modelled_ones()
    {
        var source =
            "VERSION 5.00\r\n"
          + "Begin VB.UserControl Gauge \r\n"
          + "   BackColor       =   &H00C0FFFF&\r\n"
          + "   CanGetFocus     =   0   'False\r\n"
          + "   ClientHeight    =   1200\r\n"
          + "   ClientLeft      =   0\r\n"
          + "   ClientTop       =   0\r\n"
          + "   ClientWidth     =   2400\r\n"
          + "   Enabled         =   0   'False\r\n"
          + "   ForwardFocus    =   -1  'True\r\n"
          + "   Public          =   -1  'True\r\n"
          + "   ScaleHeight     =   1200\r\n"
          + "   ScaleWidth      =   2400\r\n"
          + "   Windowless      =   -1  'True\r\n"
          + "End\r\n"
          + string.Format(Attributes, "Gauge", "True");

        var (file, saved, sink) = RoundTrip(source, "Gauge.ctl");

        saved.Should().Be(source);
        sink.Errors.Should().BeEmpty();
        Root(file).GetPropertyOrDefault(VBProperties.EnabledProperty).Should().BeFalse();
        Root(file).UnknownRawProperties.Select(p => p.Name).Should().Contain(["CanGetFocus", "ForwardFocus", "Public", "Windowless"]);
    }

    // A PropertyPage keeps its Caption as a property it models, and its own lines as written.
    [Fact]
    public void A_PropertyPages_Caption_is_modelled_and_its_own_lines_kept()
    {
        var source =
            "VERSION 5.00\r\n"
          + "Begin VB.PropertyPage General \r\n"
          + "   Caption         =   \"General\"\r\n"
          + "   ClientHeight    =   3600\r\n"
          + "   ClientLeft      =   0\r\n"
          + "   ClientTop       =   0\r\n"
          + "   ClientWidth     =   4800\r\n"
          + "   PaletteMode     =   0  'Halftone\r\n"
          + "   ScaleHeight     =   3600\r\n"
          + "   ScaleWidth      =   4800\r\n"
          + "   StandardSize    =   1  'Small\r\n"
          + "End\r\n"
          + string.Format(Attributes, "General", "False");

        var (file, saved, sink) = RoundTrip(source, "General.pag");

        saved.Should().Be(source);
        sink.Errors.Should().BeEmpty();
        Root(file).GetPropertyOrDefault(VBProperties.CaptionProperty).Should().Be("General");
        Root(file).UnknownRawProperties.Select(p => p.Name).Should().Contain(["PaletteMode", "StandardSize"]);
    }

    // The root of a UserControl or PropertyPage HexIDE creates is its kind's, and the file it writes is unchanged: VB6's
    // (#1538).
    [Theory]
    [InlineData(ModuleKind.UserControl, "UserControl1", "VB.UserControl")]
    [InlineData(ModuleKind.PropertyPage, "PropertyPage1", "VB.PropertyPage")]
    public void A_new_designers_root_is_its_kinds(ModuleKind kind, string name, string typeName)
    {
        var module = ModuleDefinition.NewDesigner(new ProjectDefinition(VBProjectType.EXE, "Project1"), name, kind,
            FormComponentClass.ForRoot(typeName));

        Root(module.FormPart!).BaseClass.VBTypeName.Should().Be(typeName).And.Be(module.FormPart!.RootVBTypeName);
        module.FormPart!.Name.Should().Be(name);
    }
}
