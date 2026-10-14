using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

public class VBTextBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);

    static VBTextBox()
    {
        TextProperty.Changed.AddClassHandler<VBTextBox>((textBox, e) =>
        {
            textBox.ExecuteSub(TextBoxComponentClass.ChangeEvent);
        });
        // Before Click, so MouseUp comes first, as VB6 raises them (#974).
        AttachedEvents.AttachMouse<VBTextBox>();
        AttachedEvents.AttachClick<VBTextBox>();
    }
}