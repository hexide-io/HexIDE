using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

public class VBListBox : ListBox
{
    protected override Type StyleKeyOverride => typeof(ListBox);

    static VBListBox()
    {
        // Before Click, so MouseUp comes first, as VB6 raises them (#974).
        AttachedEvents.AttachMouse<VBListBox>(AttachedEvents.DblClick.InPlaceOfMouseDown);
        AttachedEvents.AttachClick<VBListBox>();
    }
}