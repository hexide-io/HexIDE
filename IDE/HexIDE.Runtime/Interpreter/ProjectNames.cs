using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// The names other modules make visible to one module unqualified: each standard module's Public (or Global) variables
/// and Consts (#880), and the members of each standard or class module's Public Enums (#889). An environment consults this only for a name it does not hold itself, so the procedure's locals and
/// parameters, and the module's own variables, Consts, fields and controls, are found first.
/// </summary>
/// <remarks>
/// <para>What VB6 does, measured with <c>vb6.exe /make</c>, native and p-code agreeing:</para>
/// <list type="bullet">
/// <item>Module2's <c>Public G As Long</c>, <c>Global G2 As Integer</c> and <c>Public Const M = 3</c> are read, written and
/// passed ByRef from Module1 bare, with or without Option Explicit, and from a class's code and a form's.</item>
/// <item>A Private one (<c>Private P</c>, a module-level <c>Dim</c>, <c>Private Const</c>) is not visible: bare, it is a new
/// variable, and under Option Explicit "Variable not defined".</item>
/// <item>The module's own name wins: its module-level <c>Dim X</c>, its <c>Function V</c>, and in a form, the form's own
/// members (L2, #995). A procedure's local shadows it too.</item>
/// <item>Two modules exporting the name is "Ambiguous name detected" only where it is used bare, and so is one module's
/// Public variable beside another's Public Sub or Function of the name. Each module still reads its own, and qualified
/// reads are unaffected.</item>
/// <item>It is found before a built-in constant of the same name: Module2's <c>Public Const vbCancel = 42</c> is 42 from
/// Module1. That falls out of the order: a name an environment resolves is read before any built-in is tried.</item>
/// <item>A member of another module's Public Enum reads as its Long, a class's Enum included, and from a class's and a
/// form's code. A Private Enum's is not visible. Two modules' members of one name are ambiguous bare, and so is a member
/// beside another module's Public variable. Assigning one is "Assignment to constant not permitted" (#889).</item>
/// </list>
/// <para>This is collection, not binding: the table is built from the pre-pass's lists of each module's Public names, and
/// a name is related to its declaration only when the walk reads it, through the owner's own environment.</para>
/// </remarks>
internal sealed class ProjectNames
{
    // Each visible name, and the module that declares it. Null where two modules export it: a bare use is ambiguous.
    private readonly Dictionary<string, ModuleInfo?> owners = new(StringComparer.OrdinalIgnoreCase);

    private ProjectNames()
    {
    }

    /// <summary>The names <paramref name="reader"/> sees unqualified that another standard module declares.</summary>
    public static ProjectNames For(ModuleInfo reader, IReadOnlyList<ModuleInfo> modules)
    {
        var names = new ProjectNames();
        foreach (var owner in modules)
        {
            if (ReferenceEquals(owner, reader) || owner.Kind != InterpreterModuleKind.Standard)
                continue;
            foreach (var name in owner.PrePass.PublicVariables)
                names.Add(reader, owner, name);
            foreach (var (name, constant) in owner.PrePass.ModuleConsts)
                if (constant.Public)
                    names.Add(reader, owner, name);
        }
        // A Public Enum's members, a class's as well as a standard module's (measured, #889). Each is the read-only slot its
        // module's pre-pass hoisted it into.
        foreach (var owner in modules)
        {
            if (ReferenceEquals(owner, reader) || owner.Kind is not (InterpreterModuleKind.Standard or InterpreterModuleKind.Class))
                continue;
            foreach (var (enumName, members) in owner.PrePass.Enums)
                if (!owner.PrePass.PrivateEnums.Contains(enumName))
                    foreach (var member in members.Keys)
                        names.Add(reader, owner, member);
        }
        // A Public variable or Const beside another module's Public procedure of the name is ambiguous as well (measured
        // for a Sub and for a Function).
        foreach (var name in new List<string>(names.owners.Keys))
            foreach (var other in modules)
                if (!ReferenceEquals(other, reader) && !ReferenceEquals(other, names.owners[name])
                    && other.Kind == InterpreterModuleKind.Standard
                    && other.PrePass.Procedures.TryGetValue(name, out var procedure) && !procedure.IsPrivate)
                    names.owners[name] = null;
        return names;
    }

    private void Add(ModuleInfo reader, ModuleInfo owner, string name)
    {
        if (DeclaresItself(reader, name))
            return;
        owners[name] = owners.ContainsKey(name) ? null : owner;
    }

    // A name the reading module resolves before the project's, found by a lookup that comes after the environment's.
    // Its variables, Consts, fields and controls need no entry here: the environment holds them, and is asked first.
    private static bool DeclaresItself(ModuleInfo reader, string name)
    {
        if (reader.PrePass.Procedures.ContainsKey(name) || reader.PrePass.Properties.ContainsKey(name))
            return true;
        foreach (var members in reader.PrePass.Enums.Values)
            if (members.ContainsKey(name))
                return true;
        // A form's own members come before the project's globals: its Caption beats a module's Public Caption (L2,
        // measured). They are looked up after the environment, so a Public of the name must not be found here.
        return reader.Kind == InterpreterModuleKind.Form && VB6FormMembers.TryGet(name, out _);
    }

    /// <summary>
    /// The slot of a name another module exports, read from that module's own environment, so it is the slot the module's
    /// declarations filled. Throws VB6's compile error where the name is ambiguous.
    /// </summary>
    public bool TryGetLocation(string name, out int location)
    {
        location = default;
        if (!owners.TryGetValue(name, out var owner))
            return false;
        if (owner is null)
            throw new VBCompileErrorException("Ambiguous name detected: " + name);
        return owner.ModuleEnv.TryGetOwnVariableLocation(name, out location);
    }
}
