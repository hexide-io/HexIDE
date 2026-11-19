using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

public class PrePass : VB6BaseVisitor<object?>
{
    public enum DefaultMemberKind { Function, Property, Variable, Sub }
    public sealed record DefaultMemberInfo(string Name, DefaultMemberKind Kind);
    public DefaultMemberInfo? DefaultMember { get; private set; }
    internal DefaultMemberInfo? EnumerationMember { get; private set; }

    private readonly ExecutionEnvironment rootEnv;
    private readonly ExecutionState state;
    private readonly bool allowIndexedLet;
    private readonly bool allowIndexedSet;
    public Dictionary<string, ProcedureInfo> Procedures = new(StringComparer.OrdinalIgnoreCase);
    internal HashSet<string> DeclareSubs { get; } = new(StringComparer.OrdinalIgnoreCase);
    // `Property Get/Let/Set` accessors, keyed by property name (each name owns up to three accessors).
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
    internal List<Antlr4.Runtime.ParserRuleContext> DeclarationItems { get; } = new();

    /// <summary>True for a class or a form: an object module, where VB6 refuses some Public members outright.</summary>
    public bool ObjectModule { get; init; }

    public VbaBitness VbaBitness { get; init; }

    internal const string PublicMemberOfObjectModule =
        "Constants, fixed-length strings, arrays, user-defined types and Declare statements not allowed as Public members of object modules";
    // Each module-level Const by name: its declaration, and whether another module can see it (Public or Global; one
    // with no visibility is Private, measured). Collected for the allocation site of a module-level array, which
    // classifies what a bound reads, through the Consts it names (ModuleLevelBounds.Check and CheckConst).
    public Dictionary<string, (VB6Parser.ConstSubStmtContext Declaration, bool Public)> ModuleConsts =
        new(StringComparer.OrdinalIgnoreCase);
    // Module-level variables declared Public, for the same classification: another module's Public variable in a bound
    // is VB6's "Constant expression required", where a name it cannot see is "Variable not defined" (measured).
    public HashSet<string> PublicVariables = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> PrivateVariables = new(StringComparer.OrdinalIgnoreCase);
    // Module-level arrays whose bounds are not plain integers. They are sized only when the module's declarations
    // run, which a form's run does not do yet (#256), so that run refuses them rather than leave them unallocated.
    public List<string> DeferredArrays = new();
    // User-defined Type definitions and Enum member tables declared in this module (aggregated program-wide by
    // the interpreter). Public by default — MVP does no Private-Type module-scoping.
    public Dictionary<string, UdtTypeDef> Types = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, long>> Enums = new(StringComparer.OrdinalIgnoreCase);

    // Project Const values are read when declarations execute; retain names before their values exist (#4614).
    internal Dictionary<string, (VB6Parser.EnumerationStmtContext Statement, int First)> DeferredEnums { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, VB6Parser.EnumerationStmtContext> enumStatements = new(StringComparer.OrdinalIgnoreCase);

    internal IEnumerable<string> EnumMemberNames(string enumName)
    {
        foreach (var member in enumStatements[enumName].enumerationStmt_Constant())
            yield return member.ambiguousIdentifier().GetText();
    }

    internal bool HasPendingEnumMember(string name) => DeferredEnums.Values.Any(p =>
        p.Statement.enumerationStmt_Constant().Skip(p.First).Any(m => m.ambiguousIdentifier().GetText().Equals(name, StringComparison.OrdinalIgnoreCase)));

    internal void DeferEnum(string enumName, string memberName)
    {
        var statement = enumStatements[enumName];
        var members = statement.enumerationStmt_Constant();
        var first = Array.FindIndex(members, m => m.ambiguousIdentifier().GetText().Equals(memberName, StringComparison.OrdinalIgnoreCase));
        if (DeferredEnums.TryGetValue(enumName, out var earlier) && earlier.First <= first)
            return;
        DeferredEnums[enumName] = (statement, first);
        for (var i = first; i < members.Length; i++)
        {
            var name = members[i].ambiguousIdentifier().GetText();
            Enums[enumName].Remove(name);
            var location = state.Alloc(default(Vb6Value));
            state.MarkConstant(location);
            rootEnv.DefineVariable(name, location);
        }
        if (!DeclarationItems.Contains(statement))
        {
            var index = DeclarationItems.FindIndex(item => item.Start.TokenIndex > statement.Start.TokenIndex);
            DeclarationItems.Insert(index < 0 ? DeclarationItems.Count : index, statement);
        }
    }

    /// <summary>Where each Enum is declared, so that a module-level Const or bound above it can be refused, as VB6 refuses
    /// it (measured: "Constant expression required"). Collected, not related: the check runs with the declarations.</summary>
    public Dictionary<string, int> EnumDeclaredAt = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Enum names this module declared <c>Private</c> — invisible outside it, members included.
    /// Measured: a bare member of a foreign Private enum is "Variable not defined".</summary>
    public HashSet<string> PrivateEnums = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bare member names this module's enums declare AMBIGUOUSLY — the same name from two different
    /// enums. VB6 accepts the declarations and refuses the bare USE with "Ambiguous name detected", so the
    /// name is recorded here and the complaint is made when something reads it.</summary>
    public HashSet<string> AmbiguousEnumMembers = new(StringComparer.OrdinalIgnoreCase);

    // Every name this module declares at module level, so that a second declaration of one is refused as vb6.exe refuses
    // it, "Ambiguous name detected", naming the second (measured, #1622). Variables, Consts, Declares, Subs, Functions,
    // Properties and Enum members share one namespace, in any case and whatever their visibility; Enum and Type names share
    // another, and Events a third, so `Dim E` beside `Enum E` and `Event K` beside `Sub K` compile. Two exceptions within the
    // first: a Property's Get, Let and Set share its name, each once, and two Enums' members may share one, which is
    // ambiguous only where it is read (AmbiguousEnumMembers). Collected, not related: each name meets only the names before
    // it in the same module.
    internal enum ModuleNameKind { Other, Variable, Const }
    internal bool HasOtherModuleName(string name) => moduleNames.TryGetValue(name, out var entry)
        && entry.Kind == ModuleNameKind.Other && entry.Enums is null && entry.Exported;
    internal ModuleNameKind? ModuleName(string name) => moduleNames.TryGetValue(name, out var entry) ? entry.Kind : null;

    internal sealed record EnumLibraryName(string Enum, string Member, string Name, bool Qualified, Exception? Refusal);
    internal List<EnumLibraryName> EnumLibraryNames { get; } = new();
    private string foldingMember = "";

    private readonly Dictionary<string, (HashSet<string>? Enums, HashSet<string>? Accessors, ModuleNameKind Kind, bool Exported)> moduleNames =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> moduleTypeNames = new(StringComparer.OrdinalIgnoreCase);

    private void DeclareModuleName(string name, string? inEnum = null, string? accessor = null, ModuleNameKind kind = ModuleNameKind.Other, bool exported = true)
    {
        if (!moduleNames.TryGetValue(name, out var earlier))
            moduleNames[name] = (inEnum is null ? null : new(StringComparer.OrdinalIgnoreCase) { inEnum },
                                 accessor is null ? null : new(StringComparer.Ordinal) { accessor }, kind, exported);
        else if (!(inEnum is not null && earlier.Enums?.Add(inEnum) == true)
                 && !(accessor is not null && earlier.Accessors?.Add(accessor) == true))
            throw new VBCompileErrorException("Ambiguous name detected: " + name);
        else if (exported && !earlier.Exported)
            moduleNames[name] = (earlier.Enums, earlier.Accessors, earlier.Kind, true);
    }

    private void DeclareModuleTypeName(string name)
    {
        if (!moduleTypeNames.Add(name))
            throw new VBCompileErrorException("Ambiguous name detected: " + name);
    }

    // A name declared twice in one procedure, a parameter among them, is "Duplicate declaration in current scope", and a Dim
    // inside an If is the procedure's as much as one at the top (measured, #1499). A Function's or a Property Get's own name
    // is one of them, since it holds the return; a Sub's is not (measured: `Dim S` in `Sub S` compiles). Refused as the
    // module loads, as vb6.exe refuses the build; the second declaration was skipped, so the first one's type won, and a Dim
    // beside a Static made an ordinary local.
    private static ProcedureInfo CheckedLocals(ProcedureInfo procedure)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (procedure.IsFunction)
            names.Add(procedure.Name);
        foreach (var parameter in procedure.Parameters)
            Declare(parameter.Name);
        foreach (var constant in procedure.DeclaredConsts)
            Declare(constant.ambiguousIdentifier().GetText());
        foreach (var local in procedure.DeclaredLocals)
            Declare(local.ambiguousIdentifier().GetText());
        // A bare Cls: is a label, including VB6's duplicate-declaration check (#988).
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (procedure.Body is { } body)
            CheckLabels(body);
        return procedure;

        void CheckLabels(Antlr4.Runtime.Tree.IParseTree tree)
        {
            var pending = new Stack<Antlr4.Runtime.Tree.IParseTree>();
            pending.Push(tree);
            while (pending.TryPop(out var node))
            {
                if (node is VB6Parser.LineLabelContext label && !labels.Add(label.labelName().GetText()))
                    throw new VBCompileErrorException("Duplicate declaration in current scope");
                for (var i = 0; i < node.ChildCount; i++)
                    pending.Push(node.GetChild(i));
            }
        }

        void Declare(string name)
        {
            if (!names.Add(name))
                throw new VBCompileErrorException("Duplicate declaration in current scope");
        }
    }

