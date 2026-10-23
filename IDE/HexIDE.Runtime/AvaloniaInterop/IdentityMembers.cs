using System;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>
/// A control's Name and Index at run time (#1431). Measured on vb6.exe, native and p-code agreeing (the oracle's "A control's
/// Name and Index, at run time"):
/// <list type="bullet">
/// <item>Every control has a Name, a Line, a Timer and a menu among them: a String, the name on its Begin line. An element
/// of a control array has the array's, with no index.</item>
/// <item>An element's Index is its index, an Integer. Any other control's is 343, "Object not an array", and a form has
/// none: 438.</item>
/// <item>Assigning either is 382, "'Name' property cannot be set at run time" or "'Index' ...", through an Object or a
/// Control, which is the only way a program VB6 compiles can assign it. Early-bound, VB6 refuses the statement at compile time, "Can't assign to
/// read-only property"; here it is the 382 too, when the statement runs.</item>
/// </list>
/// The Name property itself is named "(Name)", as the Properties window shows it, so a lookup by member name never finds
/// it. A form's Name is read by <see cref="FormMembers"/>.
/// </summary>
internal static class IdentityMembers
{
    public static bool IsName(string member) => string.Equals(member, "Name", StringComparison.OrdinalIgnoreCase);

    private static bool IsIndex(string member) => string.Equals(member, "Index", StringComparison.OrdinalIgnoreCase);

    public static bool TryGet(Control control, string member, out Vb6Value value)
    {
        value = default;
        if (VBProps.GetName(control) is not { } name)
            return false;
        if (IsName(member))
        {
            value = new Vb6Value(name);
            return true;
        }
        if (!IsIndex(member))
            return false;
        if (control is IFormView)
            throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        value = VBProps.GetIndex(control) is { } index
            ? new Vb6Value(index)
            : throw new VBRunTimeException(VBStandardError.ObjectNotAnArray);
        return true;
    }

    public static bool TrySet(Control control, string member)
    {
        if (VBProps.GetName(control) is null)
            return false;
        if (IsName(member))
            throw ReadOnlyAtRunTime("Name");
        if (IsIndex(member))
            throw ReadOnlyAtRunTime("Index");
        return false;
    }

    public static VBRunTimeException ReadOnlyAtRunTime(string member) => TextBoxMembers.ReadOnlyAtRunTime(member);
}
