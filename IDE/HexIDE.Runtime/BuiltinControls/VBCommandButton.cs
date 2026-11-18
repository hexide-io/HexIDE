using System;
using Avalonia.Controls;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

public class VBCommandButton : Button
{
    protected override Type StyleKeyOverride => typeof(VBCommandButton);

    internal void SetDisplayAsDefault(bool value) => PseudoClasses.Set(":default", value);

    private bool defaultButton;
    private bool cancelButton;

    internal bool Default
    {
        get => defaultButton;
        set => SetRole(value, false);
    }

    internal bool Cancel
    {
        get => cancelButton;
        set => SetRole(value, true);
    }

    // #4371, oracle PR #4376: each role has one holder; disabling or hiding it keeps the role.
    private void SetRole(bool value, bool cancel)
    {
        if (cancel)
            cancelButton = value;
        else
            defaultButton = value;
        if (value && FormControls.TryOf(this, out var form))
            foreach (var control in form.All)
                if (control is VBCommandButton other && other != this)
                {
                    if (cancel)
                        other.cancelButton = false;
                    else
                        other.defaultButton = false;
                }
        if (!cancel)
            UserControlKeys.UpdateDefault(this);
    }

    static VBCommandButton()
    {
        ControlKeyEvents.Attach<VBCommandButton>();
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