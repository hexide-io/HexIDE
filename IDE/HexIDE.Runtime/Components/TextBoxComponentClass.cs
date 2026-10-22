using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

public class TextBoxComponentClass : ComponentBaseClass
{
    public TextBoxComponentClass() : base([TextProperty, EnabledProperty,
        BackColorProperty, ForeColorProperty,
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
        var box = new VBTextBox()
        {
            Text = instance.GetPropertyOrDefault(TextProperty),
            // Loaded, so a form's colours are drawn and a program reads them back (#795). They were not modelled.
            [AttachedProperties.BackColorProperty] = instance.GetPropertyOrDefault(BackColorProperty),
            [AttachedProperties.ForeColorProperty] = instance.GetPropertyOrDefault(ForeColorProperty),
        };
        // Kept as the file's own lines rather than modelled, as a form's WindowState is: they are read here and nowhere
        // else, since neither can change once the box exists (#1340). MultiLine is -1 for True, which RawNumber, reading
        // digits only, would miss.
        box.ApplyLayout(VBLoader.RawDouble(instance, "MultiLine") is { } multiLine && multiLine != 0,
            VBLoader.RawNumber(instance, "ScrollBars") ?? 0);
        return box;
    }

    public static readonly EventClass ChangeEvent = new EventClass("Change");

    public static ComponentBaseClass Instance { get; } = new TextBoxComponentClass();
}