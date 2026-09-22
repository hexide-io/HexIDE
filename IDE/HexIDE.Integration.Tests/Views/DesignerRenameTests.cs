using Avalonia.Data;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;
using NSubstitute;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The designer still refuses a rename after its rules moved into <see cref="ComponentNaming"/>, which
/// <c>set_control_property</c> now shares (#494). The refusal is a <see cref="DataValidationException"/>, which is
/// what the Properties window shows and what the tool turns into a refusal.
/// </summary>
public class DesignerRenameTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string TwoButtons = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.CommandButton Command1
           End
           Begin VB.CommandButton Command2
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static FormEditViewModel Designer(out FormDefinition form)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        form = new FormDeserializer().Deserialize(project, TwoButtons, new NullSink())!;
        project.AddForm(form);
        var vm = new FormEditViewModel(
            null!, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!, null!, null!,
            Substitute.For<ILocalizationService>());
        return vm.Initialize(form);
    }

    [AvaloniaFact]
    public void Taking_a_siblings_name_is_refused_and_changes_nothing()
    {
        Designer(out var form);
        var command1 = form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Command1");

        var act = () => command1.SetUntypedProperty(VBProperties.NameProperty, "Command2");

        act.Should().Throw<DataValidationException>().WithMessage("Name must be unique in form");
        command1.GetPropertyOrDefault(VBProperties.NameProperty).Should().Be("Command1");
    }

    [AvaloniaFact]
    public void A_free_name_is_taken()
    {
        Designer(out var form);
        var command1 = form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Command1");

        command1.SetUntypedProperty(VBProperties.NameProperty, "cmdOk");

        command1.GetPropertyOrDefault(VBProperties.NameProperty).Should().Be("cmdOk");
    }
}
