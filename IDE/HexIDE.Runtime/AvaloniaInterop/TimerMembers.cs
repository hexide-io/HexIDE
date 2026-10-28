using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

// Measured on vb6.exe SP6, native and p-code: "What a Timer has at run time" (#801).
internal static class TimerMembers
{
    private static readonly HashSet<string> Properties = new(StringComparer.OrdinalIgnoreCase)
    {
        "Enabled", "Index", "Interval", "Name", "Parent", "Tag", "Left", "Top",
    };

    private static readonly HashSet<string> Absent = new(StringComparer.OrdinalIgnoreCase)
    {
        "Width", "Height", "Visible", "MousePointer", "RightToLeft", "Font", "BackColor",
        "Container", "hWnd", "ToolTipText", "TabIndex", "Move",
    };

    public static bool HasProperty(string name) => Properties.Contains(name);

    private static void Check(string name)
    {
        if (Absent.Contains(name))
            throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        if (!Properties.Contains(name))
            throw new NotImplementedException($"A Timer's {name} is not measured (#801)");
    }

    public static bool TryGet(Control control, string name, out Vb6Value value)
    {
        // Preserve the existing extender compile-error refusals (#1974, #1403, #801 review).
        if (name.Equals("TabIndex", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ToolTipText", StringComparison.OrdinalIgnoreCase))
            throw new VBMethodOrDataMemberNotFoundException(name, Vb6Value.ValueType.Control);
        Check(name);
        value = default;
        if (!name.Equals("Parent", StringComparison.OrdinalIgnoreCase))
            return false;
        var form = control.GetLogicalAncestors().OfType<IFormView>().FirstOrDefault()?.Context.FormInstance;
        value = form is not null ? Vb6Value.NewObject(form)
            : throw new NotImplementedException("A Timer's Parent outside a running form is not implemented (#801)");
        return true;
    }

    public static void CheckWrite(string name)
    {
        Check(name);
        if (name.Equals("Left", StringComparison.OrdinalIgnoreCase) || name.Equals("Top", StringComparison.OrdinalIgnoreCase))
            throw IdentityMembers.ReadOnlyAtRunTime(name);
        if (name.Equals("Parent", StringComparison.OrdinalIgnoreCase))
            throw new NotImplementedException("Assigning a Timer's Parent is not measured (#801)");
    }

    public static void RefuseMethod(string name)
    {
        Check(name);
        throw new NotImplementedException($"Calling a Timer's {name} is not measured (#801)");
    }
}
