using System;
using System.Collections.Generic;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// A form's own built-in members, reached by name from its code: bare (<c>Caption = "x"</c>, implicit <c>Me</c>, #995)
/// or through <c>Me</c> and the form's name. The member is VB6's when <see cref="VB6FormMembers"/> has it, which is read
/// from VB6.OLB; the value is the window's.
/// </summary>
/// <remarks>
/// VB6 resolves a bare name in a form module against the form after the procedure's locals and the form's own
/// variables, controls and procedures, and before the project's globals (measured, #822: the form's <c>Caption</c> wins
/// over a module's <c>Public Caption</c>, and its <c>Tag</c> over a module's <c>Function Tag</c>). Its locals still shadow
/// it (measured: a <c>Dim Caption</c> in Load is that local). A name that is a VB6 member HexIDE can't read or set is
/// refused as not implemented, never left to become a new variable: <c>CurrentX = 0</c> quietly making a variable is
/// the silent wrong result #995 was filed for.
/// </remarks>
internal static class FormMembers
{
    /// <summary>
    /// VB6's compile error for assigning to a Form member that has no let, bare or through <c>Me</c>: measured for
    /// <c>hWnd</c>, <c>hDC</c>, <c>Count</c> and <c>HasDC</c>. Not the "read-only property" message one might expect.
    /// </summary>
    public const string AssignToReadOnly =
        "Function or interface marked as restricted, or the function uses an Automation type not supported in Visual Basic";

    /// <summary>
    /// The form a bare name in <paramref name="env"/>'s procedure belongs to, and VB6's Form member of that name. False
    /// outside a form's code, as in a standard module or a class, and for a name VB6's Form doesn't have.
    /// </summary>
    public static bool TryBare(ModuleExecutionContext context, ExecutionEnvironment env, string name,
        out VbObject form, out VB6FormMembers.Member member)
    {
        form = null!;
        member = null!;
        if (!context.TryInspectVariable(env, "Me", out var me)
            || me.Value is not VbObject { ClassDef.Kind: InterpreterModuleKind.Form, FormWindow: not null } owner
            || !VB6FormMembers.TryGet(name, out member))
            return false;
        form = owner;
        return true;
    }

    /// <summary>
    /// Whether a call of a Form property, with <paramref name="argCount"/> arguments, is VBA's function of the same name.
    /// VB6 takes VBA's when the arguments fit it, and otherwise refuses the call as the property's (measured: in a form,
    /// <c>Left("abc", 1)</c> is <c>a</c>, while <c>Left(5)</c> and <c>Left("abc", 1, 2)</c> are "Wrong number of arguments
    /// or invalid property assignment", where in a module <c>Left(5)</c> is "Argument not optional"). A project's own
    /// Function is never the fallback: <c>Caption(1)</c> is refused though a module declares <c>Function Caption(n)</c>.
    /// Left is the one Form member that is also a VBA function (the rest of VB6's intrinsic names the Form shares, Name,
    /// is a statement), and it takes two arguments.
    /// </summary>
    public static bool CallIsVbas(string name, int argCount) =>
        string.Equals(name, "Left", StringComparison.OrdinalIgnoreCase) && argCount == 2;

    /// <summary>Whether VB6's Form has a member of this name.</summary>
    public static bool IsMember(string name, out VB6FormMembers.Member member) => VB6FormMembers.TryGet(name, out member);

    /// <summary>Read a property of the form, or refuse it as not implemented where VB6 has it and HexIDE doesn't.</summary>
    public static Vb6Value Read(VbObject form, string name)
    {
        var window = form.BuiltInMembersFor(name)!;
        // A form's Name is its module's, read-only at run time: no property of the window holds it.
        if (IsName(name))
            return new Vb6Value(form.ClassName);
        if (IsScaleSize(name, out var vertical) && window is Avalonia.Controls.Window shown)
            return AvaloniaInterop.ControlPositions.ScaleSize(shown, vertical);
        foreach (var property in VBProperties.PropertiesByName.GetValueOrDefault(name, []))
            if (AvaloniaInteroperability.TryGet(window, property, out var value))
                return value;
        throw new NotImplementedException($"Reading {form.ClassName}'s {name} is not implemented{TrackedBy(name)}");
    }

    /// <summary>
    /// Assign a property of the form: VB6's compile error where the member has no let, and not implemented where VB6 can
    /// set it and HexIDE can't.
    /// </summary>
    public static void Write(VbObject form, string name, VB6FormMembers.Member member, Vb6Value value)
    {
        if (!member.CanLet)
            throw new VBCompileErrorException(AssignToReadOnly);
        if (IsScaleSize(name, out _))
            throw new NotImplementedException($"Setting {form.ClassName}'s {name} is not implemented: it sets a scale of the program's own (#990)");
        var window = form.BuiltInMembersFor(name)!;
        foreach (var property in VBProperties.PropertiesByName.GetValueOrDefault(name, []))
            if (AvaloniaInteroperability.TrySet(window, property, value))
                return;
        throw new NotImplementedException($"Setting {form.ClassName}'s {name} is not implemented{TrackedBy(name)}");
    }

    /// <summary>Call a method of the form, as <c>Me.Hide</c> does: through its window, which says when it can't.</summary>
    public static Vb6Value Call(VbObject form, string name, IReadOnlyList<Vb6Value> args) =>
        form.BuiltInMembersFor(name)!.Call(name, args);

    private static bool IsName(string name) => string.Equals(name, "Name", StringComparison.OrdinalIgnoreCase);

    // ScaleWidth and ScaleHeight, which no property of the window holds: the client area, in the form's units (#1071).
    private static bool IsScaleSize(string name, out bool vertical)
    {
        vertical = string.Equals(name, "ScaleHeight", StringComparison.OrdinalIgnoreCase);
        return vertical || string.Equals(name, "ScaleWidth", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The issue that tracks a Form member HexIDE lacks, as its refusal names it, or empty where no issue does yet. A
    /// refusal that names its issue tells the reader whether it is known, and where to look.
    /// </summary>
    internal static string TrackedBy(string name) =>
        string.Equals(name, "Controls", StringComparison.OrdinalIgnoreCase) ? ": the form's Controls collection is #1060"
        : name.StartsWith("Scale", StringComparison.OrdinalIgnoreCase) ? ": the form's coordinate system is #990"
        : "";

    /// <summary>
    /// Whether HexIDE can read <paramref name="name"/> on the form's window: what decides, for <c>Me.X</c>, between
    /// reading it and refusing a VB6 member HexIDE lacks as not implemented, where it used to raise 438.
    /// </summary>
    public static bool CanRead(Avalonia.Controls.Control window, string name)
    {
        if (IsName(name) || (IsScaleSize(name, out _) && window is Avalonia.Controls.Window))
            return true;
        foreach (var property in VBProperties.PropertiesByName.GetValueOrDefault(name, []))
            if (AvaloniaInteroperability.TryGet(window, property, out _))
                return true;
        return false;
    }
}
