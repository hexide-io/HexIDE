using static HexIDE.Runtime.Components.ComponentBaseClass;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

/// <summary>
/// The events of VB6's two designer roots that aren't a form's, as its code window's Procedure box lists them and the
/// stubs it writes for them (measured on VB6 SP6, #1632). Those a form or a control has too are the shared ones.
/// </summary>
internal static class DesignerRootEvents
{
    private static readonly EventClass AccessKeyPressEvent = new EventClass("AccessKeyPress", new EventClassArgument("KeyAscii", "Integer"));
    private static readonly EventClass AmbientChangedEvent = new EventClass("AmbientChanged", new EventClassArgument("PropertyName", "String"));
    private static readonly EventClass AsyncReadCompleteEvent = new EventClass("AsyncReadComplete", new EventClassArgument("AsyncProp", "AsyncProperty"));
    private static readonly EventClass AsyncReadProgressEvent = new EventClass("AsyncReadProgress", new EventClassArgument("AsyncProp", "AsyncProperty"));
    private static readonly EventClass DragDropEvent = new EventClass("DragDrop", new EventClassArgument("Source", "Control"), new EventClassArgument("X", "Single"), new EventClassArgument("Y", "Single"));
    private static readonly EventClass DragOverEvent = new EventClass("DragOver", new EventClassArgument("Source", "Control"), new EventClassArgument("X", "Single"), new EventClassArgument("Y", "Single"), new EventClassArgument("State", "Integer"));
    private static readonly EventClass EnterFocusEvent = new EventClass("EnterFocus");
    private static readonly EventClass ExitFocusEvent = new EventClass("ExitFocus");
    private static readonly EventClass GetDataMemberEvent = new EventClass("GetDataMember", new EventClassArgument("DataMember", "String"), new EventClassArgument("Data", "Object"));
    private static readonly EventClass HideEvent = new EventClass("Hide");
    private static readonly EventClass HitTestEvent = new EventClass("HitTest", new EventClassArgument("X", "Single"), new EventClassArgument("Y", "Single"), new EventClassArgument("HitResult", "Integer"));
    private static readonly EventClass InitializeEvent = new EventClass("Initialize");
    private static readonly EventClass InitPropertiesEvent = new EventClass("InitProperties");
    private static readonly EventClass OLECompleteDragEvent = new EventClass("OLECompleteDrag", new EventClassArgument("Effect", "Long"));
    private static readonly EventClass OLEDragDropEvent = new EventClass("OLEDragDrop", new EventClassArgument("Data", "DataObject"), new EventClassArgument("Effect", "Long"), new EventClassArgument("Button", "Integer"), new EventClassArgument("Shift", "Integer"), new EventClassArgument("X", "Single"), new EventClassArgument("Y", "Single"));
    private static readonly EventClass OLEDragOverEvent = new EventClass("OLEDragOver", new EventClassArgument("Data", "DataObject"), new EventClassArgument("Effect", "Long"), new EventClassArgument("Button", "Integer"), new EventClassArgument("Shift", "Integer"), new EventClassArgument("X", "Single"), new EventClassArgument("Y", "Single"), new EventClassArgument("State", "Integer"));
    private static readonly EventClass OLEGiveFeedbackEvent = new EventClass("OLEGiveFeedback", new EventClassArgument("Effect", "Long"), new EventClassArgument("DefaultCursors", "Boolean"));
    private static readonly EventClass OLESetDataEvent = new EventClass("OLESetData", new EventClassArgument("Data", "DataObject"), new EventClassArgument("DataFormat", "Integer"));
    private static readonly EventClass OLEStartDragEvent = new EventClass("OLEStartDrag", new EventClassArgument("Data", "DataObject"), new EventClassArgument("AllowedEffects", "Long"));
    private static readonly EventClass PaintEvent = new EventClass("Paint");
    private static readonly EventClass ReadPropertiesEvent = new EventClass("ReadProperties", new EventClassArgument("PropBag", "PropertyBag"));
    private static readonly EventClass ResizeEvent = new EventClass("Resize");
    private static readonly EventClass ShowEvent = new EventClass("Show");
    private static readonly EventClass TerminateEvent = new EventClass("Terminate");
    private static readonly EventClass WritePropertiesEvent = new EventClass("WriteProperties", new EventClassArgument("PropBag", "PropertyBag"));
    private static readonly EventClass ApplyChangesEvent = new EventClass("ApplyChanges");
    private static readonly EventClass EditPropertyEvent = new EventClass("EditProperty", new EventClassArgument("PropertyName", "String"));
    private static readonly EventClass SelectionChangedEvent = new EventClass("SelectionChanged");

