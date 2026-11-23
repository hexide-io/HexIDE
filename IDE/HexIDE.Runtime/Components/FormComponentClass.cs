using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

public class FormComponentClass : ComponentBaseClass
{
    public FormComponentClass() : base([CaptionProperty,
    FormBorderStyleProperty,
    BackColorProperty,
    // A form or a UserControl saved with Enabled = False runs disabled (#1101). Undeclared, the saved line was dropped.
    EnabledProperty,
    ForeColorProperty,
    IconProperty,
    MousePointerProperty,
    ShowInTaskbarProperty,
    FontProperty,
    StartUpPositionProperty,
    KeyPreviewProperty,
    MaxButtonProperty,
    MinButtonProperty], [.. RootEvents, new EventClass("Load"), new EventClass("Resize"), AvaloniaInterop.RuntimePaint.PaintEvent])
    {
        Members = CreateMembers();
    }

    // VB6 SP6, #5293: docs/vb6-fidelity-oracle/the-object-browsers-library-list.md.
    // Only the Form constructor fills this: UserControl and PropertyPage were not measured.
    public override IReadOnlyList<InBox.InBoxMember> Members { get; } = [];

    // Implemented records today's FormMembers, drawing/scale, Avalonia bindings and event dispatch.
    // No runtime path reads this flag for Form; the table supplies the Object Browser only.
    private static IReadOnlyList<InBox.InBoxMember> CreateMembers() =>
    [
        new("Activate", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, true),
        new("ActiveControl", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("Appearance", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, false),
        new("AutoRedraw", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("BackColor", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, true, true),
        new("BorderStyle", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, false),
        new("Caption", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, true, true),
        new("Circle", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, true),
        new("Click", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, true),
        new("ClipControls", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, false),
        new("Cls", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, true),
        new("ControlBox", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, false),
        new("Controls", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("Count", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, false),
        new("CurrentX", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("CurrentY", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("DblClick", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, true),
        new("Deactivate", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, true),
        new("DragDrop", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, false),
        new("DragOver", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, false),
        new("DrawMode", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("DrawStyle", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("DrawWidth", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("Enabled", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, true, true),
        new("FillColor", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("FillStyle", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("Font", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, true, true),
        new("FontBold", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("FontItalic", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("FontName", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("FontSize", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("FontStrikethru", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("FontTransparent", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("FontUnderline", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("ForeColor", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, true, true),
        new("GotFocus", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, true),
        new("HasDC", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, false),
        new("hDC", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("Height", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, true, true),
        new("HelpContextID", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, false),
        new("Hide", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, true),
        new("hWnd", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, false),
        new("Icon", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, true, true),
        new("Image", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, false),
        new("Initialize", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, true),
        new("KeyDown", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, true),
        new("KeyPress", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, true),
        new("KeyPreview", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, true, true),
        new("KeyUp", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, true),
        new("Left", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, true, true),
        new("Line", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, true),
        new("LinkClose", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, false),
        new("LinkError", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, false),
        new("LinkExecute", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, false),
        new("LinkMode", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, false),
        new("LinkOpen", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, false),
        new("LinkTopic", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, false),
        new("Load", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, true),
        new("LostFocus", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, true),
        new("MaxButton", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, true, false),
        new("MDIChild", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, false),
        new("MinButton", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, true, false),
        new("MouseDown", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, true),
        new("MouseIcon", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, false),
        new("MouseMove", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, true),
        new("MousePointer", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, true, true),
        new("MouseUp", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, true),
        new("Move", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, true),
        new("Moveable", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, false),
        new("Name", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("OLECompleteDrag", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, false),
        new("OLEDrag", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, false),
        new("OLEDragDrop", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, false),
        new("OLEDragOver", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, false),
        new("OLEDropMode", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, false),
        new("OLEGiveFeedback", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, false),
        new("OLESetData", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, false),
        new("OLEStartDrag", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, false),
        new("Paint", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, true),
        new("PaintPicture", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, false),
        new("Palette", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, false),
        new("PaletteMode", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, false),
        new("Picture", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, false),
        new("Point", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, true),
        new("PopupMenu", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, false),
        new("PrintForm", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, false),
        new("PSet", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, true),
        new("QueryUnload", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, true),
        new("Refresh", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, true),
        new("Resize", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, true),
        new("RightToLeft", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("Scale", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, true),
        new("ScaleHeight", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("ScaleLeft", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("ScaleMode", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("ScaleTop", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("ScaleWidth", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("ScaleX", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, true),
        new("ScaleY", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, true),
        new("SetFocus", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, true),
        new("Show", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, true),
        new("ShowInTaskbar", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, true, false),
        new("StartUpPosition", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, true, false),
        new("Tag", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, true, true),
        new("Terminate", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, true),
        new("TextHeight", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, false),
        new("TextWidth", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, false),
        new("Top", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, true, true),
        new("Unload", InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, true),
        new("ValidateControls", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, false),
        new("Visible", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, true, true),
        new("WhatsThisButton", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, false),
        new("WhatsThisHelp", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, false),
        new("WhatsThisMode", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, false),
        new("Width", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, true, true),
        new("WindowState", InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true),
        new("ZOrder", InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, false),
    ];

    /// <summary>
    /// The root of a designer that is not a form's: a UserControl's or a PropertyPage's. Its list is the given properties,
    /// with (Name) and Tag. VB6's Properties window gives neither Left, Top nor Visible (measured, #1585), so they aren't
    /// in it; the root's geometry is the file's ClientLeft, ClientTop, ClientWidth and ClientHeight, as a form's is.
    /// </summary>
    protected FormComponentClass(IReadOnlyList<PropertyClass> properties, IReadOnlyList<EventClass> events)
        : base(properties, events, hasBounds: false, hasVisible: false)
    {
    }

    /// <summary>
    /// The event whose procedure the designer writes when its background is double-clicked: a form's Load (#1532), a
    /// UserControl's Initialize and a PropertyPage's SelectionChanged (measured, #1632).
    /// </summary>
    public override string DesignerDoubleClickEvent => "Load";

    private static readonly EventClass[] RootEvents = [KeyDownEvent, KeyPressEvent, KeyUpEvent, ClickEvent, DblClickEvent, MouseDownEvent, MouseMoveEvent, MouseUpEvent];

    public override string Name => "Form";
    public override string VBTypeName => "VB.Form";

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        return new Control()
        {
        };
    }

    /// <param name="menuBar">
    /// Whether the form has a menu bar, which sits inside the window here and outside the client area in VB6, so the
    /// window is that much taller than the form's ClientHeight (#1127).
    /// </param>
    public VBFormRuntime InstantiateWindow(ComponentInstance instance, bool menuBar = false) =>
        InstantiateWindow(instance, menuBar, menuBar);

    internal VBFormRuntime InstantiateWindow(ComponentInstance instance, bool menuBar, bool menus)
    {
        var form = new VBFormRuntime()
        {
            Title = instance.GetPropertyOrDefault(CaptionProperty),
            Width = instance.GetPropertyOrDefault(WidthProperty),
            Height = instance.GetPropertyOrDefault(HeightProperty) + (menuBar ? VBLoader.MenuBarHeight : 0),
            [AttachedProperties.BackColorProperty] = instance.GetPropertyOrDefault(BackColorProperty),
            [AttachedProperties.ForeColorProperty] = instance.GetPropertyOrDefault(ForeColorProperty),
            [AttachedProperties.FontProperty] = instance.GetPropertyOrDefault(FontProperty),
            [AttachedProperties.MousePointerProperty] = instance.GetPropertyOrDefault(MousePointerProperty),
            Cursor = instance.GetPropertyOrDefault(MousePointerProperty).ToCursor(),
            WindowStartupLocation = instance.GetPropertyOrDefault(StartUpPositionProperty).ToAvalonia(),
            CenterOnShow = instance.GetPropertyOrDefault(StartUpPositionProperty) == VBStartupPosition.StartUpScreen,
            ShowInTaskbar = instance.GetPropertyOrDefault(ShowInTaskbarProperty),
            IsEnabled = instance.GetPropertyOrDefault(EnabledProperty),
            Position = new PixelPoint((int)instance.GetPropertyOrDefault(LeftProperty), (int)instance.GetPropertyOrDefault(TopProperty))
        };
        AttachedProperties.SetKeyPreview(form, instance.GetPropertyOrDefault(KeyPreviewProperty));
        VBProps.SetName(form, instance.GetPropertyOrDefault(NameProperty));
        // The frame its Width and Height include at run time (#1072).
        // And the window that frame is drawn as: its border, caption bar and buttons (#1575).
        var chrome = AvaloniaInterop.FormChrome.Read(instance, menus);
        AvaloniaInterop.FormFrame.Set(form, chrome, menuBar);
        AvaloniaInterop.FormPosition.FromClient(form, instance.GetPropertyOrDefault(LeftProperty),
            instance.GetPropertyOrDefault(TopProperty), chrome, menuBar);
        chrome.Apply(form);
        // And the icon it carries: its own, none, or the shared neutral default (#1594, #4187).
        AvaloniaInterop.FormIcon.Read(instance).Apply(form);
        // The state the form's file gives it, which it shows in: `WindowState = 2 'Maximized` (#1152, measured). It is kept
        // as the line it was read with, so the designer and the file are unchanged.
        if (VBLoader.RawNumber(instance, "WindowState") is { } state
            && Interpreter.FormMembers.AvaloniaWindowState(state) is { } windowState)
            form.WindowState = windowState;
        return form;
    }

    // VB6 SP6's designer resets these on a different BorderStyle (#1596, oracle PR #5216).
    public override IReadOnlyList<PropertyChange> ApplyDesignerConsequences(ComponentInstance instance, PropertyClass property)
    {
        if (!ReferenceEquals(property, FormBorderStyleProperty))
            return [];
        var style = instance.GetPropertyOrDefault(FormBorderStyleProperty);
        if (style is < VBFormBorderStyle.None or > VBFormBorderStyle.SizableToolWindow)
            return [];
        var changes = new List<PropertyChange>();
        if (style != VBFormBorderStyle.None)
        {
            Set(MinButtonProperty, style == VBFormBorderStyle.Sizable);
            Set(MaxButtonProperty, style == VBFormBorderStyle.Sizable);
        }
        Set(ShowInTaskbarProperty, style is VBFormBorderStyle.FixedSingle or VBFormBorderStyle.Sizable);
        return changes;

        void Set(PropertyClass<bool> changedProperty, bool value)
        {
            var before = instance.GetPropertyOrDefault(changedProperty);
            if (before == value)
                return;
            instance.SetProperty(changedProperty, value);
            changes.Add(new PropertyChange(changedProperty, before, value));
        }
    }

    public static FormComponentClass Instance { get; } = new();

    /// <summary>The class of the root a designer file names: VB.Form, VB.UserControl or VB.PropertyPage.</summary>
    public static FormComponentClass ForRoot(string rootTypeName) => rootTypeName switch
    {
        "VB.UserControl" => UserControlRootClass.Instance,
        "VB.PropertyPage" => PropertyPageRootClass.Instance,
        _ => Instance,
    };
}