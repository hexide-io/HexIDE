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

/// <summary>
/// The run's forms, as the interpreter reaches them (#822): making a form's default instance, loading it, and showing and
/// hiding it. The program implements it. A run with none, as a test's bare interpreter is, has no forms to reach.
/// </summary>
public interface IFormProgram
{
    /// <summary>Make the default instance of <paramref name="form"/>, stored in the form's name: Initialize runs here.</summary>
    Task MakeDefaultInstance(ModuleInfo form);

    /// <summary>Load <paramref name="form"/> if it isn't loaded: Form_Load runs here. A form that unloaded gets a new window.</summary>
    Task Load(VbObject form);
}
