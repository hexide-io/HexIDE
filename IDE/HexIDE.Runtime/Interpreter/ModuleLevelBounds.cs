using Antlr4.Runtime.Tree;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// What a module-level Const's value and a module-level array's bounds may read: VB6's constant expressions, checked
/// as the module's declarations run, never in the pre-pass. VB6 works both out before the program starts, and refuses
/// to compile one it cannot. Every rule here was measured with <c>vb6.exe /make</c> (the oracle's "What a module-level
/// array's bound may name"):
/// <list type="bullet">
/// <item>a same-module Const declared above, an Enum member of the module, bare or as <c>Sizes.eMax</c>, another
/// module's Public Enum member qualified by its Enum's name, and a built-in constant (<c>vbKeyA</c>);</item>
/// <item>in a bound, not in a Const, some built-in functions of constants: <c>Abs</c>, <c>CBool</c>, <c>CByte</c>,
/// <c>CCur</c>, <c>CDbl</c>, <c>CInt</c>, <c>CLng</c>, <c>CSng</c>, <c>CStr</c>, <c>CVar</c>, <c>Fix</c>, <c>Int</c>,
/// <c>Len</c>, <c>LenB</c> and <c>Sgn</c>. A Const refuses every function call, <c>Len</c> included.</item>
/// </list>
/// A Const below or the Const itself, a variable, a user function, another module's Public variable, and the built-ins
/// measured as refused are "Constant expression required"; a name the module cannot see is "Variable not defined"
/// under Option Explicit, and the first otherwise; the module's own name as a qualifier is "Circular dependencies
/// between modules".
/// </summary>
/// <remarks>
/// <para>Each module-level Const is checked as it runs, before it is evaluated, and the Consts above it ran first, so a
/// Const a bound or another Const names has been checked already: the walk is one level deep. This relates symbols,
/// which the pre-pass must not do, so it runs with the declarations. The pre-pass only collects: each Const's
/// declaration and visibility, and each Public variable.</para>
/// <para>Two things VB6 reads are refused loudly instead, because HexIDE would read Empty: another module's Public
/// Const, qualified or not (#880), and another module's Public Enum member, unqualified (#889). A built-in function
/// nobody has measured in a bound is refused loudly too, rather than guessed either way.</para>
/// <para>A procedure's Consts are not checked here (its <c>Dim a(N)</c> above <c>Const N</c> runs, a missing compile
/// error, recorded).</para>
/// </remarks>
internal sealed class ModuleLevelBounds
{
    // Measured legal in a module-level bound (not in a Const initialiser, where every function call is refused).
    private static readonly HashSet<string> AllowedFunctions = new(StringComparer.OrdinalIgnoreCase)
        { "Abs", "CBool", "CByte", "CCur", "CDbl", "CInt", "CLng", "CSng", "CStr", "CVar", "Fix", "Int", "Len", "LenB", "Sgn" };

