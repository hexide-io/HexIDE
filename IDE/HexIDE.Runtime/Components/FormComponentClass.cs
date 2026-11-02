using Avalonia;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

public class FormComponentClass : ComponentBaseClass
{
    public FormComponentClass() : base([CaptionProperty,
    BackColorProperty,
    // A form or a UserControl saved with Enabled = False runs disabled (#1101). Undeclared, the saved line was dropped.
    EnabledProperty,
    ForeColorProperty,
    IconProperty,
    ShowInTaskbarProperty,
    FontProperty,
    StartUpPositionProperty,
    KeyPreviewProperty,
    MaxButtonProperty,
    MinButtonProperty], [.. RootEvents, new EventClass("Load"), new EventClass("Resize")])
    {
    }

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
    public virtual string DesignerDoubleClickEvent => "Load";

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
    public VBFormRuntime InstantiateWindow(ComponentInstance instance, bool menuBar = false)
    {
        var form = new VBFormRuntime()
        {
            Title = instance.GetPropertyOrDefault(CaptionProperty),
            Width = instance.GetPropertyOrDefault(WidthProperty),
            Height = instance.GetPropertyOrDefault(HeightProperty) + (menuBar ? VBLoader.MenuBarHeight : 0),
            [AttachedProperties.BackColorProperty] = instance.GetPropertyOrDefault(BackColorProperty),
            [AttachedProperties.ForeColorProperty] = instance.GetPropertyOrDefault(ForeColorProperty),
            [AttachedProperties.FontProperty] = instance.GetPropertyOrDefault(FontProperty),
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
        var chrome = AvaloniaInterop.FormChrome.Read(instance, menuBar);
        AvaloniaInterop.FormFrame.Set(form, chrome, menuBar);
        AvaloniaInterop.FormPosition.FromClient(form, instance.GetPropertyOrDefault(LeftProperty),
            instance.GetPropertyOrDefault(TopProperty), chrome, menuBar);
        chrome.Apply(form);
        // And the icon it carries: its own, none, or the default (#1594).
        AvaloniaInterop.FormIcon.Read(instance).Apply(form);
        // The state the form's file gives it, which it shows in: `WindowState = 2 'Maximized` (#1152, measured). It is kept
        // as the line it was read with, so the designer and the file are unchanged.
        if (VBLoader.RawNumber(instance, "WindowState") is { } state
            && Interpreter.FormMembers.AvaloniaWindowState(state) is { } windowState)
            form.WindowState = windowState;
        return form;
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