    /// <summary>Is this declaration <c>Private</c>? Absent visibility means Public for a Type and an Enum.</summary>
    internal static bool IsPrivateVisibility(string? text)
        => text is not null && text.Trim().Equals("Private", StringComparison.OrdinalIgnoreCase);
    public bool RequireVariableDefinitions { get; private set; }

    // The types DefType gives by first letter (#169), A to Z; null where no DefType covers the letter. DefVar's letters hold
    // EmptyVariant, so a second directive over them is still refused.
    private readonly Vb6Value.ValueType?[] defaultTypes = new Vb6Value.ValueType?[26];

    // Set at the module's first declaration, after which a DefType does not compile: a variable, a Const, a Type, an Enum or a
    // Declare (all measured). An Option statement or another DefType does not count.
    private bool declared;

    /// <summary>
    /// The type a declaration with no As clause takes from the first letter of its name, under the module's DefType
    /// statements (#169): an undeclared variable, a <c>Dim</c> or <c>Static</c>, a field, a parameter, and a Function's or
    /// Property Get's return, but not a <c>Const</c>, which takes its value's type (all measured). Null for a Variant's.
    /// </summary>
    public Vb6Value.ValueType? DefaultTypeOf(string name)
    {
        var letter = name.Length > 0 ? char.ToUpperInvariant(name[0]) : '\0';
        return letter is >= 'A' and <= 'Z' && defaultTypes[letter - 'A'] is { } type && type != Vb6Value.ValueType.EmptyVariant
            ? type
            : null;
    }

    /// <summary>
    /// <c>DefInt A-Z</c> and its family (#169), collected into the letter table: no relationship between symbols, only
    /// which letters a module's undeclared and untyped names take which type from. VB6's compile errors, measured: a letter
    /// two directives cover is "Duplicate Deftype statement", even under the same type; a DefType after a variable
    /// declaration is "Deftype statements must precede declarations"; a name longer than a letter is "Expected: letter";
    /// and DefDec, which VB6 does not have, is not a statement at all. A range written backwards, <c>DefStr Z-X</c>,
    /// covers X to Z.
    /// </summary>
    public override object? VisitDeftypeStmt(VB6Parser.DeftypeStmtContext context)
    {
        if (context.DEFLNGLNG() != null && VbaBitness != VbaBitness.Bit64
            || context.DEFLNGPTR() != null && VbaBitness == VbaBitness.None)
            throw new VBCompileErrorException("Invalid outside procedure");
        if (declared)
            throw new VBCompileErrorException("Deftype statements must precede declarations");
        var type = context.DEFBOOL() != null ? Vb6Value.ValueType.Boolean
            : context.DEFBYTE() != null ? Vb6Value.ValueType.Byte
            : context.DEFINT() != null ? Vb6Value.ValueType.Integer
            : context.DEFLNGPTR() != null && VbaBitness == VbaBitness.Bit32 ? Vb6Value.ValueType.Long
            : context.DEFLNGLNG() != null || context.DEFLNGPTR() != null ? Vb6Value.ValueType.LongLong
            : context.DEFLNG() != null ? Vb6Value.ValueType.Long
            : context.DEFCUR() != null ? Vb6Value.ValueType.Currency
            : context.DEFSNG() != null ? Vb6Value.ValueType.Single
            : context.DEFDBL() != null ? Vb6Value.ValueType.Double
            : context.DEFDATE() != null ? Vb6Value.ValueType.Date
            : context.DEFSTR() != null ? Vb6Value.ValueType.String
            : context.DEFOBJ() != null ? Vb6Value.ValueType.Object
            : context.DEFVAR() != null ? Vb6Value.ValueType.EmptyVariant
            : throw new VBCompileErrorException("Expected: line number or label or statement or end of statement");
        foreach (var range in context.letterrange())
        {
            var letters = range.certainIdentifier();
            var first = Letter(letters[0]);
            var last = letters.Length > 1 ? Letter(letters[1]) : first;
            if (first > last)
                (first, last) = (last, first);
            for (var letter = first; letter <= last; letter++)
            {
                if (defaultTypes[letter - 'A'] is not null)
                    throw new VBCompileErrorException("Duplicate Deftype statement");
                defaultTypes[letter - 'A'] = type;
            }
        }
        return default;
    }