    // Measured refused there, with "Constant expression required".
    private static readonly HashSet<string> RefusedFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Asc", "AscW", "Atn", "CDec", "Choose", "Chr", "Exp", "Hex", "IIf", "InStr", "LBound", "Left", "Log", "Mid", "Rnd",
        "Round", "Sin", "Space", "Sqr", "Timer", "UCase", "Val",
    };

    private readonly BasicInterpreter interpreter;
    private readonly ModuleInfo module;
    private readonly ExecutionEnvironment env;
    // What is being checked, for a refusal's message ("d's bounds", "Const C's value"), and its verbs.
    private readonly string subject;
    private readonly string reads, calls;

    private ModuleLevelBounds(BasicInterpreter interpreter, ModuleInfo module, ExecutionEnvironment env, string subject,
        bool plural)
    {
        this.interpreter = interpreter;
        this.module = module;
        this.env = env;
        this.subject = subject;
        (reads, calls) = plural ? ("read", "call") : ("reads", "calls");
    }

    /// <summary>A module-level array's bounds, at its Dim.</summary>
    public static void Check(BasicInterpreter interpreter, ModuleInfo module, ExecutionEnvironment env,
        VB6Parser.SubscriptsContext bounds, VB6Parser.VariableSubStmtContext dim)
        => new ModuleLevelBounds(interpreter, module, env, dim.ambiguousIdentifier().GetText() + "'s bounds", plural: true)
            .Walk(bounds, inConst: false, position: dim.Start.TokenIndex);

    /// <summary>
    /// A module-level Const's value, as the Const runs and before it is evaluated. Every Const above it was checked
    /// when it ran, so a same-module Const it names is a constant expression already, and the walk need go no deeper.
    /// </summary>
    public static void CheckConst(BasicInterpreter interpreter, ModuleInfo module, ExecutionEnvironment env,
        VB6Parser.ConstSubStmtContext constant)
        => new ModuleLevelBounds(interpreter, module, env, "Const " + constant.ambiguousIdentifier().GetText() + "'s value", plural: false)
            .Walk(constant.valueStmt(), inConst: true, position: constant.Start.TokenIndex);

    // `position` is where the expression sits: a name it reads must be declared before it.
    private void Walk(IParseTree node, bool inConst, int position)
    {
        switch (node)
        {
            case VB6Parser.ICS_S_MembersCallContext members:
                Qualified(members.GetText());
                return;
            case VB6Parser.ICS_S_VariableOrProcedureCallContext name:
                Name(name.ambiguousIdentifier().GetText(), inConst, position);
                return;
            case VB6Parser.ICS_S_ProcedureOrArrayCallContext call when call.ambiguousIdentifier() is { } callee:
                Call(callee.GetText(), inConst);
                break;   // its arguments are read as the walk goes on
        }
        for (var i = 0; i < node.ChildCount; i++)
            Walk(node.GetChild(i), inConst, position);
    }

    private void Name(string name, bool inConst, int position)
    {
        if (module.PrePass.ModuleConsts.TryGetValue(name, out var constant))
        {
            // A Const below, or the Const itself, is not yet a constant (measured: forward and self references). One
            // above was checked when it ran (CheckConst), so it is one.
            if (constant.Declaration.Start.TokenIndex >= position)
                throw ConstantExpressionRequired();
            return;
        }
        if (module.PrePass.Enums.Values.Any(members => members.ContainsKey(name)))
            return;
        if (module.PrePass.Procedures.ContainsKey(name) || module.PrePass.Properties.ContainsKey(name))
            throw ConstantExpressionRequired();
        if (env.TryGetVariableLocation(name, out _))
            throw ConstantExpressionRequired();   // a variable of this module
        if (interpreter.BuiltIns.TryGetBuiltInConstant(name, out _))
            return;
        if (VB6BuiltIns.IsBuiltInFunction(name))
        {
            Call(name, inConst);   // `Timer`, called without parentheses
            return;
        }
        foreach (var other in interpreter.Modules.All)
        {
            if (ReferenceEquals(other, module) || other.Kind != InterpreterModuleKind.Standard)
                continue;
            if (other.PrePass.ModuleConsts.TryGetValue(name, out var theirs) && theirs.Public)
                throw AnotherModulesConst(name);
            if (other.PrePass.Enums.Any(e => !other.PrePass.PrivateEnums.Contains(e.Key) && e.Value.ContainsKey(name)))
                throw AnotherModulesEnumMember(name);
            if (other.PrePass.PublicVariables.Contains(name)
                || (other.PrePass.Procedures.TryGetValue(name, out var procedure) && !procedure.IsPrivate))
                throw ConstantExpressionRequired();
        }
        // A name the module cannot see: another module's Private Const or Enum, or nothing at all.
        if (module.PrePass.RequireVariableDefinitions)
            throw new VBVariableNotDefinedException(name);
        throw ConstantExpressionRequired();
    }

    private void Qualified(string text)
    {
        var parts = text.Split('.');
        if (parts.Length != 2)
            throw Unsupported(text);
        var (qualifier, member) = (parts[0], parts[1]);

        if (string.Equals(qualifier, module.Name, StringComparison.OrdinalIgnoreCase))
        {
            // Measured for a Const and an Enum member of the module itself.
            if (module.PrePass.ModuleConsts.ContainsKey(member)
                || module.PrePass.Enums.Values.Any(members => members.ContainsKey(member)))
                throw new VBCompileErrorException("Circular dependencies between modules");
            throw Unsupported(text);
        }
        if (interpreter.TryResolveEnum(qualifier, module, out var enumMembers))
        {
            if (enumMembers.ContainsKey(member))
                return;
            throw Unsupported(text);
        }
        if (interpreter.Modules.TryGet(qualifier, out var other) && other.Kind == InterpreterModuleKind.Standard)
        {
            if (other.PrePass.ModuleConsts.TryGetValue(member, out var theirs) && theirs.Public)
                throw AnotherModulesConst(text);
            if (other.PrePass.PublicVariables.Contains(member))
                throw ConstantExpressionRequired();
        }
        throw Unsupported(text);
    }

    private void Call(string name, bool inConst)
    {
        var function = name.TrimEnd('$');
        // Every function call in a Const initialiser was refused: Len, Asc, Int and a user function (measured).
        if (inConst)
            throw ConstantExpressionRequired();
        // A user function, here or in another module (measured: `Dim d(Twice(2))`).
        if (module.PrePass.Procedures.ContainsKey(function)
            || interpreter.Modules.All.Any(m => m.PrePass.Procedures.TryGetValue(function, out var p) && !p.IsPrivate))
            throw ConstantExpressionRequired();
        if (AllowedFunctions.Contains(function))
            return;
        if (RefusedFunctions.Contains(function))
            throw ConstantExpressionRequired();
        throw new NotImplementedException(
            $"{subject} {calls} {name}. VB6 allows only some built-in functions in a module-level array's bounds, " +
            "and HexIDE does not know whether this is one, so it does not size the array from it.");
    }

    private static VBCompileErrorException ConstantExpressionRequired() => new("Constant expression required");

    private NotImplementedException AnotherModulesConst(string name) => new(
        $"{subject} {reads} {name}, another module's Public Const. A module's Consts and arrays are worked out as its " +
        "declarations run, and HexIDE does not read another module's Const there yet: unqualified it is not found, and " +
        "qualified it may not have been initialised (#880).");

    private NotImplementedException AnotherModulesEnumMember(string name) => new(
        $"{subject} {reads} {name}, a member of another module's Enum. HexIDE does not read another module's Enum " +
        "member unqualified yet (#889). Qualify it with its Enum's name.");

    private NotImplementedException Unsupported(string text) => new(
        $"{subject} {reads} {text}, which HexIDE does not read in a module-level Const or array bound.");
}
