using Antlr4.Runtime.Tree;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// What a module-level Const's value and a module-level array's bounds may read: VB6's constant expressions, checked
/// as the module's declarations run, never in the pre-pass. VB6 works both out before the program starts, and refuses
/// to compile one it cannot. Every rule here was measured with <c>vb6.exe /make</c> (the oracle's "What a module-level
/// array's bound may name"):
/// <list type="bullet">
/// <item>a same-module Const or Enum declared above, an Enum member bare or as <c>Sizes.eMax</c>, a built-in constant
/// bare or qualified by its library or container (<c>vbKeyA</c>, <c>VBA.vbCrLf</c>, <c>KeyCodeConstants.vbKeyA</c>,
/// <c>VBRUN.KeyCodeConstants.vbKeyA</c>), and another module's Public Enum member qualified (<c>Cols.colMax</c>,
/// <c>Module2.colMax</c>, <c>Module2.Cols.colMax</c>). The project name in front is stepped over and the rest resolved
/// the same way: measured for <c>Probe.Module2.Cols.colMax</c> and <c>Probe.Module2.M + 1</c>, inferred for the rest;</item>
/// <item>in a bound, not in a Const, some built-in functions of constants: <c>Abs</c>, <c>CBool</c>, <c>CByte</c>,
/// <c>CCur</c>, <c>CDbl</c>, <c>CInt</c>, <c>CLng</c>, <c>CSng</c>, <c>CStr</c>, <c>CVar</c>, <c>Fix</c>, <c>Int</c>,
/// <c>Len</c>, <c>LenB</c> and <c>Sgn</c>, called bare. <c>Len</c> and <c>LenB</c> take only a String: of a number they
/// are "Variable required - can't assign to this expression". A Const refuses every function call, <c>Len</c>
/// included, and a bound refuses a qualified one, <c>VBA.Len("ab")</c> included.</item>
/// </list>
/// A Const or Enum below, the Const itself, a variable, a user function, another module's Public or Global variable,
/// and the built-ins measured as refused are "Constant expression required"; a name the module cannot see is "Variable
/// not defined" under Option Explicit, and the first otherwise; the module's own name as a qualifier is "Circular
/// dependencies between modules", whatever it names, a forward reference included. Another module's Public declaration
/// is found before a built-in constant of the same name: the project beats the library.
/// </summary>
/// <remarks>
/// <para>Each module-level Const is checked as it runs, before it is evaluated, and the Consts above it ran first, so a
/// Const a bound or another Const names has been checked already: the walk is one level deep. This relates symbols,
/// which the pre-pass must not do, so it runs with the declarations. The pre-pass only collects: each Const's
/// declaration and visibility, each Enum's position, and each Public variable.</para>
/// <para>Another module's Public Const is read bare or qualified, running its declarations on demand first (#893).
/// Re-entering a module still running its declarations is a circular dependency. A built-in
/// function nobody has measured in a bound, and a qualified name of a shape nobody has measured, are refused loudly
/// too, rather than guessed.</para>
/// <para>Procedure declarations reuse only the measured forward-local-Const check below (#894).</para>
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
    public static Task Check(BasicInterpreter interpreter, ModuleInfo module, ExecutionEnvironment env,
        VB6Parser.SubscriptsContext bounds, VB6Parser.VariableSubStmtContext dim)
        => new ModuleLevelBounds(interpreter, module, env, dim.ambiguousIdentifier().GetText() + "'s bounds", plural: true)
            .Walk(bounds, inConst: false, position: dim.Start.TokenIndex);

    /// <summary>
    /// A module-level Const's value, as the Const runs and before it is evaluated. Every Const above it was checked
    /// when it ran, so a same-module Const it names is a constant expression already, and the walk need go no deeper.
    /// </summary>
    public static Task CheckConst(BasicInterpreter interpreter, ModuleInfo module, ExecutionEnvironment env,
        VB6Parser.ConstSubStmtContext constant)
        => new ModuleLevelBounds(interpreter, module, env, "Const " + constant.ambiguousIdentifier().GetText() + "'s value", plural: false)
            .Walk(constant.valueStmt(), inConst: true, position: constant.Start.TokenIndex);

    // Procedure expressions have different measured rules from module expressions. Leave other names and calls
    // to today's evaluator; only a bare read of a later local Const is classified here (#894).
    public static void CheckProcedure(ModuleInfo module, ProcedureInfo procedure, IParseTree expression, int position, bool inConst, int depth = 0)
    {
        if (depth > 500)
            throw new NotImplementedException("Procedure constant expression is too deeply nested");
        if (expression is VB6Parser.ICS_S_MembersCallContext)
            return;
        if (expression is VB6Parser.ICS_S_VariableOrProcedureCallContext read)
        {
            var name = read.ambiguousIdentifier().GetText();
            foreach (var constant in procedure.DeclaredConsts)
                if (constant.Start.TokenIndex > position
                    && constant.ambiguousIdentifier().GetText().Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    if (module.PrePass.ModuleConsts.ContainsKey(name))
                    {
                        // Only the Const read under Option Explicit was measured with a module name to shadow.
                        if (inConst && module.PrePass.RequireVariableDefinitions)
                            throw new VBCompileErrorException("Duplicate declaration in current scope");
                        return;
                    }
                    throw new VBCompileErrorException(module.PrePass.RequireVariableDefinitions
                        ? "Variable not defined" : "Constant expression required");
                }
            return;
        }
        for (var i = 0; i < expression.ChildCount; i++)
            CheckProcedure(module, procedure, expression.GetChild(i), position, inConst, depth + 1);
    }

    // `position` is where the expression sits: a name it reads must be declared before it.
    private async Task Walk(IParseTree node, bool inConst, int position)
    {
        switch (node)
        {
            case VB6Parser.ICS_S_MembersCallContext members:
                await Qualified(members, position);
                return;
            case VB6Parser.ICS_S_VariableOrProcedureCallContext name:
                await Name(name.ambiguousIdentifier().GetText(), inConst, position);
                return;
            case VB6Parser.ICS_S_ProcedureOrArrayCallContext call when call.ambiguousIdentifier() is { } callee:
                Call(callee.GetText(), inConst);
                for (var i = 0; i < node.ChildCount; i++)
                    await Walk(node.GetChild(i), inConst, position);
                // Once its argument is known to be a constant: Len and LenB of a number, a numeric Const, a typed,
                // fractional or Date Const are each "Variable required" (measured), and of a String constant they size.
                if (callee.GetText().TrimEnd('$') is var function
                    && (function.Equals("Len", StringComparison.OrdinalIgnoreCase)
                        || function.Equals("LenB", StringComparison.OrdinalIgnoreCase)))
                    await RequireString(call, callee.GetText(), position);
                return;
        }
        for (var i = 0; i < node.ChildCount; i++)
            await Walk(node.GetChild(i), inConst, position);
    }

    // Returns the constant's value where it is known here, for Len's check; null where the walk approved it without one.
    private async Task<Vb6Value?> Name(string name, bool inConst, int position)
    {
        if (module.PrePass.ModuleConsts.TryGetValue(name, out var constant))
        {
            // A Const below, or the Const itself, is not yet a constant (measured: forward and self references). One
            // above was checked when it ran (CheckConst), so it is one.
            if (constant.Declaration.Start.TokenIndex >= position)
                throw ConstantExpressionRequired();
            return interpreter.ExecutionContext.TryGetVariable(env, name, out var value) ? (Vb6Value?)value : null;
        }
        foreach (var (enumName, members) in module.PrePass.Enums)
        {
            if (!members.TryGetValue(name, out var member))
                continue;
            // An Enum below is not yet a constant either (measured: `Const A = eLater + 1` above the Enum).
            if (module.PrePass.EnumDeclaredAt[enumName] >= position)
                throw ConstantExpressionRequired();
            return new Vb6Value(member);
        }
        if (module.PrePass.Procedures.ContainsKey(name) || module.PrePass.Properties.ContainsKey(name))
            throw ConstantExpressionRequired();
        if (env.TryGetOwnVariableLocation(name, out _))
            throw ConstantExpressionRequired();   // a variable of this module
        // The project before the library: measured, Module2's `Public Const vbKeyA = 3` sizes `Dim d(vbKeyA)` 0 To 3.
        // Another module's Public Const is a constant, read as the qualified one is (measured, #880: Module2's
        // `Public Const M = 3` makes Module1's `Const A = M + 1` 4 and `Dim d(M)` 0 To 3). Two modules naming it is
        // ambiguous, as a bare read anywhere is (ProjectNames).
        // Another module's Public Enum member is a constant too, a class's included (measured, #889: Module2's `colMax` makes
        // `Const K = colMax + 1` 3 and `Dim d(colMax)` 0 To 2, and Class1's `clsA = 40` sizes `Dim d(clsA)` 0 To 40).
        ModuleInfo? exporter = null;
        var isConst = false;
        long? exported = null;
        foreach (var other in interpreter.Modules.All)
        {
            if (ReferenceEquals(other, module))
                continue;
            long? theirMember = null;
            if (other.Kind is InterpreterModuleKind.Standard or InterpreterModuleKind.Class)
                foreach (var (enumName, members) in other.PrePass.Enums)
                    if (!other.PrePass.PrivateEnums.Contains(enumName) && members.TryGetValue(name, out var value))
                        theirMember = theirMember is null ? value : throw new VBCompileErrorException("Ambiguous name detected: " + name);
            var standard = other.Kind == InterpreterModuleKind.Standard;
            var theirConst = standard && other.PrePass.ModuleConsts.TryGetValue(name, out var theirs) && theirs.Public;
            if (theirMember is null && !theirConst && !(standard && other.PrePass.PublicVariables.Contains(name))
                && !(standard && other.PrePass.Procedures.TryGetValue(name, out var procedure) && !procedure.IsPrivate))
                continue;
            if (exporter is not null || theirMember is not null && (theirConst || standard && other.PrePass.PublicVariables.Contains(name)))
                throw new VBCompileErrorException("Ambiguous name detected: " + name);
            (exporter, isConst, exported) = (other, theirConst, theirMember);
        }
        if (exported is { } exportedValue)
            return new Vb6Value(exportedValue);
        if (exporter is not null)
            return isConst ? await ModuleConst(exporter, name) : throw ConstantExpressionRequired();
        if (interpreter.BuiltIns.TryGetBuiltInConstant(name, out var builtIn))
            return builtIn;
        if (VB6BuiltIns.IsBuiltInFunction(name))
        {
            Call(name, inConst);   // `Timer`, called without parentheses
            return null;
        }
        // A name the module cannot see: another module's Private Const or Enum, or nothing at all.
        if (module.PrePass.RequireVariableDefinitions)
            throw new VBVariableNotDefinedException(name);
        throw ConstantExpressionRequired();
    }

    // A dotted name, resolved one level at a time as the evaluator resolves it (ExpressionExecutor's qualified read),
    // so that what main read here it still reads: each level is a module, an Enum, a library, an in-box container or
    // the project.
    private async Task<Vb6Value?> Qualified(VB6Parser.ICS_S_MembersCallContext members, int position)
    {
        var text = members.GetText();
        var lead = text.Split('.')[0];
        if (lead.Contains('('))
            throw Unsupported(text);
        var parts = text.Split('.');
        if (parts.Length > 1 && parts[0].Equals(interpreter.ProjectName, StringComparison.OrdinalIgnoreCase)
            && !interpreter.Modules.TryGet(parts[0], out _) && !interpreter.TryResolveEnum(parts[0], module, out _))
        {
            // The project level is stepped over (measured: `Probe.Module2.Cols.colMax` is 4, `Probe.Module2.M + 1` 4).
            parts = parts[1..];
            if (parts.Length == 1)
                return parts[0].Contains('(') ? throw Unsupported(text) : await Name(parts[0], inConst: false, position);
        }
        // The module naming itself is a cycle (measured for a Const, an Enum member, `Module1.Sizes.eMax`, a variable,
        // a function, a name that does not exist, and a qualified forward reference, which is this error rather than
        // "Constant expression required").
        if (parts[0].Equals(module.Name, StringComparison.OrdinalIgnoreCase))
            throw new VBCompileErrorException("Circular dependencies between modules");
        // A call through a qualifier is no constant (measured: `VBA.Len("ab")` in a bound, though `Len("ab")` sizes).
        if (text.Contains('('))
            throw ConstantExpressionRequired();

        if (interpreter.Modules.TryGet(parts[0], out var other) && other.Kind == InterpreterModuleKind.Standard)
            return await ModuleMember(other, parts, text);
        if (interpreter.TryResolveEnum(parts[0], module, out var enumMembers))
        {
            if (parts.Length != 2 || !enumMembers.TryGetValue(parts[1], out var member))
                throw Unsupported(text);
            // The module's own Enum below is not yet a constant (measured: `Const C = Sizes.eMax` above the Enum).
            if (module.PrePass.EnumDeclaredAt.TryGetValue(parts[0], out var declaredAt) && declaredAt >= position)
                throw ConstantExpressionRequired();
            return new Vb6Value(member);
        }
        if (interpreter.TryResolveQualifier(parts[0], out var qualifier))
        {
            Vb6Value value;
            switch (qualifier.Kind)
            {
                case BasicInterpreter.QualifierKind.Library when parts.Length == 2
                        && VB6InBoxLibraries.TryInLibrary(qualifier.Name!, parts[1], out value):
                    return value;   // `VBA.vbCrLf`
                case BasicInterpreter.QualifierKind.Library when parts.Length == 3
                        && VB6InBoxLibraries.ContainerBelongsTo(qualifier.Name!, parts[1])
                        && VB6InBoxLibraries.TryInLibraryContainer(qualifier.Name!, parts[1], parts[2], out value):
                    return value;   // `VBRUN.KeyCodeConstants.vbKeyA`
                case BasicInterpreter.QualifierKind.InBoxContainer when parts.Length == 2:
                    // `KeyCodeConstants.vbKeyA`; a member of another container is refused (measured:
                    // `KeyCodeConstants.vbCancel` is "Method or data member not found").
                    return VB6InBoxLibraries.TryInContainer(qualifier.Name!, parts[1], out value)
                        ? value
                        : throw new VBCompileErrorException("Method or data member not found");
            }
        }
        throw Unsupported(text);
    }

    private async Task<Vb6Value?> ModuleMember(ModuleInfo other, string[] parts, string text)
    {
        if (parts.Length == 2)
        {
            var member = parts[1];
            if (other.PrePass.ModuleConsts.TryGetValue(member, out var theirs) && theirs.Public)
                return await ModuleConst(other, member);
            foreach (var (enumName, members) in other.PrePass.Enums)
            {
                if (!members.TryGetValue(member, out var value))
                    continue;
                // `Module2.colMax` is 4 (measured); a member of its Private Enum is not found (measured).
                if (other.PrePass.PrivateEnums.Contains(enumName))
                    throw new VBCompileErrorException("Method or data member not found");
                return new Vb6Value(value);
            }
            if (other.PrePass.PublicVariables.Contains(member))
                throw ConstantExpressionRequired();
        }
        // `Module2.Cols.colMax`, a Public Enum of that module (measured: 4).
        else if (parts.Length == 3 && other.PrePass.Enums.TryGetValue(parts[1], out var enumMembers)
                 && !other.PrePass.PrivateEnums.Contains(parts[1]) && enumMembers.TryGetValue(parts[2], out var enumValue))
            return new Vb6Value(enumValue);
        throw Unsupported(text);
    }

    // Another module's Public Const, bare or qualified: run its declarations first, once (#893).
    private async Task<Vb6Value?> ModuleConst(ModuleInfo other, string name)
    {
        await interpreter.RunModuleDeclarations(other);
        return interpreter.ExecutionContext.TryGetVariable(other.ModuleEnv, name, out var value) ? (Vb6Value?)value : null;
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

    private async Task RequireString(VB6Parser.ICS_S_ProcedureOrArrayCallContext call, string name, int position)
    {
        var argument = call.argsCall().Length == 1 && call.argsCall(0).argCall() is { Length: 1 } args
            ? args[0].valueStmt()
            : null;
        switch (argument is null ? null : await IsString(argument, position))
        {
            case true:
                return;
            case false:
                throw new VBCompileErrorException("Variable required - can't assign to this expression");
            default:
                throw new NotImplementedException(
                    $"{subject} {calls} {name} of {argument?.GetText()}. VB6 sizes an array from {name} only of a String " +
                    "constant, and HexIDE cannot tell whether this one is.");
        }
    }

    // Whether a constant expression is a String: true, false, or null where HexIDE cannot tell.
    private async Task<bool?> IsString(VB6Parser.ValueStmtContext value, int position) => value switch
    {
        VB6Parser.VsLiteralContext literal => literal.literal().STRINGLITERAL() != null ? true
            : literal.literal().INTEGERLITERAL() != null || literal.literal().DOUBLELITERAL() != null
              || literal.literal().DATELITERAL() != null ? false
            : null,
        VB6Parser.VsAmpContext => true,
        // A difference is not a sum: what it reads as is left unknown, as it was while `-` had an alternative of its own.
        VB6Parser.VsAddContext subtract when subtract.MINUS() != null => null,
        // A String added to a String is a String, and to a number it is not (measured: `Len("ab" + "c")` sizes 0 To 3,
        // and `Len("ab" + 1)` is "Variable required").
        VB6Parser.VsAddContext add => (await IsString(add.valueStmt(0), position), await IsString(add.valueStmt(1), position)) switch
        {
            (true, true) => true,
            (not null, not null) => false,
            _ => null,
        },
        VB6Parser.VsStructContext group when group.valueStmt().Length == 1 => await IsString(group.valueStmt(0), position),
        VB6Parser.VsICSContext ics => ics.implicitCallStmt_InStmt() switch
        {
            { } call when call.iCS_S_VariableOrProcedureCall() is { } name =>
                Known(await Name(name.ambiguousIdentifier().GetText(), inConst: false, position)),
            { } call when call.iCS_S_MembersCall() is { } members => Known(await Qualified(members, position)),
            // CStr gives a String (measured: `Len(CStr(123))` sizes 0 To 3). Len of another function's result was not
            // measured, so it is not decided here.
            { } call when call.iCS_S_ProcedureOrArrayCall()?.ambiguousIdentifier()?.GetText().TrimEnd('$') is { } function =>
                function.Equals("CStr", StringComparison.OrdinalIgnoreCase) ? true : null,
            _ => null,
        },
        _ => null,
    };

    private static bool? Known(Vb6Value? value) => value is { } v ? v.Type == Vb6Value.ValueType.String : null;

    private static VBCompileErrorException ConstantExpressionRequired() => new("Constant expression required");

    private NotImplementedException Unsupported(string text) => new(
        $"{subject} {reads} {text}, which HexIDE does not read in a module-level Const or array bound.");
}
