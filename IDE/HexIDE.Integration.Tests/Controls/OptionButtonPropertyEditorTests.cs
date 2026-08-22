using System.Linq;
using Avalonia.Headless.XUnit;
using HexIDE.Controls;
using HexIDE.Runtime.Components;

namespace HexIDE.Integration.Tests.Controls;

/// <summary>
/// The designer half of #95: what the Properties window offers for an option button's <c>Value</c>.
///
/// VB6 shows a two-item False/True dropdown there, because <c>OptionButton.Value</c> is a Boolean.
/// HexIDE showed the check box's three-item <i>Unchecked / Checked / Grayed</i> list, because both
/// controls shared one property class — so the designer offered a state an option button cannot hold,
/// and picking it wrote <c>Value = 1 'Checked</c> into the .frm where VB6 writes <c>-1 'True</c>.
///
/// This drives the real editor rather than asserting the property's Type, because the type is only
/// interesting for what the editor does with it, and PropertyEnumBox is what a user actually meets.
/// </summary>
public class OptionButtonPropertyEditorTests
{
    private static PropertyEnumBox EditorFor(ComponentBaseClass componentClass, string propertyName)
    {
        var property = componentClass.PropertiesByName[propertyName];
        var box = new PropertyEnumBox { PropertyType = property.PropertyType };
        box.SelectedValue = property.BoxedDefaultValue(componentClass);
        return box;
    }

    [AvaloniaFact]
    public void AnOptionButtonsValue_OffersFalseAndTrue()
    {
        var box = EditorFor(OptionButtonComponentClass.Instance, "Value");

        box.Options!.Select(o => o.Text).Should().Equal("False", "True");
        box.SelectedViewModel!.UnderlyingValue.Should().Be(false);   // unselected is the default
    }

    [AvaloniaFact]
    public void ACheckBoxesValue_StillOffersTheThreeCheckStates()
    {
        // The other half of the same assertion: separating the two must not have flattened the check box,
        // which really does have three states.
        var box = EditorFor(CheckBoxComponentClass.Instance, "Value");

        box.Options!.Should().HaveCount(3);
        box.Options!.Select(o => o.Text).Should().Contain(n => n.EndsWith("Grayscale"));
    }
}
