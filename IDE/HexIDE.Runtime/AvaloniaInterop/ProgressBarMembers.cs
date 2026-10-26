using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>
/// A ProgressBar's members as a program reads, writes and calls them (#1082), by name, ahead of the shared bindings: true
/// for a member it answers, false for one a ProgressBar doesn't have, null for one every control has.
/// </summary>
/// <remarks>
/// Measured on vb6.exe, native and p-code agreeing, for both libraries (the oracle's "In-box controls: the ProgressBar"):
/// <list type="bullet">
/// <item>Value, Min and Max read as Singles. Value outside Min to Max is 380 and leaves it. Min below 0, or not below Max,
/// is 380, and so is Max not above Min. A Min or Max that passes the Value moves it to them.</item>
/// <item>Orientation, Scrolling, Appearance, BorderStyle and OLEDropMode read as Longs and take 0 or 1, and MousePointer
/// 0 to 15 or 99, after rounding half to even; anything else is 380. Past a Long is 6.</item>
/// <item>Null is 13 for every one of them, Enabled included, where an intrinsic's numeric property is 94. Garbage is 13.</item>
/// <item>A 5.0 ProgressBar has no Orientation, Scrolling or Refresh: 438 late-bound. A member it lacks is refused as VB6
/// refuses one early-bound, "Method or data member not found", which is how a missing member of any control is.</item>
/// <item>The extender gives it no TabStop, HelpContextID or CausesValidation, and the control has no Font, BackColor,
/// ForeColor, Text or SetFocus. Negotiate can't be read at run time (393).</item>
/// </list>
/// </remarks>
internal static class ProgressBarMembers
{
    // The extender's members every control has, which the shared bindings and members answer.
    private static readonly HashSet<string> Shared = new(StringComparer.OrdinalIgnoreCase)
    {
        "Name", "Index", "Left", "Top", "Width", "Height", "Visible", "Tag", "ToolTipText", "TabIndex", "Container", "Parent",
        "Object", "Move", "ZOrder", "Drag", "ShowWhatsThis",
    };

    // The extender's members a ProgressBar has in VB6 that HexIDE doesn't implement yet.
    private static readonly HashSet<string> ExtenderNotImplemented = new(StringComparer.OrdinalIgnoreCase)
    {
        "Align", "DragIcon", "DragMode", "WhatsThisHelpID",
    };

    /// <summary>A member read.</summary>
    public static bool? TryGet(Avalonia.Controls.Control control, string name, out Vb6Value value)
    {
        value = default;
        if (control is not VBProgressBar bar)
            return null;
        if (Shared.Contains(name))
            return null;
        if (name.Equals("Negotiate", StringComparison.OrdinalIgnoreCase))
            throw new VBRunTimeException(VBStandardError.PropertyGetCantBeExecutedAtRunTime);
        RefuseUnimplemented(bar, name);
        var member = Own(bar, name);
        if (member is null)
            return false;
        value = member.Name switch
        {
            "Value" => new Vb6Value(bar.VbValue),
            "Min" => new Vb6Value(bar.VbMin),
            "Max" => new Vb6Value(bar.VbMax),
            "Orientation" => new Vb6Value((long)bar.Orientation),
            "Scrolling" => new Vb6Value((long)bar.Scrolling),
            "Appearance" => new Vb6Value((long)bar.Appearance),
            "BorderStyle" => new Vb6Value((long)bar.BorderStyle),
            "OLEDropMode" => new Vb6Value((long)bar.OLEDropMode),
            "MousePointer" => new Vb6Value((long)AttachedProperties.GetMousePointer(bar)),
            "Enabled" => new Vb6Value(bar.IsEnabled),
            _ => throw new NotImplementedException($"Reading a ProgressBar's {member.Name} is not implemented"),
        };
        return true;
    }

