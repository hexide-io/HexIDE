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

    /// <summary>A row of VB6's Locals window under a Timer: its value, or the text of the error its read raises, and the
    /// member's declared type, which the row shows either way.</summary>
    internal sealed record LocalsRow(string Name, Vb6Value Value, string? Error, string DeclaredType);

    // What VB6's Locals window lists under a Timer, in its order: these seven and no others, neither Left and Top nor any
    // member that raises 438. [_Default] is Enabled, the Timer's default member (measured on #2289, the table on #2329).
    private static readonly (string Name, string Member, string Declared)[] Listed =
    [
        ("[_Default]", "Enabled", "Boolean"), ("Enabled", "Enabled", "Boolean"), ("Index", "Index", "Integer"),
        ("Interval", "Interval", "Long"), ("Name", "Name", "String"), ("Parent", "Parent", "Form"), ("Tag", "Tag", "String"),
    ];

    /// <summary>The rows VB6's Locals window gives a Timer, each read as a program reads it (#2329). A read that raises is
    /// listed with its error, as VB6's Index is for a Timer in no control array.</summary>
    public static IReadOnlyList<LocalsRow> LocalsRows(Control timer) =>
    [
        .. Listed.Select(row =>
        {
            try
            {
                return new LocalsRow(row.Name, Read(timer, row.Member), null, row.Declared);
            }
            catch (VBRunTimeException e)
            {
                return new LocalsRow(row.Name, default, e.Error.Description, row.Declared);
            }
            // Not a raise VB6 would make, such as Parent outside a running form, but one shouldn't hide the other rows.
            catch (Exception e)
            {
                return new LocalsRow(row.Name, default, e.Message.ReplaceLineEndings(" ").Trim(), row.Declared);
            }
        }),
    ];

    // The way the interpreter reads a Timer's member: Parent here, a bound property, then Name and Index.
    private static Vb6Value Read(Control timer, string member)
    {
        if (TryGet(timer, member, out var parent))
            return parent;
        foreach (var property in VBProperties.PropertiesByName.GetValueOrDefault(member, []))
            if (AvaloniaInteroperability.TryGet(timer, property, out var value))
                return value;
        return IdentityMembers.TryGet(timer, member, out var identity)
            ? identity
            : throw new NotImplementedException($"A Timer's {member} has no reader");
    }
}
