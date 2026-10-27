using Avalonia;
using Avalonia.Controls;

namespace HexIDE.Runtime.BuiltinControls;

internal static class VBEnabled
{
    // A host has no parent until its container's template is applied, after Form_Load.
    private static readonly AttachedProperty<Control?> OwnerProperty =
        AvaloniaProperty.RegisterAttached<Control, Canvas, Control?>("EnabledContainerOwner");

    public static void SetOwner(Canvas host, Control owner) => host.SetValue(OwnerProperty, owner);

    public static bool Get(Control control)
    {
        for (Control? current = control; current is not null;
             current = current.GetValue(OwnerProperty) ?? current.Parent as Control)
        {
            if (!current.IsEnabled)
                return false;
        }
        return true;
    }
}
