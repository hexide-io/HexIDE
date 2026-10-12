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
    }

    public ExecutionEnvironment()
    {

    }

    public void DefineVariable(string name, int location)
    {
        variableToLocation[name] = location;
    }

    public bool TryGetVariableLocation(string name, out int location)
    {
        return variableToLocation.TryGetValue(name, out location);
    }

    public ExecutionEnvironment Clone()
    {
        return new ExecutionEnvironment(this);
    }
}