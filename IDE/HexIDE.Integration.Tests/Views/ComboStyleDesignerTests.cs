using Avalonia;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A ComboBox's Style in the designer (#1428), as VB6's designer was measured to do it (the oracle's "Drawn in the VB6
/// IDE's designer"): setting Style to 2 resets the Text to the control's name, a Dropdown List's Text is never written,
/// and going back to Style 0 writes that name, so a Text typed earlier is gone.
/// </summary>
public class ComboStyleDesignerTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static (FormEditViewModel Vm, FormDefinition Form, ComponentInstance Combo) Drawn()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project,
            "VERSION 5.00\r\nBegin VB.Form Form1\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n", new NullSink())!;
        project.AddForm(form);
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            Substitute.For<IComponentRegistry>(), Substitute.For<ILocalizationService>());
        var vm = new FormEditViewModel(toolbox, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!, null!,
            Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
        vm.SpawnControlAt(ComboBoxComponentClass.Instance, new Rect(10, 10, 80, 25));
        return (vm, form, vm.SelectedComponent!.Instance);
    }

    // As the Properties window and set_control_property both do it: set the value, then record it.
    private static void Set<T>(FormEditViewModel vm, ComponentInstance combo, PropertyClass<T> property, T value)
    {
        var before = combo.GetBoxedPropertyOrDefault(property);
        combo.SetProperty(property, value);
        vm.PushSetPropertyCommand(combo, property, before, value);
    }

    private static string Saved(FormEditViewModel vm, FormDefinition form)
    {
        vm.FlushComponentsToModel();
        return new FormSerializer().Serialize(form, "Form1.frm").Item1;
    }

    [AvaloniaFact]
    public void Setting_Style_2_resets_the_Text_to_the_name_and_Style_0_writes_the_name()
    {
        var (vm, form, combo) = Drawn();
        Set(vm, combo, VBProperties.TextProperty, "zz");

        Set(vm, combo, VBProperties.ComboStyleProperty, VBComboStyle.DropdownList);

        combo.GetPropertyOrDefault(VBProperties.TextProperty).Should().Be("Combo1");
        var list = Saved(vm, form);
        list.Should().Contain("      Style           =   2  'Dropdown List\r\n").And.NotContain("Text            =");

        Set(vm, combo, VBProperties.ComboStyleProperty, VBComboStyle.DropdownCombo);

        Saved(vm, form).Should().Contain("      Text            =   \"Combo1\"\r\n").And.NotContain("Style           =");
    }

    // One change: Undo puts the Style and the Text back together, and Redo makes both again.
    [AvaloniaFact]
    public void Undo_takes_back_the_Style_and_the_Text_together()
    {
        var (vm, _, combo) = Drawn();
        Set(vm, combo, VBProperties.TextProperty, "zz");
        Set(vm, combo, VBProperties.ComboStyleProperty, VBComboStyle.DropdownList);

        vm.UndoStack.Undo();

        combo.GetPropertyOrDefault(VBProperties.ComboStyleProperty).Should().Be(VBComboStyle.DropdownCombo);
        combo.GetPropertyOrDefault(VBProperties.TextProperty).Should().Be("zz");

        vm.UndoStack.Redo();

        combo.GetPropertyOrDefault(VBProperties.ComboStyleProperty).Should().Be(VBComboStyle.DropdownList);
        combo.GetPropertyOrDefault(VBProperties.TextProperty).Should().Be("Combo1");
    }

    // Only Style 2 does it: a Simple Combo keeps its Text.
    [AvaloniaFact]
    public void Setting_Style_1_keeps_the_Text()
    {
        var (vm, _, combo) = Drawn();
        Set(vm, combo, VBProperties.TextProperty, "zz");

        Set(vm, combo, VBProperties.ComboStyleProperty, VBComboStyle.SimpleCombo);

        combo.GetPropertyOrDefault(VBProperties.TextProperty).Should().Be("zz");
    }
}
