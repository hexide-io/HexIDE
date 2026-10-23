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
    MinButtonProperty], [ClickEvent, DblClickEvent, MouseDownEvent, MouseMoveEvent, MouseUpEvent])
    {
    }

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
            ShowInTaskbar = instance.GetPropertyOrDefault(ShowInTaskbarProperty),
            IsEnabled = instance.GetPropertyOrDefault(EnabledProperty),
            Position = new PixelPoint((int)instance.GetPropertyOrDefault(LeftProperty), (int)instance.GetPropertyOrDefault(TopProperty))
        };
        VBProps.SetName(form, instance.GetPropertyOrDefault(NameProperty));
        // The frame its Width and Height include at run time (#1072).
        // And the window that frame is drawn as: its border, caption bar and buttons (#1575).
        var chrome = AvaloniaInterop.FormChrome.Read(instance, menuBar);
        AvaloniaInterop.FormFrame.Set(form, chrome, menuBar);
        chrome.Apply(form);
        // The state the form's file gives it, which it shows in: `WindowState = 2 'Maximized` (#1152, measured). It is kept
        // as the line it was read with, so the designer and the file are unchanged.
        if (VBLoader.RawNumber(instance, "WindowState") is { } state
            && Interpreter.FormMembers.AvaloniaWindowState(state) is { } windowState)
            form.WindowState = windowState;
        return form;
    }

    public static FormComponentClass Instance { get; } = new();
}