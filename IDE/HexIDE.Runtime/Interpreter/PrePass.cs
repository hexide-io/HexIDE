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
    // Interfaces this (class) module claims via `Implements IFoo`, in source order. Collecting the NAME is
    // a pre-pass lookup, not analysis: nothing here compares this module against the interface's members.
    // That comparison is the conformance check, and it runs at first instantiation, against two tables that
    // by then are both already built.
    public List<string> Implemented = new();
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

    public override object? VisitImplementsStmt(VB6Parser.ImplementsStmtContext context)
    {
        Implemented.Add(context.ambiguousIdentifier().GetText());
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
        // Enum members are Long compile-time CONSTANTS whose values are constant EXPRESSIONS, not merely
        // literals: `&H80000005` (measured -2147483643, the high bit making it negative), `&O17`, `-3`,
        // `xFirst + 1`, `2 ^ 3` and bit-ors of earlier members are all ordinary VB6. A member with no value
        // takes the previous member's value + 1, from any of those.
        //
        // Evaluation is a SINGLE FORWARD WALK, and that is measured rather than convenient: VB6 refuses a
        // member that references a later member, and one that references a later Const, both with
        // "Constant expression required". So source order is the rule, and the lazy-memoised treatment
        // CLAUDE.md prescribes for Const — which exists precisely because Const is order-independent — is
        // not wanted here. Collecting values as the walk reaches them stays pure collection.
        var enumName = context.ambiguousIdentifier().GetText();
        var members = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long next = 0;
        foreach (var c in context.enumerationStmt_Constant())
        {
            var constName = c.ambiguousIdentifier().GetText();
            long value;
            if (c.valueStmt() is { } vs)
            {
                if (!TryFoldEnumValue(vs, enumName, members, out value))
                    throw new VBCompileErrorException(
                        $"Constant expression required (Enum {enumName}, member '{constName}': '{vs.GetText().Trim()}')");
                next = value + 1;
            }
            else
            {
                value = next++;
            }
            members[constName] = value;

            // Hoisted READ-ONLY. VB6 answers `pTwo = 5` with "Assignment to constant not permitted", and a
            // plain variable slot silently accepted it — so a program could overwrite vbRed and nothing
            // would say so.
            var location = state.Alloc(new Vb6Value(value));
            state.MarkReadOnly(location);
            rootEnv.DefineVariable(constName, location);
        }
        Enums[enumName] = members;
        return default;
    }

    /// <summary>
    /// Fold one Enum member's value expression to the Long it denotes, or fail.
    ///
    /// <para>
    /// This is a CONSTANT folder, deliberately separate from <see cref="ExpressionExecutor"/>: an Enum body
    /// is evaluated before anything runs, so there is no environment to evaluate against and nothing here
    /// may have a side effect. VB6 draws the same line and reports crossing it as "Constant expression
    /// required". Anything this cannot fold is refused rather than guessed — a member is a value the whole
    /// program reads, so a wrong one would be wrong everywhere and silently.
    /// </para>
    ///
    /// <para>
    /// Names resolve against members already folded, which is the whole of the scoping rule: earlier
    /// members of this enum (bare or qualified with this enum's own name — both measured legal), and
    /// members of enums declared earlier. A forward reference simply is not found, which is the right
    /// answer for the right reason.
    /// </para>
    /// </summary>
    private bool TryFoldEnumValue(VB6Parser.ValueStmtContext ctx, string enumName,
        Dictionary<string, long> soFar, out long result)
    {
        result = 0;
        if (!TryFoldToDouble(ctx, enumName, soFar, out var d)) return false;
        if (double.IsNaN(d) || double.IsInfinity(d) || d < long.MinValue || d > long.MaxValue) return false;
        // A member is a Long, so the constant expression is COERCED to one — and VB6 coerces by rounding
        // half to EVEN, not by truncating. Measured: `7 / 2` is 4, `5 / 2` is 2, `-7 / 2` is -4. Getting
        // this wrong is silent, because every one of those still produces a plausible number.
        result = (long)Math.Round(d, MidpointRounding.ToEven);
        return true;
    }

    /// <summary>
    /// The fold itself, in double — because VB6 evaluates the member's expression and only then coerces to
    /// Long. Folding in integers instead makes `7 / 2` come out 3 rather than 4, which is a wrong value and
    /// looks entirely reasonable.
    /// </summary>
    private bool TryFoldToDouble(VB6Parser.ValueStmtContext ctx, string enumName,
        Dictionary<string, long> soFar, out double result)
    {
        result = 0;
        switch (ctx)
        {
            case VB6Parser.VsLiteralContext lit:
            {
                var l = lit.literal();
                Vb6Value v;
                if (l.HEXLITERAL() is { } hex) v = ExpressionExecutor.ClassifyRadixLiteral(hex.GetText(), 16);
                else if (l.OCTALLITERAL() is { } oct) v = ExpressionExecutor.ClassifyRadixLiteral(oct.GetText(), 8);
                else if (l.INTEGERLITERAL() is { } i) v = ExpressionExecutor.ClassifyIntegerLiteral(i.GetText());
                else if (l.DOUBLELITERAL() is { } dbl)
                    return double.TryParse(dbl.GetText().TrimEnd('#', '!', '&', '@', '%'),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out result);
                else if (l.TRUE() != null) { result = -1; return true; }     // VB6 True is -1
                else if (l.FALSE() != null) { result = 0; return true; }
                else return false;                                          // strings, dates, Nothing, Null
                return TryAsDouble(v, out result);
            }

            // A parenthesised value. `vsStruct` also covers a comma list, which is not a constant.
            case VB6Parser.VsStructContext s when s.valueStmt().Length == 1:
                return TryFoldToDouble(s.valueStmt(0), enumName, soFar, out result);

            case VB6Parser.VsNegationContext n when TryFoldToDouble(n.valueStmt(), enumName, soFar, out var neg):
                result = -neg; return true;
            case VB6Parser.VsPlusContext p:
                return TryFoldToDouble(p.valueStmt(), enumName, soFar, out result);

            // A NAME: an earlier member, bare or qualified.
            case VB6Parser.VsICSContext ics:
            {
                if (!TryResolveFoldedName(ics.GetText().Trim(), enumName, soFar, out var named)) return false;
                result = named;
                return true;
            }
        }

        // The binary operators, all of which need both sides folded first.
        var (left, right, op) = ctx switch
        {
            VB6Parser.VsAddContext a => (a.valueStmt(0), a.valueStmt(1), "+"),
            VB6Parser.VsMinusContext m => (m.valueStmt(0), m.valueStmt(1), "-"),
            VB6Parser.VsMultContext m => (m.valueStmt(0), m.valueStmt(1), "*"),
            // ONE token covers both `/` and `\`; the runtime tells them apart by its text and so must this.
            // Folding them alike makes `7 / 2` come out 3 where VB6 says 4.
            VB6Parser.VsDivContext d => (d.valueStmt(0), d.valueStmt(1), d.DIV().GetText() == "/" ? "/" : "\\"),
            VB6Parser.VsModContext m => (m.valueStmt(0), m.valueStmt(1), "Mod"),
            VB6Parser.VsPowContext p => (p.valueStmt(0), p.valueStmt(1), "^"),
            VB6Parser.VsAndContext a => (a.valueStmt(0), a.valueStmt(1), "And"),
            VB6Parser.VsOrContext o => (o.valueStmt(0), o.valueStmt(1), "Or"),
            VB6Parser.VsXorContext x => (x.valueStmt(0), x.valueStmt(1), "Xor"),
            _ => (null, null, null),
        };
        if (op is null) return false;
        if (!TryFoldToDouble(left!, enumName, soFar, out var lv)) return false;
        if (!TryFoldToDouble(right!, enumName, soFar, out var rv)) return false;

        switch (op)
        {
            case "+": result = lv + rv; return true;
            case "-": result = lv - rv; return true;
            case "*": result = lv * rv; return true;
            case "/": if (rv == 0) return false; result = lv / rv; return true;      // REAL division
            case "^": result = Math.Pow(lv, rv); return true;
            // `\` and `Mod` are integer operators: VB6 rounds each operand to a whole number first, then
            // works on those. `-7 \ 2` is -3, truncating toward zero after that rounding.
            case "\\":
            {
                var (li, ri) = (ToWhole(lv), ToWhole(rv));
                if (ri == 0) return false;
                result = li / ri;
                return true;
            }
            case "Mod":
            {
                var (li, ri) = (ToWhole(lv), ToWhole(rv));
                if (ri == 0) return false;
                result = li % ri;
                return true;
            }
            // Bitwise, not logical: `flagA Or flagB` is how every flag enum in VB6 is written.
            case "And": result = ToWhole(lv) & ToWhole(rv); return true;
            case "Or": result = ToWhole(lv) | ToWhole(rv); return true;
            case "Xor": result = ToWhole(lv) ^ ToWhole(rv); return true;
            default: return false;
        }
    }

    /// <summary>Round to a whole number the way VB6 does — half to EVEN, not away from zero.</summary>
    private static long ToWhole(double d) => (long)Math.Round(d, MidpointRounding.ToEven);

    /// <summary>Resolve a name inside an Enum member value: `Earlier`, `ThisEnum.Earlier`, `OtherEnum.Member`.</summary>
    private bool TryResolveFoldedName(string text, string enumName, Dictionary<string, long> soFar, out long result)
    {
        result = 0;
        var dot = text.LastIndexOf('.');
        if (dot < 0)
        {
            // Bare: this enum's earlier members first, then any earlier enum's members. Both are measured
            // legal; a name declared by two enums is ambiguous in VB6 and is left for the collision work.
            if (soFar.TryGetValue(text, out result)) return true;
            foreach (var (_, members) in Enums)
                if (members.TryGetValue(text, out result)) return true;
            return false;
        }

        var qualifier = text[..dot].Trim();
        var member = text[(dot + 1)..].Trim();
        // An enum's own name is in scope inside its own body — measured.
        var table = string.Equals(qualifier, enumName, StringComparison.OrdinalIgnoreCase)
            ? soFar
            : Enums.TryGetValue(qualifier, out var t) ? t : null;
        return table is not null && table.TryGetValue(member, out result);
    }

    /// <summary>A numeric literal's value, whatever width the classifier gave it.</summary>
    private static bool TryAsDouble(Vb6Value v, out double result)
    {
        switch (v.Value)
        {
            case long l: result = l; return true;
            case int i: result = i; return true;
            case short s: result = s; return true;
            case byte b: result = b; return true;
            case double d: result = d; return true;
            case float f: result = f; return true;
            case decimal m: result = (double)m; return true;
            default: result = 0; return false;
        }
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