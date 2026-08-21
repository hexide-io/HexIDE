using System.Collections.Generic;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Runtime.Components;

public static class VBProperties
{
    public static Dictionary<string, List<PropertyClass>> PropertiesByName { get; } = new();

    public static PropertyClass<string> NameProperty = new PropertyClass<string>("(Name)", "The identifier used to refer to this object in code.", PropertyCategory.Misc);

    public static PropertyClass<double> LeftProperty = new PropertyClass<double>("Left", "The distance from the inside-left of the container to the control's left edge.", PropertyCategory.Position, 0);

    public static PropertyClass<double> TopProperty = new PropertyClass<double>("Top", "The distance from the inside-top of the container to the control's top edge.", PropertyCategory.Position, 0);

    public static PropertyClass<double> WidthProperty = new PropertyClass<double>("Width", "The width of the object.", PropertyCategory.Position, 0);

    public static PropertyClass<double> HeightProperty = new PropertyClass<double>("Height", "The height of the object.", PropertyCategory.Position, 0);

    public static PropertyClass<string> CaptionProperty = new PropertyClass<string>("Caption", "The text shown in the object's title bar, or beside its icon.", PropertyCategory.Appearance, "");

    public static PropertyClass<object?> TagProperty = new PropertyClass<object?>("Tag", "A free-form value for attaching your own data to the object.", PropertyCategory.Misc);

    public static PropertyClass<bool> VisibleProperty = new PropertyClass<bool>("Visible", "Whether the object is shown or hidden at run time.", PropertyCategory.Behavior, true);

    public static PropertyClass<bool> EnabledProperty = new PropertyClass<bool>("Enabled", "Whether the object can respond to user input.", PropertyCategory.Behavior, true);

    public static PropertyClass<bool> CausesValidationProperty = new PropertyClass<bool>("CausesValidation", "Whether moving focus off this control validates the control that lost focus.", PropertyCategory.Behavior, true);

    public static PropertyClass<VBCursorType> MousePointerProperty = new PropertyClass<VBCursorType>("MousePointer", "The pointer shape shown while the mouse is over the object.", PropertyCategory.Misc, VBCursorType.Arrow);

    public static PropertyClass<bool> RightToLeftProperty = new PropertyClass<bool>("RightToLeft", "Whether text and layout are mirrored for right-to-left languages.", PropertyCategory.Behavior);

    public static PropertyClass<string?> ToolTipTextProperty = new PropertyClass<string?>("ToolTipText", "The tooltip shown when the pointer rests on the control.", PropertyCategory.Misc);

    public static PropertyClass<int> WhatsThisHelpIdProperty = new PropertyClass<int>("WhatsThisHelpID", "A context number that links the object to a help topic.", PropertyCategory.Misc);

    public static PropertyClass<VBFont> FontProperty = new PropertyClass<VBFont>("Font", "The font used for the object's text.", PropertyCategory.Appearance, VBFont.Default);

    public static PropertyClass<VBTextAlignment> AlignmentProperty = new PropertyClass<VBTextAlignment>("Alignment", "How the control's text is aligned (or a CheckBox or OptionButton's caption).", PropertyCategory.Misc);

    public static PropertyClass<VBAppearance> AppearanceProperty = new PropertyClass<VBAppearance>("Appearance", "Whether the object is drawn flat or with a 3-D effect.", PropertyCategory.Appearance, VBAppearance._3D);

    public static PropertyClass<bool> AutoSizeProperty = new PropertyClass<bool>("AutoSize", "Whether the control resizes itself to fit its contents.", PropertyCategory.Position, false);

    public static PropertyClass<VBBorder> BorderStyleProperty = new PropertyClass<VBBorder>("BorderStyle", "The style of border drawn around the object.", PropertyCategory.Appearance, VBBorder.None);

    public static PropertyClass<VBColor> ForeColorProperty = new PropertyClass<VBColor>("ForeColor", "The color used for the object's text and graphics.", PropertyCategory.Appearance, VBColor.FromSystemColor(VbSystemColor.Btntext));

