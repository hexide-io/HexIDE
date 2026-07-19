using System;
using Avalonia.Controls;

namespace HexIDE.Runtime.BuiltinControls;

public class VBComboBox : ComboBox
{
    protected override Type StyleKeyOverride => typeof(ComboBox);
}