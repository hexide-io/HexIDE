using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// A control-array element renamed out of its array can be renamed back, and an element can join another array, as
/// VB6 allows: its names are unique by name and Index together (#1276). The rule refused every name already held, so
/// <c>optSize(0)</c> renamed to <c>optBoard</c> could never become <c>optSize(0)</c> again.
/// </summary>
public class ArrayElementRenameTests
{
    private const string Frm = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           Begin VB.OptionButton optSize
              Caption         =   "Small"
              Index           =   0
           End
           Begin VB.OptionButton optSize
              Caption         =   "Medium"
              Index           =   1
           End
           Begin VB.OptionButton optSize
              Caption         =   "Large"
              Index           =   2
           End
           Begin VB.CommandButton cmdGo
              Caption         =   "Go"
           End
           Begin VB.OptionButton optLone
              Caption         =   "Lone"
              Index           =   1
           End
           Begin VB.Data datArr
              Caption         =   "Data"
              Index           =   0
           End
           Begin VB.OptionButton optPlace
              Caption         =   "Here"
              Index           =   0
           End
           Begin VB.Data optPlace
              Caption         =   "There"
              Index           =   1
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private readonly ILocalizationService _loc = Substitute.For<ILocalizationService>();
    private readonly FormDefinition _form;

    public ArrayElementRenameTests()
    {
        _loc.GetString("Str.Naming.Msg.NotAVb6Name").Returns("'{0}' is not a VB6 name");
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        _form = new FormDeserializer().Deserialize(project, Frm, new NullSink())!;
        project.AddForm(_form);
    }

    private ComponentInstance Element(string name, int? index = null) =>
        PropertyText.Named(_form.Components, index is { } i ? $"{name}({i})" : name).Single();

    private string? Refusal(ComponentInstance target, string proposed) =>
        ComponentNaming.RefusalFor(target, target.GetPropertyOrDefault(VBProperties.NameProperty), proposed,
            _form.Components, _form, isRoot: false, _loc);

    private static void Rename(ComponentInstance target, string name) => target.SetProperty(VBProperties.NameProperty, name);

    [Fact]
    public void An_element_renamed_out_of_its_array_can_be_renamed_back()
    {
        var small = Element("optSize", 0);
        Refusal(small, "optBoard").Should().BeNull("a free name");
        Rename(small, "optBoard");

        Refusal(small, "optSize").Should().BeNull("its Index, 0, is free among optSize(1) and optSize(2)");
    }

    [Fact]
    public void An_element_may_join_another_array_where_its_Index_is_free()
    {
        var small = Element("optSize", 0);
        Rename(small, "optBoard");

        Refusal(Element("optSize", 1), "optBoard").Should().BeNull("optBoard holds only Index 0, and this is Index 1");
        Refusal(Element("optSize", 1), "OPTBOARD").Should().BeNull("names are compared without regard to case");
    }

    [Fact]
    public void An_element_may_not_take_an_Index_its_new_array_already_holds()
    {
        // optLone is Index 1, and optSize holds 0, 1 and 2.
        Refusal(Element("optLone"), "optSize").Should().Be("Name must be unique in form");
    }

    [Fact]
    public void A_name_held_by_a_control_with_no_Index_is_still_taken()
    {
        Refusal(Element("optSize", 1), "cmdGo").Should().Be("Name must be unique in form");
    }

    [Fact]
    public void A_control_with_no_Index_does_not_join_an_array()
    {
        // VB6 offers to make an array when a control takes another's name. That is not measured, so it stays refused.
        Refusal(Element("cmdGo"), "optSize").Should().Be("Name must be unique in form");
    }

    [Fact]
    public void A_name_held_by_a_preserved_control_is_still_taken()
    {
        // datArr is kept as text, whose Index HexIDE does not read.
        Refusal(Element("optSize", 1), "datArr").Should().Be("Name must be unique in form");
    }

    [Fact]
    public void An_array_with_a_preserved_element_takes_no_more()
    {
        // optPlace(0) is modelled and has Index 0, which leaves 2 free, but its other element is kept as text, whose Index
        // HexIDE does not read.
        Refusal(Element("optSize", 2), "optPlace").Should().Be("Name must be unique in form");
    }

    [Fact]
    public void An_element_may_change_the_case_of_its_arrays_name()
    {
        // #854 refused this, since the siblings keep the name. Its Index is free among them, so it is an element still.
        Refusal(Element("optSize", 0), "OptSize").Should().BeNull();
    }
}
