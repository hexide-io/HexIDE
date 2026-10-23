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
        if (IsWindowState(name) && window is Avalonia.Controls.Window stateWindow)
            return ReadWindowState(stateWindow);
        foreach (var property in VBProperties.PropertiesByName.GetValueOrDefault(name, []))
            if (AvaloniaInteroperability.TryGet(window, property, out var value))
                return value;
        if (FontMembers.TryGet(window, name, out var fontMember))
            return fontMember;
        throw new NotImplementedException($"Reading {form.ClassName}'s {name} is not implemented{TrackedBy(name)}");
    }

    /// <summary>
    /// Assign a property of the form: VB6's compile error where the member has no let, and not implemented where VB6 can
    /// set it and HexIDE can't.
    /// </summary>
    public static void Write(VbObject form, string name, VB6FormMembers.Member member, Vb6Value value)
    {
        // VB6 raises 382 for it through an Object, the only way a program it compiles can assign it; early-bound, it refuses
        // the statement at compile time (measured, #1431).
        if (IsName(name))
            throw IdentityMembers.ReadOnlyAtRunTime("Name");
        if (!member.CanLet)
            throw new VBCompileErrorException(AssignToReadOnly);
        if (IsScaleSize(name, out _))
            throw new NotImplementedException($"Setting {form.ClassName}'s {name} is not implemented: it sets a scale of the program's own (#990)");
        var window = form.BuiltInMembersFor(name)!;
        if (IsWindowState(name) && window is Avalonia.Controls.Window stateWindow)
        {
            WriteWindowState(stateWindow, value);
            return;
        }
        foreach (var property in VBProperties.PropertiesByName.GetValueOrDefault(name, []))
            if (AvaloniaInteroperability.TrySet(window, property, value))
                return;
        // The form's own font: its controls keep theirs, as vb6.exe measured (#1141).
        if (FontMembers.TrySet(window, name, value))
            return;
        throw new NotImplementedException($"Setting {form.ClassName}'s {name} is not implemented{TrackedBy(name)}");
    }

    /// <summary>Call a method of the form, as <c>Me.Hide</c> does: through its window, which says when it can't.</summary>
    public static Vb6Value Call(VbObject form, string name, IReadOnlyList<Vb6Value> args) =>
        form.BuiltInMembersFor(name)!.Call(name, args);

    private static bool IsName(string name) => string.Equals(name, "Name", StringComparison.OrdinalIgnoreCase);

    private static bool IsWindowState(string name) => string.Equals(name, "WindowState", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// WindowState, an Integer: 0 normal, 1 minimized, 2 maximized (#1152). A form not yet shown reads 0 whatever it was
    /// set to or saved with, and reads the state it showed in once shown (measured).
    /// </summary>
    private static Vb6Value ReadWindowState(Avalonia.Controls.Window window) =>
        new(window.IsVisible
            ? window.WindowState switch
            {
                Avalonia.Controls.WindowState.Minimized => 1,
                Avalonia.Controls.WindowState.Maximized or Avalonia.Controls.WindowState.FullScreen => 2,
                _ => 0,
            }
            : 0);

    /// <summary>
    /// Sets WindowState: a hidden form takes it when it is shown, and a shown one at once, raising Resize as its size
    /// changes (measured). Converted as an Integer is, so 1.6 is 2; Null is 94, and anything but 0, 1 or 2 is 380.
    /// </summary>
    private static void WriteWindowState(Avalonia.Controls.Window window, Vb6Value value)
    {
        if (value.IsNull)
            throw new VBRunTimeException(VBStandardError.InvalidUseOfNull);
        var state = Convert.ToInt32(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Integer, null).Value,
            System.Globalization.CultureInfo.InvariantCulture);
        window.WindowState = AvaloniaWindowState(state) ?? throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
    }

    /// <summary>VB6's WindowState as Avalonia's, or null for a value VB6 refuses.</summary>
    internal static Avalonia.Controls.WindowState? AvaloniaWindowState(int state) => state switch
    {
        0 => Avalonia.Controls.WindowState.Normal,
        1 => Avalonia.Controls.WindowState.Minimized,
        2 => Avalonia.Controls.WindowState.Maximized,
        _ => null,
    };

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
}
