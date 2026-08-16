using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

public class PrePass : VB6BaseVisitor<object?>
{
    private readonly ExecutionEnvironment rootEnv;
    private readonly ExecutionState state;
    public Dictionary<string, ProcedureInfo> Procedures = new(StringComparer.OrdinalIgnoreCase);
    // Class `Property Get/Let/Set` accessors, keyed by property name (each name owns up to three accessors).
    public Dictionary<string, PropertyInfo> Properties = new(StringComparer.OrdinalIgnoreCase);
    // Names this (class) module declares as `Public Event Foo(...)` — Phase 5. Recorded for completeness; dispatch
    // resolves by handler name, so the registry isn't strictly consulted (no pre-execution signature validation).
    public HashSet<string> Events = new(StringComparer.OrdinalIgnoreCase);
    // Module-level `WithEvents` variable names (event sinks) — a `Set` of one of these registers/unregisters an
    // event connection on the source object (Phase 5). `WithEvents` is class/form-only (oracle-verified).
    public HashSet<string> WithEventsNames = new(StringComparer.OrdinalIgnoreCase);
    public List<VB6Parser.BlockContext> topLevelBlocks = new();
    // User-defined Type definitions and Enum member tables declared in this module (aggregated program-wide by
    // the interpreter). Public by default — MVP does no Private-Type module-scoping.
    public Dictionary<string, UdtTypeDef> Types = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, long>> Enums = new(StringComparer.OrdinalIgnoreCase);
    public bool RequireVariableDefinitions { get; private set; }
    public int ArrayBase { get; private set; } = 0;

    public PrePass(ExecutionEnvironment rootEnv, ExecutionState state)
    {
        this.rootEnv = rootEnv;
        this.state = state;
    }

    public override object? VisitModuleBlock(VB6Parser.ModuleBlockContext context)
    {
        topLevelBlocks.Add(context.block());
        Visit(context.block());
        return default;
    }

    public override object? VisitOptionBaseStmt(VB6Parser.OptionBaseStmtContext context)
    {
        ArrayBase = int.Parse(context.INTEGERLITERAL().GetText());
        return default;
    }

    public override object? VisitOptionCompareStmt(VB6Parser.OptionCompareStmtContext context)
        // Accepted but not honoured — HexIDE always compares strings ordinally (Option Compare Binary). `Option
        // Compare Text` (case-insensitive) is a documented divergence (see docs/interpreter-gaps.md); the directive
        // must NOT fail the whole module load, so it is a no-op rather than a throw.
        => default;

    public override object? VisitOptionPrivateModuleStmt(VB6Parser.OptionPrivateModuleStmtContext context)
        // Accepted as a no-op — HexIDE doesn't enforce cross-project module-member visibility. Skipped (not thrown)
        // so a module carrying this directive still loads.
        => default;

    public override object? VisitOptionExplicitStmt(VB6Parser.OptionExplicitStmtContext context)
    {
        RequireVariableDefinitions = true;
        return default;
    }

