using System;
using Avalonia.Controls;

namespace HexIDE.Runtime.BuiltinControls;

public class VBCommandButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    static VBCommandButton()
    {
        AttachedEvents.AttachFocusEvents<VBCommandButton>();
        AttachedEvents.AttachClick<VBCommandButton>();
        // MouseUp follows Click, which the Button raised in its own release handler (measured, #974).
        AttachedEvents.AttachMouse<VBCommandButton>();
        ContentTemplateProperty.OverrideDefaultValue<VBCommandButton>(AccessTextDataTemplate.Access);
    }
}