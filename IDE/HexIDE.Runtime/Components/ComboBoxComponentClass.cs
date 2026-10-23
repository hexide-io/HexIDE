using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

public class ComboBoxComponentClass : ComponentBaseClass
{
    public ComboBoxComponentClass() : base([TextProperty,
        EnabledProperty,
        FontProperty,
        ForeColorProperty,
        BackColorProperty,
        ListProperty,
        ItemDataProperty,
        LockedProperty,
        ComboStyleProperty,
        MousePointerProperty,
        RightToLeftProperty,
        AppearanceProperty,
        TabStopProperty,
        TabIndexProperty,
        ToolTipTextProperty], [ChangeEvent, ClickEvent, DropDownEvent, GotFocusEvent, LostFocusEvent])
    {
    }

    /// <summary>As the list opens, by the mouse, F4 or Alt+Down (measured, #1429).</summary>
    public static readonly EventClass DropDownEvent = new("DropDown");

    // A combo's Change is the TextBox's event: the same name, no arguments.
    private static readonly EventClass ChangeEvent = TextBoxComponentClass.ChangeEvent;

    public override string Name => "Combo";
    public override string VBTypeName => "VB.ComboBox";
    public override (int Width, int Height) AddedSize => (1215, 315);

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        var style = instance.GetPropertyOrDefault(ComboStyleProperty);
        return new VBComboBox(style)
        {
            // A Dropdown List ignores a saved Text (measured): its Text is its selected item, and none is selected.
            Text = style == VBComboStyle.DropdownList ? "" : instance.GetPropertyOrDefault(TextProperty),
            // No ItemsSource. List is the companion record verbatim, and turning it into items needs a
            // non-empty example to decode against — every List record in VB6's Template tree is an empty
            // two-byte count. Binding it directly would compile and be wrong: byte[] IS an IEnumerable, so
            // a populated list would render as a column of raw byte values.
            //
            // This renders empty, exactly as it did before: nothing ever populated the old strings model
            // from a file either.
            [AttachedProperties.BackColorProperty] = instance.GetPropertyOrDefault(BackColorProperty),
            [AttachedProperties.ForeColorProperty] = instance.GetPropertyOrDefault(ForeColorProperty),
            [AttachedProperties.FontProperty] = instance.GetPropertyOrDefault(FontProperty),
            Cursor = instance.GetPropertyOrDefault(MousePointerProperty).ToCursor(),
            FlowDirection = instance.GetPropertyOrDefault(RightToLeftProperty) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };
    }

    /// <summary>
    /// VB6's designer draws a Dropdown List's box with the control's name: the name it has now, after a rename too, and not
    /// a Text saved in the file (measured, #1604). The running control shows nothing there until an item is selected. The
    /// name is drawn as an item is, in the control's ForeColor, not as a hint.
    /// </summary>
    public override Control InstantiateForDesigner(ComponentInstance instance)
    {
        var control = Instantiate(instance);
        if (control is VBComboBox { IsDropdownList: true } combo)
        {
            combo.PlaceholderText = instance.GetPropertyOrDefault(NameProperty);
            combo[!ComboBox.PlaceholderForegroundProperty] = combo[!TemplatedControl.ForegroundProperty];
        }
        return control;
    }

    /// <summary>A Dropdown List: its Text is never written to the .frm, as VB6's designer never writes it (measured).</summary>
    public static bool IsDropdownList(ComponentInstance instance) =>
        instance.GetPropertyOrDefault(ComboStyleProperty) == VBComboStyle.DropdownList;

    /// <summary>
    /// Setting Style to Dropdown List resets the Text to the control's name, so a Text typed earlier is gone when Style goes
    /// back (measured on VB6's designer, #926).
    /// </summary>
    public override IReadOnlyList<PropertyChange> ApplyDesignerConsequences(ComponentInstance instance, PropertyClass property)
    {
        if (!ReferenceEquals(property, ComboStyleProperty) || !IsDropdownList(instance))
            return [];
        var before = instance.GetPropertyOrDefault(TextProperty);
        var name = instance.GetPropertyOrDefault(NameProperty) ?? "";
        if (before == name)
            return [];
        instance.SetProperty(TextProperty, name);
        return [new PropertyChange(TextProperty, before, name)];
    }

    static ComboBoxComponentClass()
    {
        BackColorProperty.OverrideDefault<ComboBoxComponentClass>(VBColor.FromSystemColor(VbSystemColor.Window));
        // The window's text colour, not the button text every other control defaults to (measured: -2147483640).
        ForeColorProperty.OverrideDefault<ComboBoxComponentClass>(VBColor.FromSystemColor(VbSystemColor.Windowtext));
    }

    public static ComponentBaseClass Instance { get; } = new ComboBoxComponentClass();
}