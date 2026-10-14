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

    /// <summary>Whether VB6's Form has a member of this name.</summary>
    public static bool IsMember(string name, out VB6FormMembers.Member member) => VB6FormMembers.TryGet(name, out member);

    /// <summary>Read a property of the form, or refuse it as not implemented where VB6 has it and HexIDE doesn't.</summary>
    public static Vb6Value Read(VbObject form, string name)
    {
        var window = form.BuiltInMembersFor(name)!;
        // A form's Name is its module's, read-only at run time: no property of the window holds it.
        if (IsName(name))
            return new Vb6Value(form.ClassName);
        foreach (var property in VBProperties.PropertiesByName.GetValueOrDefault(name, []))
            if (AvaloniaInteroperability.TryGet(window, property, out var value))
                return value;
        throw new NotImplementedException($"Reading {form.ClassName}'s {name} is not implemented");
    }

    /// <summary>
    /// Assign a property of the form: VB6's compile error where the member has no let, and not implemented where VB6 can
    /// set it and HexIDE can't.
    /// </summary>
    public static void Write(VbObject form, string name, VB6FormMembers.Member member, Vb6Value value)
    {
        if (!member.CanLet)
            throw new VBCompileErrorException(AssignToReadOnly);
        var window = form.BuiltInMembersFor(name)!;
        foreach (var property in VBProperties.PropertiesByName.GetValueOrDefault(name, []))
            if (AvaloniaInteroperability.TrySet(window, property, value))
                return;
        throw new NotImplementedException($"Setting {form.ClassName}'s {name} is not implemented");
    }

    /// <summary>Call a method of the form, as <c>Me.Hide</c> does: through its window, which says when it can't.</summary>
    public static Vb6Value Call(VbObject form, string name, IReadOnlyList<Vb6Value> args) =>
        form.BuiltInMembersFor(name)!.Call(name, args);

    private static bool IsName(string name) => string.Equals(name, "Name", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether HexIDE can read <paramref name="name"/> on the form's window: what decides, for <c>Me.X</c>, between
    /// reading it and refusing a VB6 member HexIDE lacks as not implemented, where it used to raise 438.
    /// </summary>
    public static bool CanRead(Avalonia.Controls.Control window, string name)
    {
        if (IsName(name))
            return true;
        foreach (var property in VBProperties.PropertiesByName.GetValueOrDefault(name, []))
            if (AvaloniaInteroperability.TryGet(window, property, out _))
                return true;
        return false;
    }
}