    /// <summary>A member written, raising VB6's error for a value it refuses before anything changes.</summary>
    public static bool? TrySet(Avalonia.Controls.Control control, string name, Vb6Value value)
    {
        if (control is not VBProgressBar bar)
            return null;
        if (Shared.Contains(name))
            return null;
        if (name.Equals("Negotiate", StringComparison.OrdinalIgnoreCase))
            throw new NotImplementedException("Setting a ProgressBar's Negotiate is not implemented");
        RefuseUnimplemented(bar, name);
        var member = Own(bar, name);
        if (member is null)
            return false;
        switch (member.Name)
        {
            case "Value":
            {
                var number = Single(value);
                bar.VbValue = number >= bar.VbMin && number <= bar.VbMax ? number : throw InvalidValue();
                break;
            }
            case "Min":
            {
                var number = Single(value);
                if (number < 0 || number >= bar.VbMax)
                    throw InvalidValue();
                bar.VbMin = number;
                if (bar.VbValue < number)
                    bar.VbValue = number;
                break;
            }
            case "Max":
            {
                var number = Single(value);
                if (number <= bar.VbMin)
                    throw InvalidValue();
                bar.VbMax = number;
                if (bar.VbValue > number)
                    bar.VbValue = number;
                break;
            }
            case "Orientation": bar.Orientation = (VBOrientation)Choice(value, 0, 1); break;
            case "Scrolling": bar.Scrolling = (VBScrolling)Choice(value, 0, 1); break;
            case "Appearance": bar.Appearance = (VBAppearance)Choice(value, 0, 1); break;
            case "BorderStyle": bar.BorderStyle = (VBBorder)Choice(value, 0, 1); break;
            case "OLEDropMode": bar.OLEDropMode = (VBOLEDropMode)Choice(value, 0, 1); break;
            case "MousePointer":
            {
                var pointer = Choice(value, 0, 99);
                AttachedProperties.SetMousePointer(bar, pointer is <= 15 or 99 ? (VBCursorType)pointer : throw InvalidValue());
                break;
            }
            case "Enabled":
                bar.IsEnabled = (bool)VB6BuiltIns.CBool(NotNull(value)).Value!;
                break;
            default:
                throw new NotImplementedException($"Setting a ProgressBar's {member.Name} is not implemented");
        }
        return true;
    }

    /// <summary>A method called.</summary>
    public static bool? TryCall(Avalonia.Controls.Control control, string name)
    {
        if (control is not VBProgressBar bar)
            return null;
        if (Shared.Contains(name))
            return null;
        RefuseUnimplemented(bar, name);
        var member = Own(bar, name);
        if (member is not { Kind: MemberKind.Method })
            return false;
        // Refresh repaints the bar, which keeps its Value (measured).
        bar.InvalidateVisual();
        return true;
    }

    // One of the control's own members, as this control's library declares it: null for one it lacks, 5.0's Orientation
    // among them.
    private static InBoxMember? Own(VBProgressBar bar, string name) =>
        ProgressBarComponentClass.Instance.Member(name) is { } member && member.IsIn(bar.Library) && member.Kind != MemberKind.Event
            ? member : null;

    // A member VB6's ProgressBar has that HexIDE doesn't: refused as not implemented, not as a member it lacks.
    private static void RefuseUnimplemented(VBProgressBar bar, string name)
    {
        if (ExtenderNotImplemented.Contains(name)
            || ProgressBarComponentClass.Instance.Member(name) is { Implemented: false } member && member.IsIn(bar.Library))
            throw new NotImplementedException($"The ProgressBar's {name} is not implemented");
    }

    private static Vb6Value NotNull(Vb6Value value) =>
        value.IsNull ? throw new VBRunTimeException(VBStandardError.TypeMismatch) : value;

    // As a Single: garbage and Null are 13, past a Single is 6 (measured).
    private static float Single(Vb6Value value) =>
        System.Convert.ToSingle(VbNumeric.CoerceOnStore(NotNull(value), Vb6Value.ValueType.Single, null).Value);

    // As a Long, then one of least to most: 1.5 is 2, which Orientation refuses (measured).
    private static int Choice(Vb6Value value, int least, int most)
    {
        var number = System.Convert.ToInt64(VbNumeric.CoerceOnStore(NotNull(value), Vb6Value.ValueType.Long, null).Value);
        return number >= least && number <= most ? (int)number : throw InvalidValue();
    }

    private static VBRunTimeException InvalidValue() => new(VBStandardError.InvalidPropertyValue);
}
