using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

// The placed control's extender, not the UserControl root's own members. #1631, part A;
// vb6.exe SP6, native and p-code: issuecomment-6425849231.
internal static class UserControlMembers
{
    private sealed record Settings(bool CanGetFocus, bool Invisible, bool Windowless, bool ForwardFocus, bool DefaultCancel, bool Container, bool Alignable)
    {
        internal short Align;
        internal bool Default;
        internal bool Cancel;
    }
    private static readonly ConditionalWeakTable<Control, Settings> Controls = new();

    internal static void Register(Control control, ComponentInstance root) => Controls.Add(control,
        new Settings(Setting(root, "CanGetFocus", true), Setting(root, "InvisibleAtRuntime", false),
            Setting(root, "Windowless", false), Setting(root, "ForwardFocus", false), Setting(root, "DefaultCancel", false), Setting(root, "ControlContainer", false), Setting(root, "Alignable", false)));

    internal static bool IsUserControl(Control c) => Controls.TryGetValue(c, out _);
    internal static bool HasTabIndex(Control c) => Controls.TryGetValue(c, out var s) && !s.Invisible && (s.CanGetFocus || s.ForwardFocus);
    internal static bool HasTabStop(Control c) => Controls.TryGetValue(c, out var s) && !s.Invisible && s.CanGetFocus && !s.ForwardFocus;
    internal static bool HasAccessKeys(Control c) => Controls.TryGetValue(c, out var s) && !s.Invisible;
    internal static bool ForwardFocus(Control c) => Controls.TryGetValue(c, out var s) && s.ForwardFocus;
    internal static bool CanGetFocus(Control c) => Controls.TryGetValue(c, out var s) && s.CanGetFocus;
    internal static bool IsContainer(Control c) => Controls.TryGetValue(c, out var s) && s.Container && !s.Invisible;
    internal static bool Windowless(Control c) => Controls.TryGetValue(c, out var s) && s.Windowless;
    internal static bool Invisible(Control c) => Controls.TryGetValue(c, out var s) && s.Invisible;
    internal static bool IsDefault(Control c) => Controls.TryGetValue(c, out var s) && !s.Invisible && s.DefaultCancel && s.Default;
    internal static bool IsCancel(Control c) => Controls.TryGetValue(c, out var s) && !s.Invisible && s.DefaultCancel && s.Cancel;

    internal static void Configure(Control control)
    {
        if (!Controls.TryGetValue(control, out _))
            return;
        if (Invisible(control))
            control.IsVisible = false;
        control.Focusable = HasTabStop(control);
        if (!HasTabStop(control))
            KeyboardNavigation.SetIsTabStop(control, false);
    }

    // These remain carried .ctl lines: modelling them in the designer is group 2.
    internal static bool Setting(ComponentInstance root, string name, bool fallback)
    {
        var property = root.UnknownRawProperties.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (property.Name is null)
            return fallback;
        var line = property.Lines[0];
        var equals = line.IndexOf('=');
        var value = line[(equals + 1)..].Split('\'')[0].Trim();
        return long.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var number) ? number != 0 : fallback;
    }

    // Called before shared property bindings and method dispatch, so an absent member cannot fall through to
    // Canvas geometry, Enabled, or a method every Avalonia control happens to have.
    internal static void Check(Vb6Value reference, string member, bool reading = false)
    {
        if (reference.Value is not Control control || !Controls.TryGetValue(control, out var settings))
            return;
        var name = member.ToLowerInvariant();
        if (reading && settings.Invisible && name is "left" or "top")
            throw new VBRunTimeException(VBStandardError.PropertyGetCantBeExecutedAtRunTime);
        var missing = settings.Invisible
            ? name is not ("name" or "index" or "parent" or "object" or "left" or "top")
            : name == "enabled" || name.StartsWith("data", StringComparison.Ordinal)
                || !HasTabIndex(control) && name == "tabindex"
                || !HasTabStop(control) && name is "tabstop" or "setfocus" or "causesvalidation" or "helpcontextid"
                || !settings.DefaultCancel && name is "default" or "cancel"
                || !settings.Alignable && name is "align" or "negotiate"
                || settings.Windowless && name == "hwnd";
        if (missing)
        {
            if (reference.HeldAsVariant || string.Equals(reference.DeclaredAs, "Object", StringComparison.OrdinalIgnoreCase))
                throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
            throw new VBMethodOrDataMemberNotFoundException(member, reference.Type);
        }
        // The supervisor's #1631 handle decision (2026-10-26): a windowed member exists, but no HWND is invented.
        if (reading && name == "negotiate")
            throw new VBRunTimeException(VBStandardError.PropertyGetCantBeExecutedAtRunTime);
        if (name is "hwnd" or "parent" or "object" or "container" or "causesvalidation" or "helpcontextid")
            throw new NotImplementedException($"The UserControl extender's {member} is not implemented");
    }

    internal static bool TryGet(Control control, string member, out Vb6Value value)
    {
        value = default;
        if (!Controls.TryGetValue(control, out var settings))
            return false;
        switch (member.ToLowerInvariant())
        {
            case "visible": value = new Vb6Value(control.IsVisible && TopLevel.GetTopLevel(control) is Window { IsVisible: true }); return true;
            case "align": value = new Vb6Value(settings.Align); return true;
            case "default": value = new Vb6Value(settings.Default); return true;
            case "cancel": value = new Vb6Value(settings.Cancel); return true;
            case "tabindex": value = new Vb6Value(TabOrder.Get(control)); return true;
            case "tabstop": value = new Vb6Value(TabOrder.GetTabStop(control)); return true;
            default: return false;
        }
    }

    internal static bool TrySet(Control control, string member, Vb6Value value)
    {
        if (!Controls.TryGetValue(control, out var settings))
            return false;
        switch (member.ToLowerInvariant())
        {
            case "align":
                var align = Convert.ToInt16(VB6BuiltIns.NarrowNum(value, Vb6Value.ValueType.Integer).Value);
                if (align != 2)
                    throw new NotImplementedException("Only assignment of Align 2 is measured (#1631)");
                settings.Align = align;
                Align(control);
                return true;
            case "default":
                settings.Default = (bool)VB6BuiltIns.CBool(value).Value!;
                UserControlKeys.UpdateDefault(control);
                return true;
            case "cancel":
                settings.Cancel = (bool)VB6BuiltIns.CBool(value).Value!;
                return true;
            case "tabindex":
                TabOrder.Set(control, System.Convert.ToInt32(VB6BuiltIns.NarrowNum(value, Vb6Value.ValueType.Integer).Value));
                return true;
            case "tabstop":
                TabOrder.SetTabStop(control, (bool)VB6BuiltIns.CBool(value).Value!);
                return true;
            default: return false;
        }
    }

    internal static void Align(Control control)
    {
        if (!Controls.TryGetValue(control, out var settings) || settings.Align != 2 || control.Parent is not Canvas host)
            return;
        var window = TopLevel.GetTopLevel(control) as Window;
        var width = window is null ? host.Bounds.Width : ControlPositions.ClientPixels(window, false);
        var height = window is null ? host.Bounds.Height : ControlPositions.ClientPixels(window, true);
        Canvas.SetLeft(control, 0);
        Canvas.SetTop(control, height - control.Height);
        control.Width = width;
    }
}
