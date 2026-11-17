using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HexIDE.VisualDesigner;

// In the Menu Editor, an access key focuses the action it activates (#4142).
public sealed class MenuEditorButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnAccessKey(RoutedEventArgs e)
    {
        Focus(NavigationMethod.Tab);
        base.OnAccessKey(e);
    }
}

public sealed class MenuEditorCheckBox : CheckBox
{
    protected override Type StyleKeyOverride => typeof(CheckBox);

    protected override void OnAccessKey(RoutedEventArgs e)
    {
        Focus(NavigationMethod.Tab);
        base.OnAccessKey(e);
    }
}