    public static PropertyClass<bool> UseMnemonicProperty = new PropertyClass<bool>("UseMnemonic", "Whether an ampersand (&) in the caption marks the next character as an access key.", PropertyCategory.Misc, true);

    public static PropertyClass<bool> WordWrapProperty = new PropertyClass<bool>("WordWrap", "Whether the control grows to fit multi-line caption text.", PropertyCategory.Misc);

    public static readonly PropertyClass<VBAlign> AlignProperty = new PropertyClass<VBAlign>("Align",
        "Which edge of the form the control is docked to.", PropertyCategory.Position);

    public static readonly PropertyClass<bool> AutoRedrawProperty = new PropertyClass<bool>("AutoRedraw",
        "Whether drawing is saved to an off-screen bitmap and repainted automatically.", PropertyCategory.Behavior);

    public static readonly PropertyClass<byte[]?> PictureProperty = new PropertyClass<byte[]?>("Picture",
        "An image displayed on the control.", PropertyCategory.Appearance);

    public static readonly PropertyClass<byte[]?> IconProperty = new PropertyClass<byte[]?>("Icon",
        "The icon shown when the form is minimized.", PropertyCategory.Appearance);

    public static readonly PropertyClass<byte[]?> MouseIconProperty = new PropertyClass<byte[]?>("MouseIcon",
        "A custom image used for the mouse pointer.", PropertyCategory.Behavior);

    public static readonly PropertyClass<byte[]?> DragIconProperty = new PropertyClass<byte[]?>("DragIcon",
        "The image shown while the control is being dragged.", PropertyCategory.Behavior);

    /// <summary>
    /// False sizes the control to its picture; true scales the picture to the control. VB6's default is
    /// False, which is why an un-stretched Image is exactly as big as what it shows.
    /// </summary>
    public static readonly PropertyClass<bool> StretchProperty = new PropertyClass<bool>("Stretch",
        "Whether the picture is scaled to fit the control, rather than the control sized to the picture.",
        PropertyCategory.Appearance);

    public static readonly PropertyClass<byte[]?> OleObjectBlobProperty = new PropertyClass<byte[]?>("OleObjectBlob",
        "Binary data for an embedded OLE object.", PropertyCategory.Misc);

    /// <summary>
    /// A ListBox or ComboBox's per-item Long values, held as the companion record cites them.
    ///
    /// A blob rather than a modelled list because the record framing is a two-byte count and no VB6-shipped
    /// file has a non-empty one to learn the rest from. Kept verbatim so it survives a save; understanding
    /// what is inside can wait for an example to check against.
    /// </summary>
    public static readonly PropertyClass<byte[]?> ItemDataProperty = new PropertyClass<byte[]?>("ItemData",
        "Per-item numeric values stored alongside a list control's items.", PropertyCategory.Misc);

    public static PropertyClass<int> IntervalProperty = new PropertyClass<int>("Interval", "The delay, in milliseconds, between a Timer control's events.", PropertyCategory.Misc, defaultValue: 1000);

    public static readonly PropertyClass<string> TextProperty = new PropertyClass<string>("Text", "The text held by the control.", PropertyCategory.Misc, "");


    public static PropertyClass<VBColor> BackColorProperty = new PropertyClass<VBColor>("BackColor", "The background color used for the object's text and graphics.", PropertyCategory.Appearance, VBColor.FromSystemColor(VbSystemColor.Btnface));

    public static PropertyClass<BackStyles> BackStyleProperty = new PropertyClass<BackStyles>("BackStyle", "Whether a Label or Shape background is opaque or transparent.", PropertyCategory.Appearance);

    public static PropertyClass<VBColor> BorderColorProperty = new PropertyClass<VBColor>("BorderColor", "The color of the object's border.", PropertyCategory.Appearance, VBColor.Black);

    public static PropertyClass<BorderStyles> ShapeBorderStyleProperty = new PropertyClass<BorderStyles>("BorderStyle", "The style of border drawn around the object.", PropertyCategory.Appearance, BorderStyles.Solid);

