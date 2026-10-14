using Avalonia;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinTypes;

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

    // The ScaleMode a container gives the controls on it: the unit a program reads and writes their Left, Top, Width and
    // Height in (#933). Set on the canvas they are placed on: the form's own, and a PictureBox's child host; a Frame has
    // none, and its children stay in twips even on a pixel form (measured).
    public static readonly AttachedProperty<VBScaleMode> ScaleModeProperty =
        AvaloniaProperty.RegisterAttached<VBProps, Control, VBScaleMode>("ScaleMode", VBScaleMode.Twip);
    public static VBScaleMode GetScaleMode(AvaloniaObject element) => element.GetValue(ScaleModeProperty);
    public static void SetScaleMode(AvaloniaObject element, VBScaleMode value) => element.SetValue(ScaleModeProperty, value);

    // How far in from a control's edge its client area starts, in pixels: where a mouse event's X and Y count from (#974).
    // A 3-D bordered TextBox's and ListBox's start 2 pixels in (measured with GetClientRect); every other control's at 0.
    public static readonly AttachedProperty<double> ClientInsetProperty =
        AvaloniaProperty.RegisterAttached<VBProps, Control, double>("ClientInset");
    public static double GetClientInset(AvaloniaObject element) => element.GetValue(ClientInsetProperty);
    public static void SetClientInset(AvaloniaObject element, double value) => element.SetValue(ClientInsetProperty, value);
}