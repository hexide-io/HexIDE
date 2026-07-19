using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

public class TextBoxComponentClass : ComponentBaseClass
{
    public TextBoxComponentClass() : base([TextProperty,
        TabStopProperty,
        TabIndexProperty], [ChangeEvent])
    {
    }

    public override string Name => "Text";
    public override string VBTypeName => "VB.TextBox";

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        return new VBTextBox()
        {
            Text = instance.GetPropertyOrDefault(TextProperty)
        };
    }

    public static readonly EventClass ChangeEvent = new EventClass("Change");

    public static ComponentBaseClass Instance { get; } = new TextBoxComponentClass();
}