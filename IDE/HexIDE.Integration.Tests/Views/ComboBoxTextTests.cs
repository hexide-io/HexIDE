using Avalonia;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

public class ComboBoxTextTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    // A control drawn in the designer starts with its name as its Text, as VB6's designer writes it (the oracle's "Drawn
    // in the VB6 IDE's designer"), and has no Caption it was never given.
    [AvaloniaTheory]
    [InlineData("TextBox")]
    [InlineData("ComboBox")]
    public void A_drawn_TextBox_or_ComboBox_starts_with_its_name_as_its_Text(string kind)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project,
            "VERSION 5.00\r\nBegin VB.Form Form1\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n", new NullSink())!;
        project.AddForm(form);
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            InBoxRegistry.Create(), Substitute.For<ILocalizationService>());
        var vm = new FormEditViewModel(toolbox, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!, null!,
            Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
        ComponentBaseClass type = kind == "TextBox" ? TextBoxComponentClass.Instance : ComboBoxComponentClass.Instance;

        vm.SpawnControlAt(type, new Rect(10, 10, 80, 25));

        var drawn = vm.SelectedComponent!.Instance;
        drawn.GetPropertyOrDefault(VBProperties.TextProperty).Should().Be(vm.SelectedComponent.Name);
        drawn.GetAllSetProperties().Keys.Should().NotContain(VBProperties.CaptionProperty);
        vm.FlushComponentsToModel();
        var (frm, _) = new FormSerializer().Serialize(form, "Form1.frm");
        frm.Should().Contain($"      Text            =   \"{vm.SelectedComponent.Name}\"\r\n");
    }
}