    public override object? VisitVariableStmt(VB6Parser.VariableStmtContext context)
    {
        if (context.WITHEVENTS() != null)
        {
            // `[Private|Public] WithEvents src As Clock` (class/form-only) — hoist each as an object slot
            // initialised to Nothing and record the name as an event sink. The declared class (`As Clock`) is not
            // needed for dispatch (handlers resolve by name), so it's not validated.
            foreach (var sub in context.variableListStmt().variableSubStmt())
            {
                var name = sub.ambiguousIdentifier().GetText();
                WithEventsNames.Add(name);
                rootEnv.DefineVariable(name, state.Alloc(Vb6Value.Nothing));
            }
            return default;
        }

        // Module-level `Dim`, `Private`, and `Public` declarations all hoist the same way (visibility is not
        // tracked for variables — an over-permissive simplification). Class fields are usually `Private`/`Public`.
        if (context.DIM() != null || context.visibility() != null)
        {
            foreach (var subStmt in context.variableListStmt().variableSubStmt())
            {
                if (subStmt.typeHint() != null)
                    throw new NotImplementedException("DIM type hints not implemented");
                bool isArray = false;
                List<(int, int)>? dimensions = null;
                if (subStmt.LPAREN() != null && subStmt.RPAREN() != null) // array
                {
                    isArray = true;
                    if (subStmt.subscripts() != null)
                    {
                        dimensions = new List<(int, int)>();
                        int arrayLowerBound;
                        int arrayUpperBound;
                        foreach (var dimension in subStmt.subscripts().subscript())
                        {
                            var size = dimension.valueStmt();
                            if (size.Length == 2)
                            {
                                arrayLowerBound = int.Parse(size[0].GetText());
                                arrayUpperBound = int.Parse(size[1].GetText());
                            }
                            else if (size.Length == 1)
                            {
                                arrayLowerBound = ArrayBase;
                                arrayUpperBound = int.Parse(size[0].GetText());
                            }
                            else
                                throw new VBCompileErrorException("Either specify upper bound or lower and upper bound");
                            dimensions.Add((arrayLowerBound, arrayUpperBound));
                        }
                    }
                }

                Vb6Value.ValueType type = Vb6Value.ValueType.EmptyVariant;
                if (subStmt.asTypeClause() != null)
                {
                    if (subStmt.asTypeClause().NEW() != null)
                        throw new NotImplementedException("New as type not implemented");
                    if (subStmt.asTypeClause().fieldLength() != null)
                        throw new NotImplementedException("fieldLength as type not implemented");
                    if (subStmt.asTypeClause().type().complexType() != null)
                    {
                        // A UDT/Enum-typed Dim: hoist a placeholder here; the runtime VisitVariableStmt builds
                        // the real UDT instance (or an Enum-typed Long) once the program-wide type table exists.
                        // PrePass can't — it runs while the type table is still being assembled.
                        type = Vb6Value.ValueType.EmptyVariant;
                    }
                    else
                    {
                        type = BaseTypeMapper.Map(subStmt.asTypeClause().type().baseType())
                            ?? throw new NotImplementedException("base type " + subStmt.asTypeClause().type().baseType().GetChild(0) + " not implemented");
                    }
                }
                if (isArray)
                    type = new Vb6Value.ValueType(type, true);

                var value = dimensions != null ? new Vb6Value(type, dimensions) : new Vb6Value(type);
                if (dimensions != null && value.Value is VBArray fixedArr) fixedArr.IsDynamic = false;   // `Dim a(N)` = fixed
                var location = state.Alloc(value);
                rootEnv.DefineVariable(subStmt.ambiguousIdentifier().GetText(), location);
            }
        }
        else
            throw new NotImplementedException("non dim variables not supported");

        return default;
    }

    public override object? VisitConstStmt(VB6Parser.ConstStmtContext context)
    {
        // Hoist module-level const names into rootEnv (mirrors the Dim hoist) so a Sub declared after the
        // Const can reference it. The value is filled by the runtime VisitConstStmt (PrePass can't evaluate
        // the initializer). Sub/Function bodies aren't visited here, so local consts stay out of rootEnv.
        foreach (var sub in context.constSubStmt())
        {
            if (sub.typeHint() != null)
                throw new NotImplementedException("Const type hints not supported");
            var location = state.Alloc(Vb6Value.Variant);
            rootEnv.DefineVariable(sub.ambiguousIdentifier().GetText(), location);
        }
        return default;
    }

    public override object? VisitDeclareStmt(VB6Parser.DeclareStmtContext context)
        // `Declare Sub/Function … Lib` (Win32/DLL API) is a documented deferral (docs/interpreter-gaps.md). Rather
        // than fail the whole module load, skip the declaration — the name is simply not registered, so an actual
        // CALL to it raises the clean "Sub or Function not defined" error. This lets a module that merely declares
        // an API (a near-universal pattern in real VB6) load and run its non-API code.
        => default;