    private static char Letter(VB6Parser.CertainIdentifierContext letter) =>
        letter.GetText() is { Length: 1 } text && char.IsAsciiLetter(text[0])
            ? char.ToUpperInvariant(text[0])
            : throw new VBCompileErrorException("Expected: letter");
    public int ArrayBase { get; private set; } = 0;

    /// <summary>Whether the module says <c>Option Compare Text</c>; Binary otherwise, as VB6 defaults.</summary>
    public bool OptionCompareText { get; private set; }

    /// <summary>
    /// Whether a procedure here carries <c>Attribute Name.VB_UserMemId = 0</c>, or a module-level variable
    /// <c>Attribute Name.VB_VarUserMemId = 0</c>, which makes it the class's default member. VB6 writes the attribute
    /// into whichever of a property's procedures comes first, the Let or Set as readily as the Get. Collected only:
    /// the name and kind are retained for the async resolver (#174). Execution still refuses a value read rather than
    /// read 0 or raise a trappable 438.
    /// </summary>
    public bool DeclaresDefaultMember => DefaultMember is not null;

    private void NoteDefaultMember(Antlr4.Runtime.Tree.IParseTree? body, DefaultMemberKind kind, bool enumeration = false)
    {
        if (body is null || body is VB6Parser.ValueStmtContext)
            return;
        if (body is VB6Parser.AttributeStmtContext attribute
            && attribute.implicitCallStmt_InStmt().GetText() is var name
            && (name.EndsWith(".VB_UserMemId", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".VB_VarUserMemId", StringComparison.OrdinalIgnoreCase))
            && attribute.signedLiteral().Length == 1)
        {
            var member = new DefaultMemberInfo(name[..name.LastIndexOf('.')], kind);
            if (attribute.signedLiteral(0).GetText() == "0")
                DefaultMember ??= member;
            else if (attribute.signedLiteral(0).GetText() == "-4"
                     && enumeration
                     && name.EndsWith(".VB_UserMemId", StringComparison.OrdinalIgnoreCase))
                EnumerationMember ??= member;
            return;
        }
        for (var i = 0; i < body.ChildCount; i++)
            NoteDefaultMember(body.GetChild(i), kind, enumeration);
    }

    public PrePass(ExecutionEnvironment rootEnv, ExecutionState state, bool allowIndexedLet = false, bool allowIndexedSet = false)
    {
        this.rootEnv = rootEnv;
        this.state = state;
        this.allowIndexedLet = allowIndexedLet;
        this.allowIndexedSet = allowIndexedSet;
    }

    public override object? VisitChildren(Antlr4.Runtime.Tree.IRuleNode node)
    {
        // Expressions contain no declarations, and a flat operator chain can be arbitrarily deep.
        return node is VB6Parser.ValueStmtContext ? null : base.VisitChildren(node);
    }

    public bool PredeclaredId { get; private set; }

    public override object? VisitModuleAttributes(VB6Parser.ModuleAttributesContext context)
    {
        foreach (var attribute in context.attributeStmt())
            if (attribute.implicitCallStmt_InStmt().GetText().Equals("VB_PredeclaredId", StringComparison.OrdinalIgnoreCase))
                PredeclaredId = attribute.signedLiteral(0).GetText().Equals("True", StringComparison.OrdinalIgnoreCase);
        return base.VisitModuleAttributes(context);
    }

    public override object? VisitModuleBlock(VB6Parser.ModuleBlockContext context)
    {
        topLevelBlocks.Add(context.block());
        DeclarationItems.Add(context.block());
        Visit(context.block());
        NoteDefaultMember(context.block(), DefaultMemberKind.Variable);   // a Public variable's VB_VarUserMemId follows it here
        return default;
    }

    public override object? VisitOptionBaseStmt(VB6Parser.OptionBaseStmtContext context)
    {
        ArrayBase = int.Parse(context.INTEGERLITERAL().GetText(), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture);
        return default;
    }

    public override object? VisitOptionCompareStmt(VB6Parser.OptionCompareStmtContext context)
    {
        // Recorded per module for comparisons, Like and intrinsic calls written in that module.
        OptionCompareText = context.TEXT() != null;
        return default;
    }

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
        foreach (var subStmt in context.variableListStmt().variableSubStmt())
        {
            if (subStmt.WITHEVENTS() == null)
                continue;
            // Only this declarator is an event sink; the declared class is not validated here.
            var name = subStmt.ambiguousIdentifier().GetText();
            DeclareModuleName(name, kind: ModuleNameKind.Variable);
            WithEventsNames.Add(name);
            rootEnv.DefineVariable(name, state.Alloc(Vb6Value.Nothing));
        }
        if (context.variableListStmt().variableSubStmt().All(sub => sub.WITHEVENTS() != null))
            return default;

