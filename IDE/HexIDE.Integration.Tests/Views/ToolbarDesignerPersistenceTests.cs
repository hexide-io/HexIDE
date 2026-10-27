using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

// C3 (#1088): property bags the designer cannot write must never offer edits it would discard.
public class ToolbarDesignerPersistenceTests
{
    private sealed class Sink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static string CorpusDirectory()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            var candidate = Path.Join(at.FullName, "corpus", "inbox-controls", "saved", "toolbar");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("The Toolbar corpus ships with the repository.");
    }

    [AvaloniaFact]
    public void Carried_toolbars_offer_no_property_edits_and_survive_a_properties_window_edit_to_the_form()
    {
        var directory = CorpusDirectory();
        var source = Vb6TextFile.ReadDocument(File.ReadAllBytes(Path.Join(directory, "Form1.frm"))).Text;
        var companion = File.ReadAllBytes(Path.Join(directory, "Form1.frx"));
        var file = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source,
            new Sink(), FrxDeserializer.Read(companion, source))!;
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var designer = new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(),
            null!, null!, Substitute.For<ISettingsService>(), localization).Initialize(file);
        var dock = Substitute.For<IDocumentDockService>();
        dock.ActiveDocument.Returns(designer);
        var properties = new PropertiesToolViewModel(dock, Substitute.For<IEventBus>(),
            Substitute.For<IWindowManager>(), localization);

        designer.UnmodelledControls.Where(c => c.TypeName == "MSComctlLib.Toolbar").Select(c => c.Name)
            .Should().BeEquivalentTo(new[] { "Toolbar1", "Toolbar2" });
        properties.ComponentsProxy!.Select(c => c.Name).Should().NotContain(new[] { "Toolbar1", "Toolbar2" },
            "carried Toolbar scalars and Buttons must be view-only until a faithful writer exists");
        designer.AllComponents.Should().NotContain(c => c.Instance.BaseClass.Name == "Toolbar");

        designer.SelectedComponent = designer.AllComponents.Single(c => c.Name == "Form1");
        properties.Properties.Single(p => p.PropertyClass == VBProperties.CaptionProperty).Value = "Edited";
        designer.CommitLayout();
        var (text, bytes) = new FormSerializer().Serialize(file, "Form1.frm");
        Encoding.ASCII.GetBytes(text).Should().Equal(Encoding.ASCII.GetBytes(source.Replace(
            "Caption         =   \"Form1\"", "Caption         =   \"Edited\"", StringComparison.Ordinal)));
        bytes.Should().Equal(companion);

        var reread = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), text,
            new Sink(), FrxDeserializer.Read(bytes!, text))!;
        reread.Components.Single(c => c.BaseClass is FormComponentClass)
            .GetPropertyOrDefault(VBProperties.CaptionProperty).Should().Be("Edited");
        reread.Components.Should().NotContain(c => c.BaseClass.Name == "Toolbar");
        var second = new FormSerializer().Serialize(reread, "Form1.frm");
        second.frmText.Should().Be(text);
        second.frxContent.Should().Equal(companion);
    }
}