    public static PropertyClass<VBColor> FillColorProperty = new PropertyClass<VBColor>("FillColor", "The color used to fill shapes such as circles and boxes.", PropertyCategory.Appearance, VBColor.Black);

    public static PropertyClass<FillStyles> FillStyleProperty = new PropertyClass<FillStyles>("FillStyle", "The pattern used to fill a shape.", PropertyCategory.Appearance, FillStyles.Transparent);

    public static PropertyClass<double> BorderWidthProperty = new PropertyClass<double>("BorderWidth", "The thickness of the control's border.", PropertyCategory.Appearance, 1);

    public static PropertyClass<ShapeTypes> ShapeProperty = new PropertyClass<ShapeTypes>("Shape", "The geometric form drawn by a Shape control.", PropertyCategory.Appearance, ShapeTypes.Rectangle);

    public static PropertyClass<List<string>?> ListProperty = new PropertyClass<List<string>?>("List", "The set of items shown in the control's list.", PropertyCategory.List);

    public static PropertyClass<int> ListIndexProperty = new PropertyClass<int>("ListIndex", "The index of the selected item, or -1 when nothing is selected.", PropertyCategory.List, -1);

    public static PropertyClass<int> LargeChangeProperty = new PropertyClass<int>("LargeChange", "How much Value changes when the user clicks the scroll bar track.", PropertyCategory.Behavior, 1);

    public static PropertyClass<int> SmallChangeProperty = new PropertyClass<int>("SmallChange", "How much Value changes when the user clicks a scroll arrow.", PropertyCategory.Behavior, 1);

    public static PropertyClass<int> MinProperty = new PropertyClass<int>("Min", "The lowest value the scroll bar can represent.", PropertyCategory.Behavior, 0);

    public static PropertyClass<int> MaxProperty = new PropertyClass<int>("Max", "The highest value the scroll bar can represent.", PropertyCategory.Behavior, 32767);

    public static PropertyClass<int> ValueProperty = new PropertyClass<int>("Value", "The current value of the object.", 0);

    public static PropertyClass<bool> CheckedProperty = new PropertyClass<bool>("Checked", "Whether a check mark appears beside the menu item.", PropertyCategory.Appearance);

    public static PropertyClass<bool> WindowListProperty = new PropertyClass<bool>("WindowList", "Whether this menu lists the open MDI child windows.", PropertyCategory.Misc);

    public static PropertyClass<VBCheckValue> CheckValueProperty = new PropertyClass<VBCheckValue>("Value", "The current value of the object.", PropertyCategory.Misc);

    public static PropertyClass<bool> LockedProperty = new PropertyClass<bool>("Locked", "Whether the control's contents can be edited.", PropertyCategory.Behavior, false);

    public static PropertyClass<VBStartupPosition> StartUpPositionProperty = new PropertyClass<VBStartupPosition>("StartUpPosition", "Where the form is placed on screen when it first opens.", PropertyCategory.Position, VBStartupPosition.StartUpWindowsDefault);

    public static PropertyClass<bool> ShowInTaskbarProperty = new PropertyClass<bool>("ShowInTaskbar", "Whether the form appears in the taskbar.", PropertyCategory.Misc, true);

    public static PropertyClass<int> TabIndexProperty = new PropertyClass<int>("TabIndex", "The control's position in the form's tab order.", PropertyCategory.Behavior);

    public static PropertyClass<bool> TabStopProperty = new PropertyClass<bool>("TabStop", "Whether the user can reach the control with the Tab key.", PropertyCategory.Behavior, true);

    public static PropertyClass<bool> KeyPreviewProperty = new PropertyClass<bool>("KeyPreview", "Whether the form receives keyboard events before its controls do.", PropertyCategory.Behavior, false);

    public static PropertyClass<bool> MaxButtonProperty = new PropertyClass<bool>("MaxButton", "Whether the form's title bar shows a Maximize button.", PropertyCategory.Appearance, true);

    public static PropertyClass<bool> MinButtonProperty = new PropertyClass<bool>("MinButton", "Whether the form's title bar shows a Minimize button.", PropertyCategory.Appearance, true);
}
