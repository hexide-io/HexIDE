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
        ContentTemplateProperty.OverrideDefaultValue<VBCommandButton>(AccessTextDataTemplate.Access);
    }
}