    public override object? VisitEnumerationStmt(VB6Parser.EnumerationStmtContext context)
    {
        // Enum members are Long compile-time constants: an explicit integer literal, else the previous value + 1
        // (auto-increment from 0). Each member name is hoisted as a bare Long (unqualified access, like a Const),
        // and the whole set is registered for qualified `MyEnum.Member` access. Non-literal member values
        // (hex, references to other members) are deferred.
        var enumName = context.ambiguousIdentifier().GetText();
        var members = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long next = 0;
        foreach (var c in context.enumerationStmt_Constant())
        {
            var constName = c.ambiguousIdentifier().GetText();
            long value;
            if (c.valueStmt() is { } vs)
            {
                if (!long.TryParse(vs.GetText().Trim(), out value))
                    throw new NotImplementedException($"Enum member '{constName}' must be an integer literal (got '{vs.GetText()}')");
                next = value + 1;
            }
            else
            {
                value = next++;
            }
            members[constName] = value;
            rootEnv.DefineVariable(constName, state.Alloc(new Vb6Value(value)));
        }
        Enums[enumName] = members;
        return default;
    }

    public override object? VisitEventStmt(VB6Parser.EventStmtContext context)
    {
        // `[Public] Event Foo(args)` — record the name. Dispatch is by handler name ({sink}_Foo), so the event's
        // own signature isn't consulted at runtime (no pre-execution signature check — CST-only).
        Events.Add(context.ambiguousIdentifier().GetText());
        return default;
    }

    public override object? VisitMacroIfThenElseStmt(VB6Parser.MacroIfThenElseStmtContext context)
        // Conditional compilation (`#If`/`#ElseIf`/`#Else`/`#End If`, `#Const`) is a documented deferral: selecting a
        // branch needs a preprocessor pass HexIDE doesn't have. Fail with a CLEAR compile error rather than a raw
        // NotImplementedException so the reason is legible (both branches can't be compiled blind).
        => throw new VBCompileErrorException("Conditional compilation (#If / #Const) is not supported");

    public override object? VisitPropertyGetStmt(VB6Parser.PropertyGetStmtContext context)
    {
        // `Property Get Name() As T` — Function-like: returns via its own name. A parameterized getter
        // (`Property Get Item(i)`) is a wall, so the index argList must be empty. A complexType return
        // (`As Node`) carries its concrete name so the return slot seeds a real UDT / Nothing (see RunProcedure).
        var name = context.ambiguousIdentifier().GetText();
        if (context.typeHint() != null)
            throw new NotImplementedException("Type-hinted Property Get is not supported");
        var args = ParseParams(context.argList());
        if (args.Count != 0)
            throw new NotImplementedException($"Parameterized Property Get '{name}' is not supported");
        var returnUdt = context.asTypeClause()?.type()?.complexType()?.GetText();
        var returnType = returnUdt != null
            ? Vb6Value.ValueType.UserDefinedType
            : ExtractBaseType(context.asTypeClause()) ?? Vb6Value.ValueType.EmptyVariant;
        GetOrAddProperty(name).Get = new ProcedureInfo(name, true, args, returnType, context.block(),
            IsPrivate(context.visibility()), returnUdt);
        return default;
    }

    public override object? VisitPropertyLetStmt(VB6Parser.PropertyLetStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        GetOrAddProperty(name).Let = ParsePropertyMutator("Let", name, context.argList(), context.block(), context.visibility());
        return default;
    }

    public override object? VisitPropertySetStmt(VB6Parser.PropertySetStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        GetOrAddProperty(name).Set = ParsePropertyMutator("Set", name, context.argList(), context.block(), context.visibility());
        return default;
    }

    private PropertyInfo GetOrAddProperty(string name)
    {
        if (!Properties.TryGetValue(name, out var prop))
            Properties[name] = prop = new PropertyInfo { Name = name };
        return prop;
    }

    // A `Property Let`/`Property Set` accessor is Sub-like: its LAST (here, only) parameter receives the
    // assigned value, coerced to that parameter's declared type. Exactly one parameter is required —
    // parameterized properties (an index before the value) are a wall.
    private ProcedureInfo ParsePropertyMutator(string kind, string name, VB6Parser.ArgListContext? argList,
        VB6Parser.BlockContext? block, VB6Parser.VisibilityContext? visibility)
    {
        var args = ParseParams(argList);
        if (args.Count != 1)
            throw new NotImplementedException(
                $"Property {kind} '{name}' must take exactly one (value) parameter — parameterized properties are not supported");
        return new ProcedureInfo(name, false, args, Vb6Value.ValueType.EmptyVariant, block, IsPrivate(visibility));
    }

