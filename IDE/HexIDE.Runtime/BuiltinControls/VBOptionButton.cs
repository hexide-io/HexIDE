using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.BuiltinControls;

public class VBOptionButton : RadioButton
{
    public static readonly StyledProperty<VBAppearance> AppearanceProperty = AvaloniaProperty.Register<VBOptionButton, VBAppearance>(nameof(Appearance), VBAppearance._3D);

    public VBAppearance Appearance
    {
        get => GetValue(AppearanceProperty);
        set => SetValue(AppearanceProperty, value);
    }

    /// <summary>
    /// VB6's <c>Value</c> — a <b>Boolean</b>, and the one property an option button is for. See
    /// <see cref="VBProperties.OptionValueProperty"/> for why this is not the check box's tri-state.
    /// </summary>
    public static readonly StyledProperty<bool> ValueProperty = AvaloniaProperty.Register<VBOptionButton, bool>(nameof(Value));

    public bool Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    static VBOptionButton()
    {
        AppearanceProperty.Changed.AddClassHandler<VBOptionButton>((option, e) =>
        {
            if (option.Appearance == VBAppearance._3D)
                option.Theme = null;
            else
                option.Theme = Application.Current?.FindResource("FlatVBOptionButton") as ControlTheme;
        });

        // Value → IsChecked, including the FALSE direction. `Option1.Value = False` on the selected member
        // of a group is honoured by VB6 and leaves the group with NOTHING selected — it is not refused, and
        // no other member is promoted in its place (measured; see docs/vb6-fidelity-oracle.md). That is only
        // reachable from code: a user cannot clear a group by clicking.
        ValueProperty.Changed.AddClassHandler<VBOptionButton>((option, _) => option.IsChecked = option.Value);

        // IsChecked → Value, and the Click event, in ONE place so that a user click and a `Value = True`
        // assignment travel the same path. VB6 fires Click on the TRANSITION to selected and on nothing
        // else, whichever way the transition was caused:
        //
        //   - `Option1.Value = True` from code fires Option1_Click — the answer that was open on #95, and
        //     the reason this cannot live in OnClick, which only a user reaches;
        //   - the sibling that gets DESELECTED does not fire — only one Click per switch, not two;
        //   - re-selecting the already-selected member fires nothing, from code or from a click;
        //   - a designer-set `Value = -1 'True` fires nothing at load. That falls out rather than being
        //     special-cased: the control is instantiated before VBLoader attaches it, so ExecuteSub finds
        //     no execution root and no-ops. FiresNoClick_WhenTheDesignerSetIt pins it, because it would
        //     otherwise break silently the first time instantiation and attachment swapped order.
        IsCheckedProperty.Changed.AddClassHandler<VBOptionButton>((option, e) =>
        {
            var isChecked = option.IsChecked == true;
            if (option.Value != isChecked)
                option.Value = isChecked;
            if (isChecked && e.OldValue as bool? != true)
                option.ExecuteSub(ComponentBaseClass.ClickEvent);
        });

        ContentTemplateProperty.OverrideDefaultValue<VBOptionButton>(AccessTextDataTemplate.Access);
    }
}
