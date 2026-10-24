using System;
using Avalonia.Controls;

namespace HexIDE.Runtime.BuiltinControls;

public class VBCommandButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    static VBCommandButton()
    {
        AttachedEvents.AttachFocusEvents<VBCommandButton>();
        // An arrow moves the focus to the previous or next control in its container (measured, #1625).
        ArrowNavigation.Attach<VBCommandButton>();
        AttachedEvents.AttachClick<VBCommandButton>();
        // MouseUp follows Click, which the Button raised in its own release handler (measured, #974).
        AttachedEvents.AttachMouse<VBCommandButton>();
        ContentTemplateProperty.OverrideDefaultValue<VBCommandButton>(AccessTextDataTemplate.Access);
        // Named as the caption reads, not with the _ the drawn text marks its access key with (#954).
        VBCaption.NameByCaption<VBCommandButton>(ContentProperty);
    }
}