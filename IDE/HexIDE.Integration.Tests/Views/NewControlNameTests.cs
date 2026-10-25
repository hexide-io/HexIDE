using System.Linq;
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

/// <summary>
/// The name a control added or pasted in the designer takes: its class's prefix and the lowest number from 1 that no
/// control on the form has, as VB6's designer names it (#540). HexIDE started at 0, so the first button on a form was
/// Command0, and a paste counted up from the number of controls on the canvas.
/// </summary>
/// <remarks>
/// Every expected name is VB6's, measured in its IDE (docs/vb6-fidelity-oracle/the-name-vb6s-designer-gives-a-new-control.md, "The name VB6's designer gives a
/// new control"), and the form is the probe's: Command1 and Command3, with 2 free, and Text5, with 1 to 4 free.
/// </remarks>
public class NewControlNameTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private const string GapForm = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           ClientHeight    =   4500
           ClientLeft      =   60
           ClientTop       =   345
           ClientWidth     =   6000
           ScaleHeight     =   4500
           ScaleWidth      =   6000
           Begin VB.CommandButton Command1
              Caption         =   "Command1"
              Height          =   495
              Left            =   240
              TabIndex        =   0
              Top             =   240
              Width           =   1215
           End
           Begin VB.CommandButton Command3
              Caption         =   "Command3"
              Height          =   495
              Left            =   240
              TabIndex        =   1
              Top             =   960
              Width           =   1215
           End
           Begin VB.TextBox Text5
              Height          =   495
              Left            =   240
              TabIndex        =   2
              Text            =   "Text5"
              Top             =   1680
              Width           =   1215
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private const string EmptyForm = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           ClientHeight    =   4500
           ClientWidth     =   6000
        End
        Attribute VB_Name = "Form1"
        """;

    private static FormEditViewModel Designer(string source)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source, new NullSink())!;
        project.AddForm(form);
        // A toolbox, because adding a control puts the pointer tool back.
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            Substitute.For<IComponentRegistry>(), Substitute.For<ILocalizationService>());
        return new FormEditViewModel(toolbox, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!, null!,
            Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
    }

    private static ComponentInstanceViewModel Control(FormEditViewModel vm, string name) =>
        vm.AllComponents.Single(c => c.Name == name);

    private static string[] Names(FormEditViewModel vm) =>
        vm.AllComponents.Select(c => c.Name).Where(n => n != "Form1").Order().ToArray();

    private static ComponentInstanceViewModel Paste(FormEditViewModel vm, string source)
    {
        vm.SelectedComponent = Control(vm, source);
        vm.CopySelectedControls();
        var before = vm.AllComponents.ToList();
        vm.PasteControls();
        return vm.AllComponents.Single(c => !before.Contains(c));
    }

    [AvaloniaTheory]
    [InlineData("CommandButton", "Command1")]
    [InlineData("TextBox", "Text1")]
    [InlineData("Label", "Label1")]
    [InlineData("Timer", "Timer1")]
    [InlineData("VScrollBar", "VScroll1")]
    public void The_first_control_of_a_class_on_a_form_is_numbered_1(string vbClass, string expected)
    {
        var type = (ComponentBaseClass)typeof(ComponentBaseClass).Assembly
            .GetType($"HexIDE.Runtime.Components.{vbClass}ComponentClass")!
            .GetProperty("Instance")!.GetValue(null)!;
        var vm = Designer(EmptyForm);

        vm.SpawnControlAt(type, new Rect(10, 10, 80, 25));

        vm.SelectedComponent!.Name.Should().Be(expected);
    }

    [AvaloniaFact]
    public void A_new_button_is_captioned_with_its_name()
    {
        var vm = Designer(EmptyForm);

        vm.SpawnControlCenter(CommandButtonComponentClass.Instance);

        vm.SelectedComponent!.Instance.GetPropertyOrDefault(VBProperties.CaptionProperty).Should().Be("Command1");
    }

    [AvaloniaFact]
    public void A_new_control_takes_the_lowest_number_no_control_of_the_form_has()
    {
        var vm = Designer(GapForm);

        vm.SpawnControlCenter(CommandButtonComponentClass.Instance);
        var button = vm.SelectedComponent!.Name;
        vm.SpawnControlCenter(TextBoxComponentClass.Instance);
        var text = vm.SelectedComponent!.Name;

        (button, text).Should().Be(("Command2", "Text1"), "2 was free between Command1 and Command3, and 1 below Text5");
    }

    [AvaloniaFact]
    public void A_number_freed_by_a_delete_is_taken_again()
    {
        var vm = Designer(GapForm);
        vm.SpawnControlCenter(CommandButtonComponentClass.Instance);
        vm.SelectedComponent = Control(vm, "Command2");
        vm.DeleteSelected();

        vm.SpawnControlCenter(CommandButtonComponentClass.Instance);

        vm.SelectedComponent!.Name.Should().Be("Command2", "VB6 gave the deleted button's name to the next one");
    }

    [AvaloniaFact]
    public void A_pasted_control_takes_the_lowest_free_number_and_keeps_the_caption_it_was_copied_with()
    {
        var vm = Designer(GapForm);

        var button = Paste(vm, "Command3");
        var text = Paste(vm, "Text5");

        (button.Name, text.Name).Should().Be(("Command2", "Text1"));
        button.Instance.GetPropertyOrDefault(VBProperties.CaptionProperty).Should().Be("Command3");
        text.Instance.GetPropertyOrDefault(VBProperties.TextProperty).Should().Be("Text5");
        Names(vm).Should().Equal("Command1", "Command2", "Command3", "Text1", "Text5");
    }
}
