using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

public class ExecutionEnvironment
{
    // Case-insensitive, as VB6 names are: `abc` after `Dim Abc` is the same variable (#811). Every name a running
    // program reaches through an environment lives here (locals, parameters, a Function's return slot, module
    // variables and constants, class fields, controls, the form, Me, Debug, Err and App), so the comparer is set
    // once, where the table is made, rather than folded at each lookup. The first spelling declared stays the key,
    // which is the one the Locals window shows. VB6 refuses two names in one scope that differ only in case, and a
    // case-only name in an inner scope is a new variable there, so this changes no lookup VB6 would answer
    // differently. The copy constructor below runs this initializer too, so a clone keeps the comparer.
    public Dictionary<string, int> variableToLocation = new(StringComparer.OrdinalIgnoreCase);

    private ExecutionEnvironment(ExecutionEnvironment other)
    {
        foreach (var (name, loc) in other.variableToLocation)
            variableToLocation[name] = loc;
        ProjectNames = other.ProjectNames;
    }

    public ExecutionEnvironment()
    {

    }

    public void DefineVariable(string name, int location)
    {
        variableToLocation[name] = location;
    }

    /// <summary>
    /// The slot a name reads here: one this environment holds, or else another standard module's Public variable or Const
    /// (#880). Throws VB6's "Ambiguous name detected" for a name two modules export.
    /// </summary>
    public bool TryGetVariableLocation(string name, out int location) =>
        variableToLocation.TryGetValue(name, out location) || ProjectNames?.TryGetLocation(name, out location) == true;

    /// <summary>A name this environment holds itself: a local, a parameter, or the module's own declaration.</summary>
    public bool TryGetOwnVariableLocation(string name, out int location) =>
        variableToLocation.TryGetValue(name, out location);

    /// <summary>
    /// What the module this environment belongs to sees of other modules' Public names, set once every module is loaded.
    /// A procedure's environment and an instance's are cloned from the module's, and share it.
    /// </summary>
    internal ProjectNames? ProjectNames { get; set; }

    /// <summary>
    /// The parameters of the procedure this environment runs, by name. A ByRef one shares the caller's slot, so the slot
    /// cannot say it is a parameter; the name can. ReDim of a fixed array through one, an array parameter or an untyped
    /// one, is a run-time 10, and of a fixed array named directly VB6 does not compile (measured). Not copied by
    /// <see cref="Clone"/>: each call has its own.
    /// </summary>
    public HashSet<string> Parameters { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The parameters declared untyped or <c>As Variant</c>, which ReDim treats by the Variant rule even when they share a
    /// caller's slot marked as a declared array (measured, #970). Not copied by <see cref="Clone"/>.
    /// </summary>
    public HashSet<string> VariantParameters { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ExecutionEnvironment Clone()
    {
        return new ExecutionEnvironment(this);
    }
}