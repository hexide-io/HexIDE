using Avalonia.Controls;
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
        MousePointerProperty,
        RightToLeftProperty,
        AppearanceProperty,
        TabStopProperty,
        TabIndexProperty], [ChangeEvent, ClickEvent, DropDownEvent])
    {
    }

    /// <summary>As the list opens, by the mouse, F4 or Alt+Down (measured, #1429).</summary>
    public static readonly EventClass DropDownEvent = new("DropDown");

    // A combo's Change is the TextBox's event: the same name, no arguments.
    private static readonly EventClass ChangeEvent = TextBoxComponentClass.ChangeEvent;

    public override string Name => "Combo";
    public override string VBTypeName => "VB.ComboBox";

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        var style = StyleOf(instance);
        return new VBComboBox(style)
        {
            // A Dropdown List ignores a saved Text (measured), and its Text is #1428's.
            Text = style is 0 or 1 ? instance.GetPropertyOrDefault(TextProperty) : "",
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

    // Style is not one of this control's properties yet (#1428), so the form reader keeps its line as written,
    // `Style = 2 'Dropdown List`. A value this cannot read is taken as 2, so its Text is refused rather than edited.
    public static int StyleOf(ComponentInstance instance)
    {
        foreach (var property in instance.UnknownRawProperties)
        {
            if (!property.Name.Equals("Style", System.StringComparison.OrdinalIgnoreCase) || property.Lines.Count == 0)
                continue;
            var line = property.Lines[0];
            var value = line[(line.IndexOf('=') + 1)..].TrimStart();
            var digits = value.Length - value.TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').Length;
            return int.TryParse(value[..digits], out var style) ? style : 2;
        }
        return 0;
    }

    /// <summary>A Dropdown List: its Text is never written to the .frm, as VB6's designer never writes it (measured).</summary>
    public static bool IsDropdownList(ComponentInstance instance) => StyleOf(instance) is not (0 or 1);

    static ComboBoxComponentClass()
    {
        BackColorProperty.OverrideDefault<ComboBoxComponentClass>(VBColor.FromSystemColor(VbSystemColor.Window));
        // The window's text colour, not the button text every other control defaults to (measured: -2147483640).
        ForeColorProperty.OverrideDefault<ComboBoxComponentClass>(VBColor.FromSystemColor(VbSystemColor.Windowtext));
    }

    public static ComponentBaseClass Instance { get; } = new ComboBoxComponentClass();
}