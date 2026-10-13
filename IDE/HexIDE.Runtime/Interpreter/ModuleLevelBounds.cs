using Antlr4.Runtime.Tree;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// What a module-level array's bounds may read, checked where the array is allocated: as its module's declarations run,
/// never in the pre-pass. VB6 sizes such an array from a constant expression before the program starts, and refuses to
/// compile one it cannot. Every rule here was measured with <c>vb6.exe /make</c> (the oracle's "What a module-level
/// array's bound may name"):
/// <list type="bullet">
/// <item>a same-module Const declared above the Dim, whose own initialiser is checked by the same rules, through every
/// Const it names: a Const below, a variable, a function call or the Const itself there is "Constant expression
/// required";</item>
/// <item>an Enum member of the module, bare or as <c>Sizes.eMax</c>, and another module's Public Enum member qualified
/// by its Enum's name;</item>
/// <item>a built-in constant (<c>vbKeyA</c>);</item>
/// <item>in the bound itself, not in a Const, some built-in functions of constants: <c>Abs</c>, <c>CBool</c>,
/// <c>CByte</c>, <c>CCur</c>, <c>CDbl</c>, <c>CInt</c>, <c>CLng</c>, <c>CSng</c>, <c>CStr</c>, <c>CVar</c>, <c>Fix</c>,
/// <c>Int</c>, <c>Len</c>, <c>LenB</c> and <c>Sgn</c>.</item>
/// </list>
/// A variable, a user function, another module's Public variable, and the built-ins measured as refused are "Constant
/// expression required"; a name the module cannot see is "Variable not defined" under Option Explicit, and the first
/// otherwise; the module's own name as a qualifier is "Circular dependencies between modules".
/// </summary>
/// <remarks>
/// <para>This relates symbols, which the pre-pass must not do, so it runs at allocation. The pre-pass only collects:
/// each Const's declaration and visibility, and each Public variable.</para>
/// <para>Two things VB6 reads are refused loudly instead, because HexIDE would size the array from Empty: another
/// module's Public Const, qualified or not (#880), and another module's Public Enum member, unqualified (#889).
/// A built-in function nobody has measured in a bound is refused loudly too, rather than guessed either way.</para>
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
    private readonly string array;
    // Each same-module Const already found to be a constant expression.
    private readonly HashSet<VB6Parser.ConstSubStmtContext> checkedConsts = new();

    private ModuleLevelBounds(BasicInterpreter interpreter, ModuleInfo module, ExecutionEnvironment env, string array)
    {
        this.interpreter = interpreter;
        this.module = module;
        this.env = env;
        this.array = array;
    }

    public static void Check(BasicInterpreter interpreter, ModuleInfo module, ExecutionEnvironment env,
        VB6Parser.SubscriptsContext bounds, VB6Parser.VariableSubStmtContext dim)
        => new ModuleLevelBounds(interpreter, module, env, dim.ambiguousIdentifier().GetText())
            .Walk(bounds, inConst: false, position: dim.Start.TokenIndex);

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
            // A Const below, or the Const itself, is not yet a constant (measured: forward and self references).
            if (constant.Declaration.Start.TokenIndex >= position)
                throw ConstantExpressionRequired();
            if (checkedConsts.Add(constant.Declaration))
                Walk(constant.Declaration.valueStmt(), inConst: true, position: constant.Declaration.Start.TokenIndex);
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
            $"{array}'s bounds call {name}. VB6 allows only some built-in functions in a module-level array's bounds, " +
            "and HexIDE does not know whether this is one, so it does not size the array from it.");
    }

    private static VBCompileErrorException ConstantExpressionRequired() => new("Constant expression required");

    private NotImplementedException AnotherModulesConst(string name) => new(
        $"{array}'s bounds read {name}, another module's Public Const. A module-level array is sized when its module's " +
        "declarations run, and HexIDE does not read another module's Const there yet: unqualified it is not found, and " +
        "qualified it may not have been initialised (#880).");

    private NotImplementedException AnotherModulesEnumMember(string name) => new(
        $"{array}'s bounds read {name}, a member of another module's Enum. HexIDE does not read another module's Enum " +
        "member unqualified yet (#889). Qualify it with its Enum's name.");

    private NotImplementedException Unsupported(string text) => new(
        $"{array}'s bounds read {text}, which HexIDE does not read in a module-level array's bounds.");
}
