using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

public class TextBoxComponentClass : ComponentBaseClass
{
    public TextBoxComponentClass() : base([TextProperty, EnabledProperty,
        BackColorProperty, ForeColorProperty, FontProperty,
        TabStopProperty,
        TabIndexProperty], [ChangeEvent, ClickEvent, GotFocusEvent, LostFocusEvent, MouseDownEvent, MouseMoveEvent, MouseUpEvent])
    {
    }

    static TextBoxComponentClass()
    {
        // A text box is drawn in the window colours, not the button face the other controls default to (#795).
        BackColorProperty.OverrideDefault<TextBoxComponentClass>(VBColor.FromSystemColor(VbSystemColor.Window));
        ForeColorProperty.OverrideDefault<TextBoxComponentClass>(VBColor.FromSystemColor(VbSystemColor.Windowtext));
    }

    public override string Name => "Text";
    public override string VBTypeName => "VB.TextBox";

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        return new VBTextBox()
        {
            Text = instance.GetPropertyOrDefault(TextProperty),
            // Loaded, so a form's colours are drawn and a program reads them back (#795). They were not modelled.
            [AttachedProperties.BackColorProperty] = instance.GetPropertyOrDefault(BackColorProperty),
            [AttachedProperties.ForeColorProperty] = instance.GetPropertyOrDefault(ForeColorProperty),
            // Its own font, as every sibling control has had, so it draws in it and a program reads it (#915). It was not
            // modelled, so the box drew in the IDE's font at design time and the form's at run time.
            [AttachedProperties.FontProperty] = instance.GetPropertyOrDefault(FontProperty),
        };
    }

    public static readonly EventClass ChangeEvent = new EventClass("Change");

    public static ComponentBaseClass Instance { get; } = new TextBoxComponentClass();
}