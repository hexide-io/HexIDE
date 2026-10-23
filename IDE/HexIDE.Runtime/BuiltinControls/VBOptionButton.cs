using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
        // MouseUp follows Click, as a CheckBox's does (#974).
        AttachedEvents.AttachMouse<VBOptionButton>(AttachedEvents.DblClick.AfterMouseDown);
        // An option button that takes the focus is selected: Windows checks an automatic radio button as it is focused. By
        // SetFocus, and by the Tab key, which reaches only the selected option of a group, or the first it meets in a group
        // with none (measured, #1583). Not by a press, which selects it on its own path, with Click after MouseDown
        // (measured, #974). It is selected before its GotFocus runs, which reads it selected, and its Click comes after
        // (measured, #1101 and #1583). So the Click is held here and raised by the handler after GotFocus's, whether the
        // two run at once or wait their turn behind a running handler.
        GotFocusEvent.AddClassHandler<VBOptionButton>((option, e) =>
        {
            if (!ReferenceEquals(e.Source, option) || e is not FocusChangedEventArgs { NavigationMethod: not NavigationMethod.Pointer }
                || option.IsChecked == true)
                return;
            option.clickAfterFocus = true;
            option.IsChecked = true;
        }, handledEventsToo: true);
        AttachedEvents.AttachFocusEvents<VBOptionButton>();
        GotFocusEvent.AddClassHandler<VBOptionButton>((option, _) =>
        {
            if (!option.clickAfterFocus)
                return;
            option.clickAfterFocus = false;
            option.ExecuteSub(ComponentBaseClass.ClickEvent);
        }, handledEventsToo: true);
        // The TabStop the form or code gave it, kept apart from the stop the group allows.
        KeyboardNavigation.IsTabStopProperty.Changed.AddClassHandler<VBOptionButton>((option, e) =>
        {
            if (option.settingTabStop)
                return;
            option.tabStop = e.GetNewValue<bool>();
            option.UpdateGroupTabStops();
        });
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
            if (isChecked && e.OldValue as bool? != true && !option.clickAfterFocus)
                option.ExecuteSub(ComponentBaseClass.ClickEvent);
            option.UpdateGroupTabStops();
        });

        ContentTemplateProperty.OverrideDefaultValue<VBOptionButton>(AccessTextDataTemplate.Access);
        // Named as the caption reads, not with the _ the drawn text marks its access key with (#954).
        VBCaption.NameByCaption<VBOptionButton>(ContentProperty);
    }

    // An option group, the option buttons in one container, is one Tab stop: its selected option, or, with none selected,
    // each of them, so the Tab key reaches the first in its direction and selects it (measured, #1583). Each keeps its own
    // TabStop besides.
    private bool tabStop = true;
    private bool settingTabStop;

    // Set while the focus selects it, until its GotFocus has been raised.
    private bool clickAfterFocus;

    private IEnumerable<VBOptionButton> Group => (Parent as Panel)?.Children.OfType<VBOptionButton>() ?? [this];

    private void UpdateGroupTabStops()
    {
        var group = Group.ToList();
        var anySelected = group.Any(o => o.IsChecked == true);
        foreach (var option in group)
            option.ApplyTabStop(option.IsChecked == true || !anySelected);
    }

    private void ApplyTabStop(bool groupStop)
    {
        settingTabStop = true;
        try
        {
            KeyboardNavigation.SetIsTabStop(this, tabStop && groupStop);
        }
        finally
        {
            settingTabStop = false;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateGroupTabStops();
    }

    // The group it leaves may have lost its selected option, which makes the rest Tab stops again.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        var group = Group.Where(o => !ReferenceEquals(o, this)).ToList();
        base.OnDetachedFromVisualTree(e);
        if (group.Count > 0)
            group[0].UpdateGroupTabStops();
    }
}
