using System.Linq;
using Avalonia;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A control is never given a name already on its form: not a control HexIDE keeps only as preserved text, and not a
/// sibling's in another case (#854). vb6.exe compares control names without regard to case when it loads a form, and
/// a form holding two controls of one name and no Index builds without either of them (the oracle's "Two controls of
/// one name"). The designer checked only the controls it models, and mostly case-sensitively.
/// </summary>
public class ControlNameClashTests
{
    // VB.Data has no component class here, so both Data controls are kept as preserved text on the form.
    private const string DataForm = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           Begin VB.Timer Timer1
           End
           Begin VB.Data Data2
              Caption         =   "Data2"
           End
           Begin VB.Data Data1
              Caption         =   "Data1"
           End
           Begin VB.Data Command0
              Caption         =   "Command0"
           End
           Begin VB.Data Text2
              Caption         =   "Text2"
           End
           Begin VB.TextBox Text1
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static (FormEditViewModel vm, FormDefinition form) Designer()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, DataForm, new NullSink())!;
        project.AddForm(form);
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            Substitute.For<IComponentRegistry>(), Substitute.For<ILocalizationService>());
        var vm = new FormEditViewModel(toolbox, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!, null!,
            Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
        return (vm, form);
    }

    private static ComponentInstance Named(FormDefinition form, string name) =>
        form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == name);

    [AvaloniaTheory]
    [InlineData("Data1")]
    [InlineData("data1")]
    [InlineData("DATA2")]
    public void A_control_may_not_take_a_preserved_controls_name_in_any_case(string proposed)
    {
        var (_, form) = Designer();
        var text1 = Named(form, "Text1");

        var rename = () => text1.SetUntypedProperty(VBProperties.NameProperty, proposed);

        rename.Should().Throw<DataValidationException>().WithMessage("Name must be unique in form");
        text1.GetPropertyOrDefault(VBProperties.NameProperty).Should().Be("Text1");
    }

    [AvaloniaFact]
    public void A_control_may_not_take_a_siblings_name_in_another_case()
    {
        var (_, form) = Designer();
        var text1 = Named(form, "Text1");

        var rename = () => text1.SetUntypedProperty(VBProperties.NameProperty, "timer1");

        rename.Should().Throw<DataValidationException>().WithMessage("Name must be unique in form");
    }

    [AvaloniaFact]
    public void A_control_may_still_change_the_case_of_its_own_name()
    {
        var (_, form) = Designer();
        var text1 = Named(form, "Text1");

        text1.SetUntypedProperty(VBProperties.NameProperty, "TEXT1");

        text1.GetPropertyOrDefault(VBProperties.NameProperty).Should().Be("TEXT1");
    }

    [AvaloniaFact]
    public void A_control_drawn_from_the_toolbox_passes_over_a_preserved_controls_name()
    {
        var (vm, _) = Designer();

        vm.SpawnControlAt(CommandButtonComponentClass.Instance, new Rect(10, 10, 80, 25));

        vm.SelectedComponent!.Name.Should().Be("Command1", "Command0 is the preserved Data control's name");
    }

    [AvaloniaFact]
    public void A_pasted_control_passes_over_a_preserved_controls_name()
    {
        var (vm, _) = Designer();
        vm.SelectedComponent = vm.AllComponents.Single(c => c.Name == "Text1");
        vm.CopySelectedControls();

        vm.PasteControls();

        var pasted = vm.AllComponents.Last();
        pasted.Name.Should().StartWith("Text").And.NotBe("Text1").And.NotBe("Text2", "Text2 is the preserved Data control's name");
    }

    [AvaloniaFact]
    public void The_names_in_use_include_every_preserved_control_without_regard_to_case()
    {
        var (_, form) = Designer();

        var names = ComponentNaming.NamesInUse(form.Components);

        names.Should().Contain(["Form1", "Timer1", "Text1", "Data1", "Data2", "Command0", "Text2"]);
        names.Contains("DATA1").Should().BeTrue("VB6 compares control names without regard to case");
    }
}