        // Module-level `Dim`, `Private`, and `Public` declarations all hoist the same way (visibility is not
        // tracked for variables — an over-permissive simplification). Class fields are usually `Private`/`Public`.
        // The slot hoisted here has no DefType's type: the Dim statement, run when the module starts, makes the slot the
        // program uses, and DeclareLocal gives that one its type (#169).
        declared = true;
        if (context.DIM() != null || context.visibility() != null)
        {
            foreach (var subStmt in context.variableListStmt().variableSubStmt())
            {
                if (subStmt.WITHEVENTS() != null)
                    continue;
                DeclareModuleName(subStmt.ambiguousIdentifier().GetText(), kind: ModuleNameKind.Variable);
                // `Dim s$` is `Dim s As String`, typed when the Dim runs (DeclareLocal); with an As clause as well it is
                // VB6's compile error (measured, #742).
                if (subStmt.typeHint() != null && subStmt.asTypeClause() != null)
                    throw new VBCompileErrorException("Expected: end of statement");
                if (ObjectModule && context.visibility()?.PUBLIC() != null)
                {
                    if (subStmt.asTypeClause()?.fieldLength() != null)
                        throw new VBCompileErrorException(PublicMemberOfObjectModule);
                }
                bool isArray = false;
                List<(int, int)>? dimensions = null;
                if (subStmt.LPAREN() != null && subStmt.RPAREN() != null) // array
                {
                    // A Public array in a class or a form, fixed or dynamic, is refused when the program loads, as vb6.exe
                    // refuses it at /make and the IDE before Sub Main runs, with Compile On Demand on or off, used or not
                    // (measured, #3335). A Private or Dim array there, and a Public one in a standard module, are not.
                    if (ObjectModule && context.visibility()?.PUBLIC() != null)
                        throw new VBCompileErrorException(PublicMemberOfObjectModule);
                    isArray = true;
                    // Bounds written as plain integers are read here. Anything else, such as a constant
                    // (`Dim d(N)`) or a typed literal (`Dim d(2&)`), is left to the Dim statement, which allocates
                    // the array again when the module's declarations run and evaluates its bounds there. They
                    // were parsed as integers here regardless, so either one threw a FormatException and the
                    // whole module failed to load. The name is still hoisted, as an undimensioned placeholder.
                    if (subStmt.subscripts() != null)
                    {
                        dimensions = new List<(int, int)>();
                        foreach (var dimension in subStmt.subscripts().subscript())
                        {
                            var size = dimension.valueStmt();
                            if (size.Length is not (1 or 2))
                                throw new VBCompileErrorException("Either specify upper bound or lower and upper bound");
                            var lower = ArrayBase;
                            if ((size.Length == 2 && !TryReadLiteralBound(size[0], out lower))
                                || !TryReadLiteralBound(size[^1], out var upper))
                            {
                                dimensions = null;
                                DeferredArrays.Add(subStmt.ambiguousIdentifier().GetText());
                                break;
                            }
                            // A literal range with no values does not compile (measured: `Dim d(5 To 2)`, `d(0 To -5)`
                            // and `d(1, 5 To 2)` at module level and in a class). Built here, its allocation raised 9.
                            if (upper < lower)
                                throw new VBCompileErrorException("Range has no values");
                            dimensions.Add((lower, upper));
                        }
                    }
                }

                Vb6Value.ValueType type = Vb6Value.ValueType.EmptyVariant;
                // `Dim o As Object`, `Dim c As Collection`: an object slot, Nothing until Set (#1010 review). An array of
                // them has the same Nothing elements (measured, #1482).
                var objectSlot = false;
                string? declaredClass = null;
                if (subStmt.asTypeClause() != null)
                {
                    // `Dim o As New C1` holds Nothing until its first use (#435). The module's Dim statement, which runs
                    // when the module starts, marks the slot; what it refuses (a scalar, a UDT) it refuses there. An
                    // array carries that mark on its element storage (#4150).
                    if (subStmt.asTypeClause().NEW() != null)
                    {
                        objectSlot = true;
                    }
                    else if (subStmt.asTypeClause().fieldLength() != null && subStmt.asTypeClause().type()?.baseType()?.STRING() is null)
                        throw new NotImplementedException("fieldLength as type not implemented");
                    if (subStmt.asTypeClause().type().complexType() != null)
                    {
                        // A UDT/Enum-typed Dim: hoist a placeholder here; the runtime VisitVariableStmt builds
                        // the real UDT instance (or an Enum-typed Long) once the program-wide type table exists.
                        // PrePass can't — it runs while the type table is still being assembled.
                        type = Vb6Value.ValueType.EmptyVariant;
                    }
                    else if (subStmt.asTypeClause().type().baseType() is { } objectType
                             && (objectType.OBJECT() != null || objectType.COLLECTION() != null))
                    {
                        objectSlot = true;
                        declaredClass = objectType.COLLECTION() != null ? "Collection" : isArray ? null : "Object";
                    }
                    else
                    {
                        type = BaseTypeMapper.Map(subStmt.asTypeClause().type().baseType())
                            ?? throw new NotImplementedException("base type " + subStmt.asTypeClause().type().baseType().GetChild(0) + " not implemented");
                    }
                }
                if (isArray && objectSlot)
                    type = Vb6Value.ValueType.Object;
                if (isArray)
                    type = new Vb6Value.ValueType(type, true);

                var value = objectSlot && !isArray ? Vb6Value.Nothing
                    : dimensions != null ? new Vb6Value(type, dimensions) : new Vb6Value(type);
                if (dimensions != null && value.Value is VBArray fixedArr) fixedArr.IsDynamic = false;   // `Dim a(N)` = fixed
                if (isArray && value.Value is VBArray objects)
                    objects.DeclaredClass = declaredClass;
                var location = state.Alloc(value, null, isArray ? null : declaredClass);
                if (isArray)
                {
                    state.MarkDeclaredArray(location);
                    if (dimensions != null)
                        state.MarkFixedArray(location);
                }
                else if (!objectSlot && (subStmt.asTypeClause() is null
                         || (subStmt.asTypeClause().type().complexType() is null && type == Vb6Value.ValueType.EmptyVariant)))
                    state.MarkVariant(location);
                rootEnv.DefineVariable(subStmt.ambiguousIdentifier().GetText(), location);
                // Global is the old spelling of Public in a standard module (measured: `Global n` in a bound is
                // "Constant expression required", as `Public n` is).
                if (context.visibility()?.PUBLIC() != null || context.visibility()?.GLOBAL() != null)
                    PublicVariables.Add(subStmt.ambiguousIdentifier().GetText());
                else if (context.visibility()?.PRIVATE() != null)
                    PrivateVariables.Add(subStmt.ambiguousIdentifier().GetText());
            }
        }
        else
            throw new VBCompileErrorException("Invalid outside procedure");   // `Static` at module level (measured, #1491)

