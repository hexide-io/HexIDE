using Avalonia;
using Avalonia.Controls;

namespace HexIDE.Runtime.BuiltinControls;

public class VBProps
{
    public static readonly AttachedProperty<string?> NameProperty = AvaloniaProperty.RegisterAttached<VBProps, Control, string?>("Name");
    public static string? GetName(AvaloniaObject element) => element.GetValue(NameProperty);
    public static void SetName(AvaloniaObject element, string? value) => element.SetValue(NameProperty, value);

    // A control-array element's own Index (Command1(2) → 2). Null on a standalone control. Event dispatch reads it
    // to pass the leading `Index As Integer` arg to a shared handler (Command1_Click(Index)); the Locals tree /
    // `Ctrl.Index` reads reflect it too.
    public static readonly AttachedProperty<int?> IndexProperty = AvaloniaProperty.RegisterAttached<VBProps, Control, int?>("Index");
    public static int? GetIndex(AvaloniaObject element) => element.GetValue(IndexProperty);
    public static void SetIndex(AvaloniaObject element, int? value) => element.SetValue(IndexProperty, value);
}