using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
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

    /// <summary>
    /// Gives a control of a running form the name its code uses as its automation id: <c>Text1</c>, or <c>Command1(0)</c>
    /// for an element of a control array, so that a tree shows it and a tool reaches it as <c>#Text1</c> (#954). Run time
    /// only: the designer's controls keep the ids it gives them (#526).
    /// <para>
    /// An id makes a node of a control the tree used to fold away as plumbing, so a Frame, PictureBox, Label or Image is
    /// also given the type it is, not Avalonia's None: a control in a Frame is <c>Group[Frame1]/Button[cmdOK]</c>.
    /// </para>
    /// </summary>
    public static void SetRunTimeId(Control control)
    {
        AutomationProperties.SetAutomationId(
            control, GetIndex(control) is { } index ? $"{GetName(control)}({index})" : GetName(control));
        AutomationControlType? type = control switch
        {
            VBFrame => AutomationControlType.Group,
            VBPictureBox => AutomationControlType.Pane,
            VBLabel => AutomationControlType.Text,
            VBImage => AutomationControlType.Image,
            _ => null,
        };
        if (type is not null)
            AutomationProperties.SetControlTypeOverride(control, type);
    }

    // A control made by Controls.Add (#1556): only Controls.Remove takes it out, and it has no event handlers by its name.
    public static readonly AttachedProperty<bool> AddedAtRunTimeProperty =
        AvaloniaProperty.RegisterAttached<VBProps, Control, bool>("AddedAtRunTime");
    public static bool GetAddedAtRunTime(AvaloniaObject element) => element.GetValue(AddedAtRunTimeProperty);
    public static void SetAddedAtRunTime(AvaloniaObject element, bool value) => element.SetValue(AddedAtRunTimeProperty, value);

    // A control Controls.Remove has taken out, which a variable can still hold: every use of it is 735 (measured, #1556).
    public static readonly AttachedProperty<bool> RemovedProperty =
        AvaloniaProperty.RegisterAttached<VBProps, Control, bool>("Removed");
    public static bool GetRemoved(AvaloniaObject element) => element.GetValue(RemovedProperty);
    public static void SetRemoved(AvaloniaObject element, bool value) => element.SetValue(RemovedProperty, value);

    /// <summary>
    /// A removed control, which is no longer usable: 735 for every property read or written and every method (measured,
    /// #1556). <c>TypeName</c> still names its class.
    /// </summary>
    public static void RefuseIfRemoved(Control control)
    {
        if (GetRemoved(control))
            throw new VBRunTimeException(VBStandardError.ControlRemoved);
    }

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