        return default;
    }

    private static bool TryReadLiteralBound(VB6Parser.ValueStmtContext bound, out int value) =>
        int.TryParse(bound.GetText(), System.Globalization.NumberStyles.AllowLeadingSign,
            System.Globalization.CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// A Const declared <c>As Variant</c>, which reads as a Variant's. Every other Const is typed as its value is: measured,
    /// #1005, `Const KV As Variant = 5` is not `Case 4.6` and is `Case Is &lt; 5.4`, where `Const K = 5`, `Const KS = "10"`,
    /// `Const KD = 5.5` and `Const KB = True` all convert their Case values as a typed selector does.
    /// </summary>
    internal static bool IsVariantConst(VB6Parser.ConstSubStmtContext sub) =>
        sub.asTypeClause()?.type()?.baseType()?.VARIANT() != null;

    public override object? VisitConstStmt(VB6Parser.ConstStmtContext context)
    {
        if (ObjectModule && context.publicPrivateGlobalVisibility()?.PUBLIC() != null)
            throw new VBCompileErrorException(PublicMemberOfObjectModule);
        declared = true;
        // Hoist module-level const names into rootEnv (mirrors the Dim hoist) so a Sub declared after the
        // Const can reference it. The value is filled by the runtime VisitConstStmt (PrePass can't evaluate
        // the initializer). Sub/Function bodies aren't visited here, so local consts stay out of rootEnv.
        foreach (var sub in context.constSubStmt())
        {
            DeclareModuleName(sub.ambiguousIdentifier().GetText(), kind: ModuleNameKind.Const);
            var location = state.Alloc(Vb6Value.Variant);
            state.MarkConstant(location);
            if (IsVariantConst(sub))
                state.MarkVariant(location);
            rootEnv.DefineVariable(sub.ambiguousIdentifier().GetText(), location);
            var isPublic = context.publicPrivateGlobalVisibility() is { } visibility && visibility.PRIVATE() == null;
            ModuleConsts.TryAdd(sub.ambiguousIdentifier().GetText(), (sub, isPublic));
        }
        return default;
    }

    public override object? VisitDeclareStmt(VB6Parser.DeclareStmtContext context)
        // `Declare Sub/Function … Lib` (Win32/DLL API) is a documented deferral (docs/interpreter-gaps.md). Rather
        // than fail the whole module load, skip the declaration — the name is simply not registered, so an actual
        // CALL to it raises the clean "Sub or Function not defined" error. This lets a module that merely declares
        // an API (a near-universal pattern in real VB6) load and run its non-API code.
    {
        // #4245: only active declarations reach this walk, as inactive code is never parsed. vb6.exe refuses an active
        // PtrSafe with "Expected: Sub or Function" (measured, native and p-code). 64-bit VBA7 refuses a Declare without
        // PtrSafe (Excel x64, row P1) but its message wasn't read: this text is HexIDE's wording, not VBA's, until it
        // is measured. It is the engine's own English, as its other compile errors are (#3687).
        if (VbaBitness == VbaBitness.None && context.PTRSAFE() != null)
            throw new VBCompileErrorException("Expected: Sub or Function");
        if (VbaBitness == VbaBitness.Bit64 && context.PTRSAFE() == null)
            throw new VBCompileErrorException(
                $"Declare '{context.ambiguousIdentifier().GetText()}' requires PtrSafe in 64-bit VBA7");
        if (ObjectModule && context.visibility()?.PUBLIC() != null)
            throw new VBCompileErrorException(PublicMemberOfObjectModule);
        declared = true;
        DeclareModuleName(context.ambiguousIdentifier().GetText(), exported: !IsPrivate(context.visibility()));
        if (context.SUB() != null && !IsPrivate(context.visibility()))
            DeclareSubs.Add(context.ambiguousIdentifier().GetText());
        return default;
    }

    public override object? VisitEnumerationStmt(VB6Parser.EnumerationStmtContext context)
    {
        declared = true;
        // Enum members are Long compile-time CONSTANTS whose values are constant EXPRESSIONS, not merely
        // literals: `&H80000005` (measured -2147483643, the high bit making it negative), `&O17`, `-3`,
        // `xFirst + 1`, `2 ^ 3` and bit-ors of earlier members are all ordinary VB6. A member with no value
        // takes the previous member's value + 1, from any of those.
        //
        // Evaluation is a SINGLE FORWARD WALK, and that is measured rather than convenient: VB6 refuses a
        // member that references a later member, and one that references a later Const, both with
        // "Constant expression required". Values naming project Consts complete that forward walk with
        // the module's declarations, in the same source order.
        var enumName = context.ambiguousIdentifier().GetText();
        DeclareModuleTypeName(enumName);
        var members = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        enumStatements[enumName] = context;
        int? firstDeferred = null;
        var memberIndex = 0;
        long next = 0;
        foreach (var c in context.enumerationStmt_Constant())
        {
            var constName = c.ambiguousIdentifier().GetText();
            DeclareModuleName(constName, inEnum: enumName);
            long value = 0;
            if (firstDeferred is null)
            {
                if (c.valueStmt() is { } vs)
                {
                    foldingMember = constName;
                    if (!TryFoldEnumValue(vs, enumName, members, out value))
                    {
                        if (!ObjectModule && NamesProjectConstant(vs, enumName))
                            firstDeferred = memberIndex;
                        else
                            throw new VBCompileErrorException(
                                $"Constant expression required (Enum {enumName}, member '{constName}': '{vs.GetText().Trim()}')");
                    }
                    next = value + 1;
                }
                else
                {
                    value = next++;
                }
                if (firstDeferred is null) members[constName] = value;
            }

            // A member name already hoisted by a DIFFERENT enum in this module is AMBIGUOUS. VB6 accepts
            // both declarations and refuses the bare use — "Ambiguous name detected" — so the name is
            // recorded and the complaint made when something reads it. Silently overwriting, which is what
            // happened before, hands back the other enum's value with nothing to show for it.
            if (rootEnv.TryGetVariableLocation(constName, out _) && !members.ContainsKey(constName))
                AmbiguousEnumMembers.Add(constName);
            foreach (var (otherName, otherMembers) in Enums)
                if (!string.Equals(otherName, enumName, StringComparison.OrdinalIgnoreCase)
                    && otherMembers.ContainsKey(constName))
                    AmbiguousEnumMembers.Add(constName);

            // Hoisted READ-ONLY. VB6 answers `pTwo = 5` with "Assignment to constant not permitted", and a
            // plain variable slot silently accepted it — so a program could overwrite vbRed and nothing
            // would say so.
            var location = state.Alloc(firstDeferred is null ? new Vb6Value(value) : default(Vb6Value));
            if (firstDeferred is null) state.MarkReadOnly(location);
            else state.MarkConstant(location);
            rootEnv.DefineVariable(constName, location);
            memberIndex++;
        }
        if (firstDeferred is { } first)
        {
            DeferredEnums[enumName] = (context, first);
            DeclarationItems.Add(context);
        }
        Enums[enumName] = members;
        EnumDeclaredAt[enumName] = context.Start.TokenIndex;
        if (IsPrivateVisibility(context.publicPrivateVisibility()?.GetText()))
            PrivateEnums.Add(enumName);
        return default;
    }

    private bool NamesProjectConstant(Antlr4.Runtime.Tree.IParseTree node, string enumName, int depth = 0)
    {
        if (depth > 500) return false;
        if (node is VB6Parser.VsICSContext read)
        {
            var text = read.GetText();
            if (text.Contains('(')) return false;
            var parts = text.Split('.');
            if (parts.Length == 1)
                return ModuleConsts.ContainsKey(text) || !rootEnv.TryGetVariableLocation(text, out _)
                    && !VB6InBoxLibraries.TryBare(text, out _);
            var lead = parts[0];
            return !VB6InBoxLibraries.IsLibrary(lead) && !VB6InBoxLibraries.IsContainer(lead)
                && !Enums.ContainsKey(lead) && !lead.Equals(enumName, StringComparison.OrdinalIgnoreCase);
        }
        for (var i = 0; i < node.ChildCount; i++)
            if (NamesProjectConstant(node.GetChild(i), enumName, depth + 1)) return true;
        return false;
    }

    /// <summary>
    /// Fold one Enum member's value expression to the Long it denotes, or fail.
    ///
    /// <para>
    /// This is a CONSTANT folder, deliberately separate from <see cref="ExpressionExecutor"/>: an Enum body
    /// is folded before declarations run, so project Const values are not available here and nothing here
    /// may have a side effect. Values outside this fold are refused or deferred to the module's declaration
    /// walk, never guessed — a member is a value the whole program reads, so a wrong one would be wrong
    /// everywhere and silently.
    /// </para>
    ///
    /// <para>
    /// Names resolve against members already folded and in-box constants (#4594): earlier
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
                    return double.TryParse(ExpressionExecutor.ExponentAsE(dbl.GetText()).TrimEnd('#', '!', '&', '@', '%'),
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
            // `+` and `-` share an alternative, as `*` and `/` do, which is what takes them left to right (#1074).
            VB6Parser.VsAddContext a => (a.valueStmt(0), a.valueStmt(1), a.MINUS() != null ? "-" : "+"),
            VB6Parser.VsMultContext m => (m.valueStmt(0), m.valueStmt(1), m.DIV() != null ? "/" : "*"),
            // `\` alone: `7 / 2` is 4 in VB6 and `7 \ 2` is 3, so the two must not fold alike.
            VB6Parser.VsDivContext d => (d.valueStmt(0), d.valueStmt(1), "\\"),
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
    private bool TryResolveFoldedName(string text, string enumName, Dictionary<string, long> soFar, out double result)
    {
        result = 0;
        long folded;
        var dot = text.LastIndexOf('.');
        if (dot < 0)
        {
            // Bare: this enum's earlier members first, then any earlier enum's members. Both are measured
            // legal; a name declared by two enums is ambiguous in VB6 and is left for the collision work.
            if (soFar.TryGetValue(text, out folded)) { result = folded; return true; }
            foreach (var (_, members) in Enums)
                if (members.TryGetValue(text, out folded)) { result = folded; return true; }
            return TryLibraryName(text, enumName, out result);
        }

        var qualifier = text[..dot].Trim();
        var member = text[(dot + 1)..].Trim();
        // An enum's own name is in scope inside its own body — measured.
        var table = string.Equals(qualifier, enumName, StringComparison.OrdinalIgnoreCase)
            ? soFar
            : Enums.TryGetValue(qualifier, out var t) ? t : null;
        if (table is not null && table.TryGetValue(member, out folded)) { result = folded; return true; }
        return TryLibraryName(text, enumName, out result);
    }

    // #4594: library values are provisional until every module's declarations have been collected.
    // Defer refusals too: a project qualifier must win even when the library lookup would fail.
    private bool TryLibraryName(string text, string enumName, out double result)
    {
        result = 0;
        var parts = text.Split('.');
        if (parts.Length > 3 || text.Contains('(')) return false;
        Vb6Value value = default;
        bool found;
        Exception? refusal = null;
        if (parts.Length == 1)
            found = VB6InBoxLibraries.TryBare(text, out value);
        else if (VB6InBoxLibraries.IsLibrary(parts[0]))
        {
            found = parts.Length == 2
                ? VB6InBoxLibraries.TryInLibrary(parts[0], parts[1], out value)
                : parts.Length == 3 && VB6InBoxLibraries.ContainerBelongsTo(parts[0], parts[1])
                    && VB6InBoxLibraries.TryInLibraryContainer(parts[0], parts[1], parts[2], out value);
            if (!found)
                refusal = new VBCompileErrorException(parts.Length == 3
                    && VB6InBoxLibraries.TryInLibrary(parts[0], parts[1], out _)
                        ? "Invalid qualifier"
                        : "Method or data member not found");
        }
        else if (parts.Length == 2 && VB6InBoxLibraries.IsContainer(parts[0]))
        {
            found = VB6InBoxLibraries.TryInContainer(parts[0], parts[1], out value);
            if (!found)
                refusal = new VBCompileErrorException("Method or data member not found");
        }
        else
            return false;
        if (!found && refusal is null) return false;
        if (found && !TryAsDouble(value, out result))
            refusal = new VBCompileErrorException("Type mismatch");
        EnumLibraryNames.Add(new(enumName, foldingMember, text, parts.Length > 1, refusal));
        return true;
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
        // Conditional compilation is applied before the parse (ConditionalCompiler, #1112), so every directive is gone by
        // now. A `#` line left is one vb6.exe does not read as a directive, which it compiles as code: a syntax error.
        => throw new VBCompileErrorException("Syntax error");

    public override object? VisitPropertyGetStmt(VB6Parser.PropertyGetStmtContext context)
    {
        // Property Get is Function-like: returns via its own name and binds its declared parameters at the call.
        // A complexType return
        // (`As Node`) carries its concrete name so the return slot seeds a real UDT / Nothing (see RunProcedure).
        var name = context.ambiguousIdentifier().GetText();
        DeclareModuleName(name, accessor: "Get", exported: !IsPrivate(context.visibility()));
        var args = ParseParams(context.argList());
        var returnUdt = NamedType(context.asTypeClause());
        // `Property Get P$()` returns a String, as a Function's character declares its return. Unlike a Function's, it
        // compiles beside an As clause too, and the As clause is the type: `Q$() As Long` returns a Long (measured, #742).
        var (returnType, returnsVariant, returnsObject) = context.typeHint() is { } hint && context.asTypeClause() is null
            ? (BaseTypeMapper.Map(hint), false, false)
            : ReturnOf(context.asTypeClause(), name);
        GetOrAddProperty(name).Get = CheckedLocals(new ProcedureInfo(name, true, args,
            returnUdt != null && !returnType.IsArray ? Vb6Value.ValueType.UserDefinedType : returnType,
            context.block(), IsPrivate(context.visibility()), returnUdt, CheckedArrayReturnOf(context.asTypeClause()),
            returnsVariant, returnsObject) { Declaration = context, Accessor = "Get" });
        NoteDefaultMember(context.block(), DefaultMemberKind.Property, enumeration: true);
        return default;
    }

    public override object? VisitPropertyLetStmt(VB6Parser.PropertyLetStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        DeclareModuleName(name, accessor: "Let", exported: !IsPrivate(context.visibility()));
        GetOrAddProperty(name).Let = ParsePropertyMutator("Let", name, context.argList(), context.block(), context.visibility());
        NoteDefaultMember(context.block(), DefaultMemberKind.Property);
        return default;
    }

    public override object? VisitPropertySetStmt(VB6Parser.PropertySetStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        DeclareModuleName(name, accessor: "Set", exported: !IsPrivate(context.visibility()));
        GetOrAddProperty(name).Set = ParsePropertyMutator("Set", name, context.argList(), context.block(), context.visibility());
        NoteDefaultMember(context.block(), DefaultMemberKind.Property);
        return default;
    }

    private PropertyInfo GetOrAddProperty(string name)
    {
        if (!Properties.TryGetValue(name, out var prop))
            Properties[name] = prop = new PropertyInfo { Name = name };
        return prop;
    }

    // A Property Let/Set accessor receives the assigned value in its last parameter, after any indices.
    private ProcedureInfo ParsePropertyMutator(string kind, string name, VB6Parser.ArgListContext? argList,
        VB6Parser.BlockContext? block, VB6Parser.VisibilityContext? visibility)
    {
        var args = ParseParams(argList);
        if (args.Count != 1 && !(args.Count > 1 && (kind == "Let" ? allowIndexedLet : allowIndexedSet)))
            throw new NotImplementedException(
                $"Property {kind} '{name}' must take exactly one (value) parameter — parameterized properties are not supported");
        return CheckedLocals(new ProcedureInfo(name, false, args, Vb6Value.ValueType.EmptyVariant, block, IsPrivate(visibility))
            { Accessor = kind });
    }

    public override object? VisitTypeStmt(VB6Parser.TypeStmtContext context)
    {
        declared = true;
        // A `Type … End Type` definition: parse each field to a scalar ValueType or a nested-type NAME (resolved
        // — UDT vs Enum — at instantiation, once the program-wide tables exist). Array bounds and fixed-string
        // width syntax are collected here; widths resolve when storage is allocated.
        var name = context.ambiguousIdentifier().GetText();
        DeclareModuleTypeName(name);
        var fields = new List<UdtField>();
        foreach (var el in context.typeStmt_Element())
        {
            var fieldName = el.ambiguousIdentifier().GetText();
            var isArray = el.LPAREN() != null;
            List<(int, int)>? bounds = null;
            if (el.subscripts() is { } subscripts)
            {
                bounds = new();
                foreach (var dimension in subscripts.subscript())
                {
                    var values = dimension.valueStmt();
                    var lower = ArrayBase;
                    if ((values.Length == 2 && !TryReadLiteralBound(values[0], out lower))
                        || !TryReadLiteralBound(values[^1], out var upper))
                    {
                        bounds = null;
                        break;
                    }
                    if (upper < lower)
                        throw new NotImplementedException($"An inverted array bound inside Type '{name}' is not implemented (#178)");
                    bounds.Add((lower, upper));
                }
            }

            var asType = el.asTypeClause();
            if (asType == null)
            {
                fields.Add(new UdtField(fieldName, Vb6Value.ValueType.EmptyVariant, null, IsArray: isArray, Bounds: bounds));   // untyped -> Variant
                continue;
            }
            if (asType.fieldLength() != null && asType.type()?.baseType()?.STRING() is null)
                throw new NotImplementedException("fieldLength as type not implemented");
            if (asType.NEW() != null)
            {
                if (isArray)
                    throw new NotImplementedException($"An array field As New inside Type '{name}' is not implemented (#178)");
                fields.Add(new UdtField(fieldName, null, null, asType.type().GetText()));
                continue;
            }
            if (BaseTypeMapper.VbaType(asType.type().GetText(), VbaBitness) is { } wide)
                fields.Add(new UdtField(fieldName, wide, null, IsArray: isArray, Bounds: bounds));
            else if (asType.type().complexType() is not null || asType.type().baseType()?.OBJECT() is not null
                || asType.type().baseType()?.COLLECTION() is not null)
                fields.Add(new UdtField(fieldName, null, asType.type().GetText(), IsArray: isArray, Bounds: bounds));   // nested UDT, Enum or object type, resolved at NewUdt
            else
                fields.Add(new UdtField(fieldName,
                    BaseTypeMapper.Map(asType.type().baseType())
                        ?? throw new NotImplementedException($"Unsupported field type for '{fieldName}' in Type '{name}'"),
                    null, IsArray: isArray, Bounds: bounds, FixedLength: asType.fieldLength()));
        }
        for (var i = 0; i < fields.Count; i++)
            if (fields[i].IsArray && fields[i].Bounds is null && context.typeStmt_Element(i).subscripts() is { } pending)
                fields[i] = fields[i] with { BoundSyntax = pending };
        // Visibility is RECORDED rather than ignored. `Private` means invisible outside this module, and
        // measured means it: a foreign Private Type is "User-defined type not defined" whether the
        // reference is bare or qualified. Reading it here is what lets two modules each own a Point.
        Types[name] = new UdtTypeDef(name, fields, IsPrivateVisibility(context.visibility()?.GetText()));
        if (fields.Any(f => f.BoundSyntax != null))
            DeclarationItems.Add(context);
        return default;
    }

    public override object? VisitFunctionStmt(VB6Parser.FunctionStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        // A complexType return (`As Employee`) is the generic UDT ValueType here; the concrete name is carried
        // separately so the return slot can be seeded with a real UDT at call time.
        var returnUdt = NamedType(context.asTypeClause());
        // `Function F%()` returns an Integer, and `& ! # @ $` a Long, Single, Double, Currency and String, ahead of a DefType
        // for the name's letter. A character and an As clause together are VB6's compile error (measured, #740).
        var hint = context.typeHint();
        if (hint is not null && context.asTypeClause() is not null)
            throw new VBCompileErrorException("Expected: end of statement");
        var (returnType, returnsVariant, returnsObject) = hint is not null
            ? (BaseTypeMapper.Map(hint), false, false)
            : ReturnOf(context.asTypeClause(), name);
        DeclareModuleName(name, exported: !IsPrivate(context.visibility()));
        Procedures[name] = CheckedLocals(new ProcedureInfo(name, true, ParseParams(context.argList()),
            returnUdt != null && !returnType.IsArray ? Vb6Value.ValueType.UserDefinedType : returnType,
            context.block(), IsPrivate(context.visibility()), returnUdt, CheckedArrayReturnOf(context.asTypeClause()),
            returnsVariant, returnsObject) { Declaration = context });
        NoteDefaultMember(context.block(), DefaultMemberKind.Function, enumeration: true);
        return default;
    }

    // Measured for Sub names only, not other declarations (#4674).
    private static readonly HashSet<string> RefusedSubNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Close", "Debug", "End", "Get", "Input", "Len", "Lock", "Open",
        "Print", "Put", "Seek", "Spc", "Stop", "Tab", "Unlock", "Write"
    };

    public override object? VisitSubStmt(VB6Parser.SubStmtContext context)
    {
        var name = context.ambiguousIdentifier().GetText();
        if (RefusedSubNames.Contains(name))
            throw new VBCompileErrorException("Expected: identifier");
        DeclareModuleName(name, exported: !IsPrivate(context.visibility()));
        Procedures[name] = CheckedLocals(new ProcedureInfo(name, false, ParseParams(context.argList()),
            Vb6Value.ValueType.EmptyVariant, context.block(), IsPrivate(context.visibility())) { Declaration = context });
        NoteDefaultMember(context.block(), DefaultMemberKind.Sub);
        return default;
    }

    // A module-level procedure is Public unless explicitly `Private`. `Friend` counts as Public (no external
    // clients to hide from). Absent visibility ⇒ Public.
    private static bool IsPrivate(VB6Parser.VisibilityContext? visibility) => visibility?.PRIVATE() != null;

    private List<ParamInfo> ParseParams(VB6Parser.ArgListContext? argList)
    {
        var result = new List<ParamInfo>();
        if (argList == null)
            return result;
        foreach (var arg in argList.arg())
        {
            if (arg.asTypeClause() is { } fixedType && fixedType.fieldLength() is not null
                && fixedType.type()?.baseType()?.STRING() is not null && arg.LPAREN() is null)
                throw new VBCompileErrorException("Expected array");
            // `Sub F(n%)` is `Sub F(n As Integer)`, and an As clause beside the character is VB6's compile error (measured,
            // #742). With neither, the DefType's for the name's letter (measured, #169: `Sub ITake(i7)` under DefInt, given
            // 2.6, holds an Integer 3). DefObj makes it an `As Object`.
            var hint = arg.typeHint();
            if (hint is not null && arg.asTypeClause() is not null)
                throw new VBCompileErrorException("Expected: list separator or )");
            var defaultType = hint is not null ? BaseTypeMapper.Map(hint)
                : arg.asTypeClause() is null ? DefaultTypeOf(arg.ambiguousIdentifier().GetText()) : null;
            var defaultScalar = defaultType == Vb6Value.ValueType.Object ? null : defaultType;
            result.Add(new ParamInfo(
                arg.ambiguousIdentifier().GetText(),
                ByRef: arg.BYVAL() == null,        // VB6's default passing convention is ByRef
                Optional: arg.OPTIONAL() != null,
                ParamArray: arg.PARAMARRAY() != null,
                // `b() As Long` is a Long array, not a Long: declared as the element type, an array passed ByRef was
                // copied rather than shared, and then narrowed as if it were a scalar.
                DeclaredType: arg.LPAREN() != null && (ExtractBaseType(arg.asTypeClause()) ?? defaultScalar) is { } element
                    ? new Vb6Value.ValueType(element, true)
                    : ExtractBaseType(arg.asTypeClause()) ?? defaultScalar,
                Default: arg.argDefaultValue(),
                IsArray: arg.LPAREN() != null,
                ReadsAsVariant: arg.LPAREN() == null
                    && (defaultType is null ? DeclaresVariant(arg.asTypeClause()) : defaultType == Vb6Value.ValueType.Object),
                TypeName: NamedType(arg.asTypeClause()),
                DeclaredObject: arg.LPAREN() == null
                    && (DeclaresObject(arg.asTypeClause()) || defaultType == Vb6Value.ValueType.Object))
            { FixedStringLength = arg.asTypeClause()?.fieldLength() });
        }
        return result;
    }

    // A declaration that is a Variant's: no As clause, `As Variant`, or `As Object`, which VB6 binds late as it binds a
    // Variant (measured, #1005). Not an array: `As Variant()` declares an array.
    private static bool DeclaresObject(VB6Parser.AsTypeClauseContext? asType) =>
        asType?.type() is { } type && type.LPAREN() == null && type.baseType()?.OBJECT() != null;

    private static bool DeclaresVariant(VB6Parser.AsTypeClauseContext? asType) =>
        asType is null
        || asType.type()?.LPAREN() == null && asType.type()?.baseType() is { } baseType
           && (baseType.VARIANT() != null || baseType.OBJECT() != null);

    // Maps an `As <type>` clause to a base ValueType, or null when absent / complex / unknown (params and
    // return types are left untyped in that case for now — the wider type system is a later phase).
    private Vb6Value.ValueType? ExtractBaseType(VB6Parser.AsTypeClauseContext? asType)
        => BaseTypeMapper.VbaType(asType?.type()?.complexType()?.GetText(), VbaBitness)
            ?? BaseTypeMapper.Map(asType?.type()?.baseType());

    private string? NamedType(VB6Parser.AsTypeClauseContext? asType) =>
        ExtractBaseType(asType) is null ? asType?.type()?.complexType()?.GetText() : null;

    // A Function's or Property Get's return type, an array where the clause says so: `As Long()` returns a Long() (measured,
    // #975: TypeName "Long()", and one that never assigns its name returns an empty one, UBound raising 9). It was read as
    // its element type, so the array the body stored was narrowed into a Long on the way out, and came back 0.
    // A Function's or Property Get's return: its type, whether it reads as a Variant's, and whether it is an object variable,
    // As Object or As Collection. With no As clause it is the DefType's for its name's letter (measured, #169: `Function
    // IFun()` under DefInt returns an Integer).
    private (Vb6Value.ValueType Type, bool ReadsAsVariant, bool IsObject) ReturnOf(VB6Parser.AsTypeClauseContext? asType, string name)
    {
        if (asType is null && DefaultTypeOf(name) is { } defaultType)
            return defaultType == Vb6Value.ValueType.Object ? (Vb6Value.ValueType.EmptyVariant, true, true) : (defaultType, false, false);
        return (ReturnTypeOf(asType), DeclaresVariant(asType),
            DeclaresObject(asType) || asType?.type() is { } type && type.LPAREN() == null && type.baseType()?.COLLECTION() != null);
    }

    private Vb6Value.ValueType ReturnTypeOf(VB6Parser.AsTypeClauseContext? asType)
    {
        if (asType?.fieldLength() is not null && asType.type()?.baseType()?.STRING() is not null)
            throw new VBCompileErrorException("Expected: end of statement");
        var type = asType?.type()?.LPAREN() != null && asType.type().baseType() is { } element
            && (element.OBJECT() != null || element.COLLECTION() != null)
            ? Vb6Value.ValueType.Object : ExtractBaseType(asType) ?? Vb6Value.ValueType.EmptyVariant;
        return asType?.type()?.LPAREN() != null ? new Vb6Value.ValueType(type, true) : type;
    }

    // A named element type is resolved at the call, once the project's classes are known (#999).
    private Vb6Value.ValueType? CheckedArrayReturnOf(VB6Parser.AsTypeClauseContext? asType)
        => asType?.type()?.LPAREN() != null && asType.type().complexType() is null ? ReturnTypeOf(asType) : null;
}
