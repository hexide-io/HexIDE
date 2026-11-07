using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// A form's control or built-in member was used while the form isn't loaded: before its Load, or after its Unload (#822).
/// VB6 loads the form there and then (measured). Where the walk can, it does the same and reads again
/// (<see cref="BasicInterpreter.Loading{T}"/>). Anywhere else this stays the refusal it always was.
/// </summary>
public sealed class FormLoadNeeded(VbObject form, string what)
    : NotImplementedException(form.FormUnloaded
        ? VbObject.UsedAfterUnload(form.ClassName, what)
        : VbObject.NotYetLoaded(form.ClassName, what))
{
    /// <summary>The form to load.</summary>
    public VbObject Form { get; } = form;
}

/// <summary>
/// A form's name was read while it holds no instance: before the form's first use, or after <c>Set Form2 = Nothing</c>.
/// VB6 makes the form's default instance there, raising Form_Initialize (measured, #822). Where the walk can, it does the
/// same and reads again; anywhere else this is refused.
/// </summary>
public sealed class FormInstanceNeeded(ModuleInfo form)
    : NotImplementedException($"{form.Name} is used where HexIDE can't yet make its instance (#822).")
{
    /// <summary>The form module whose default instance to make.</summary>
    public ModuleInfo Form { get; } = form;
}

/// <summary>What a variable declared <c>As New</c> makes (a class's name, or "Collection"), and the name it was declared with,
/// for a refusal to name.</summary>
public sealed record AutoNew(string ClassName, string Name);

/// <summary>
/// A variable declared <c>As New</c> was read while it holds Nothing: before its first use, or after it was set to
/// Nothing. VB6 makes a new object there, running Class_Initialize, and every use counts, <c>o Is Nothing</c> and
/// <c>TypeName(o)</c> among them (measured, #435). Where the walk can, it does the same and reads again, as for a form's
/// default instance; anywhere else this is refused.
/// </summary>
public sealed class AutoInstanceNeeded(int location, AutoNew autoNew)
    : NotImplementedException($"{autoNew.Name} is declared As New, and is used where HexIDE can't yet make its object (#435).")
{
    public AutoInstanceNeeded(VbUdt bag, string field, AutoNew autoNew) : this(-1, autoNew)
    {
        Field = new UdtFieldRef(bag, field);
    }

    public UdtFieldRef? Field { get; }

    /// <summary>The slot to fill.</summary>
    public int Location { get; } = location;

    /// <summary>What to make, and the name it is made for.</summary>
    public AutoNew AutoNew { get; } = autoNew;
}

/// <summary>
/// The run's forms, as the interpreter reaches them (#822): making a form's default instance, and loading it. The program
/// implements it. A run with none, as a test's bare interpreter is, has no forms to reach.
/// </summary>
public interface IFormProgram
{
    /// <summary>Make the default instance of <paramref name="form"/>, stored in the form's name: Initialize runs here.</summary>
    Task MakeDefaultInstance(ModuleInfo form);

    /// <summary>Load <paramref name="form"/> if it isn't loaded: Form_Load runs here. A form that unloaded gets a new window.</summary>
    Task Load(VbObject form);

    /// <summary>The loaded forms, in the order they loaded: what the global <c>Forms</c> holds (#2496).</summary>
    IReadOnlyList<VbObject> LoadedForms { get; }

    /// <summary><c>Screen.ActiveForm</c> (#2431): the form last activated, until another is or it unloads.</summary>
    VbObject? ActiveForm { get; }

    /// <summary><c>Screen.ActiveControl</c> (#2431): the active form's control with the focus, or null.</summary>
    Avalonia.Controls.Control? ActiveControl { get; }

    /// <summary><c>Screen.MousePointer</c> has changed (#2431): every form shows it while it is not 0.</summary>
    void ScreenPointerChanged(BuiltinTypes.VBCursorType pointer);
}