    /// <summary>A UserControl's 35, in the Procedure box's order.</summary>
    public static readonly EventClass[] UserControl =
    [
        AccessKeyPressEvent, AmbientChangedEvent, AsyncReadCompleteEvent, AsyncReadProgressEvent, ClickEvent,
        DblClickEvent, DragDropEvent, DragOverEvent, EnterFocusEvent, ExitFocusEvent, GetDataMemberEvent,
        GotFocusEvent, HideEvent, HitTestEvent, InitializeEvent, InitPropertiesEvent, KeyDownEvent, KeyPressEvent,
        KeyUpEvent, LostFocusEvent, MouseDownEvent, MouseMoveEvent, MouseUpEvent, OLECompleteDragEvent,
        OLEDragDropEvent, OLEDragOverEvent, OLEGiveFeedbackEvent, OLESetDataEvent, OLEStartDragEvent, PaintEvent,
        ReadPropertiesEvent, ResizeEvent, ShowEvent, TerminateEvent, WritePropertiesEvent
    ];

    /// <summary>A PropertyPage's 24, in the Procedure box's order.</summary>
    public static readonly EventClass[] PropertyPage =
    [
        ApplyChangesEvent, ClickEvent, DblClickEvent, DragDropEvent, DragOverEvent, EditPropertyEvent, GotFocusEvent,
        InitializeEvent, KeyDownEvent, KeyPressEvent, KeyUpEvent, LostFocusEvent, MouseDownEvent, MouseMoveEvent,
        MouseUpEvent, OLECompleteDragEvent, OLEDragDropEvent, OLEDragOverEvent, OLEGiveFeedbackEvent, OLESetDataEvent,
        OLEStartDragEvent, PaintEvent, SelectionChangedEvent, TerminateEvent
    ];
}

/// <summary>
/// The root of a UserControl's designer. It was a form's component, so its Properties window listed a form's properties,
/// Caption, Icon and the window's buttons among them, which VB6's UserControl has none of (#1585). Its list is the
/// properties HexIDE models of the 51 VB6's Properties window gives a new UserControl (measured): the rest of those, and
/// any line a loaded file holds that isn't listed here, are kept as the file's own text.
/// </summary>
public sealed class UserControlRootClass : FormComponentClass
{
    private UserControlRootClass() : base([BackColorProperty,
        // A UserControl saved with Enabled = False is disabled where it is placed (#1101).
        EnabledProperty,
        FontProperty,
        ForeColorProperty,
        HeightProperty,
        KeyPreviewProperty,
        WidthProperty], DesignerRootEvents.UserControl)
    {
    }

    /// <summary>A double click on its background writes UserControl_Initialize (measured, #1632).</summary>
    public override string DesignerDoubleClickEvent => "Initialize";

    public override string Name => "UserControl";
    public override string VBTypeName => "VB.UserControl";

    public static new UserControlRootClass Instance { get; } = new();
}

/// <summary>
/// The root of a PropertyPage's designer: the properties HexIDE models of the 33 VB6's Properties window gives a new
/// PropertyPage (measured, #1585). It has a Caption, the page's tab, and no Enabled, Icon or window buttons.
/// </summary>
public sealed class PropertyPageRootClass : FormComponentClass
{
    private PropertyPageRootClass() : base([BackColorProperty,
        CaptionProperty,
        FontProperty,
        ForeColorProperty,
        HeightProperty,
        HelpContextIDProperty,
        KeyPreviewProperty,
        WidthProperty], DesignerRootEvents.PropertyPage)
    {
    }

    /// <summary>A double click on its background writes PropertyPage_SelectionChanged (measured, #1632).</summary>
    public override string DesignerDoubleClickEvent => "SelectionChanged";

    public override string Name => "PropertyPage";
    public override string VBTypeName => "VB.PropertyPage";

    public static new PropertyPageRootClass Instance { get; } = new();
}
