using Avalonia.Controls;
using Avalonia.Input;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

public class CommandButtonComponentClass : ComponentBaseClass
{
    public CommandButtonComponentClass() : base([CaptionProperty,
        BackColorProperty,
        ForeColorProperty,
        AppearanceProperty,
        FontProperty,
        MousePointerProperty,
        // Registered so the blob it cites survives a save, not because a graphical button renders yet.
        // An unmodelled blob-backed property makes the whole FORM refuse to save, so this is what lets
        // VB6's own Mover ListBox.frm and Button ListBox.frm round-trip at all.
        PictureProperty,
        EnabledProperty,
        TabStopProperty,
        TabIndexProperty], [ClickEvent, GotFocusEvent, LostFocusEvent])
    {
    }

    public override string Name => "Command";
    public override string VBTypeName => "VB.CommandButton";

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        return new VBCommandButton()
        {
            Content = instance.GetPropertyOrDefault(CaptionProperty),
            [AttachedProperties.BackColorProperty] = instance.GetPropertyOrDefault(BackColorProperty),
            [AttachedProperties.ForeColorProperty] = instance.GetPropertyOrDefault(ForeColorProperty),
            [AttachedProperties.FontProperty] = instance.GetPropertyOrDefault(FontProperty),
            Cursor = instance.GetPropertyOrDefault(MousePointerProperty).ToCursor(),
        };
    }

    public static CommandButtonComponentClass Instance { get; } = new();
}