    public override object? VisitTypeStmt(VB6Parser.TypeStmtContext context)
    {
        // A `Type … End Type` definition: parse each field to a scalar ValueType or a nested-type NAME (resolved
        // — UDT vs Enum — at instantiation, once the program-wide tables exist). Array fields and fixed-length
        // strings inside a Type are deferred (spec walls).
        var name = context.ambiguousIdentifier().GetText();
        var fields = new List<UdtField>();
        foreach (var el in context.typeStmt_Element())
        {
            var fieldName = el.ambiguousIdentifier().GetText();
            if (el.LPAREN() != null)
                throw new NotImplementedException($"Array field '{fieldName}' inside Type '{name}' is not yet supported");

            var asType = el.asTypeClause();
            if (asType == null)
            {
                fields.Add(new UdtField(fieldName, Vb6Value.ValueType.EmptyVariant, null));   // untyped -> Variant
                continue;
            }
            if (asType.fieldLength() != null)
                throw new NotImplementedException($"Fixed-length field '{fieldName}' inside Type '{name}' is not yet supported");
            if (asType.type().complexType() is { } ct)
                fields.Add(new UdtField(fieldName, null, ct.GetText()));   // nested UDT or Enum, resolved at NewUdt
            else
                fields.Add(new UdtField(fieldName,
                    BaseTypeMapper.Map(asType.type().baseType())
                        ?? throw new NotImplementedException($"Unsupported field type for '{fieldName}' in Type '{name}'"),
                    null));
        }
        Types[name] = new UdtTypeDef(name, fields);
        return default;
    }

    public override object? VisitFunctionStmt(VB6Parser.FunctionStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        // A complexType return (`As Employee`) is the generic UDT ValueType here; the concrete name is carried
        // separately so the return slot can be seeded with a real UDT at call time.
        var returnUdt = context.asTypeClause()?.type()?.complexType()?.GetText();
        var returnType = returnUdt != null
            ? Vb6Value.ValueType.UserDefinedType
            : ExtractBaseType(context.asTypeClause()) ?? Vb6Value.ValueType.EmptyVariant;
        Procedures[name] = new ProcedureInfo(name, true, ParseParams(context.argList()), returnType,
            context.block(), IsPrivate(context.visibility()), returnUdt);
        return default;
    }

    public override object? VisitSubStmt(VB6Parser.SubStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        Procedures[name] = new ProcedureInfo(name, false, ParseParams(context.argList()),
            Vb6Value.ValueType.EmptyVariant, context.block(), IsPrivate(context.visibility()));
        return default;
    }

    // A module-level procedure is Public unless explicitly `Private`. `Friend` counts as Public (no external
    // clients to hide from). Absent visibility ⇒ Public.
    private static bool IsPrivate(VB6Parser.VisibilityContext? visibility) => visibility?.PRIVATE() != null;

    private static List<ParamInfo> ParseParams(VB6Parser.ArgListContext? argList)
    {
        var result = new List<ParamInfo>();
        if (argList == null)
            return result;
        foreach (var arg in argList.arg())
        {
            result.Add(new ParamInfo(
                arg.ambiguousIdentifier().GetText(),
                ByRef: arg.BYVAL() == null,        // VB6's default passing convention is ByRef
                Optional: arg.OPTIONAL() != null,
                ParamArray: arg.PARAMARRAY() != null,
                DeclaredType: ExtractBaseType(arg.asTypeClause()),
                Default: arg.argDefaultValue()));
        }
        return result;
    }

    // Maps an `As <type>` clause to a base ValueType, or null when absent / complex / unknown (params and
    // return types are left untyped in that case for now — the wider type system is a later phase).
    private static Vb6Value.ValueType? ExtractBaseType(VB6Parser.AsTypeClauseContext? asType)
        => BaseTypeMapper.Map(asType?.type()?.baseType());
}