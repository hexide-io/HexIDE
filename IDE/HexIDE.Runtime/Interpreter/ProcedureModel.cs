using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// A declared user procedure — a <c>Sub</c> or <c>Function</c> — collected by <see cref="PrePass"/> and
/// invoked by <see cref="BasicInterpreter"/>. Deliberately holds no captured environment: the callee scope
/// is cloned from the module root <em>at call time</em> so recursion and forward-referenced module globals
/// work (the prepass-time clone this replaces broke both).
/// </summary>
public sealed record ProcedureInfo(
    string Name,
    bool IsFunction,
    IReadOnlyList<ParamInfo> Parameters,
    Vb6Value.ValueType ReturnType,
    VB6Parser.BlockContext? Body,
    // Module-level procedures default to Public; only `Private` (and, for us, nothing else) hides a procedure
    // from the cross-module global namespace. `Friend` is treated as Public — the interpreter has no external
    // COM clients to hide it from. A module always sees its own procedures regardless of visibility.
    bool IsPrivate = false,
    // For a Function returning a user-defined Type (`Function F() As Employee`), the type name — so the return
    // slot is seeded with a real zero-initialised UDT (its ReturnType is the generic UDT ValueType, which has
    // no concrete identity). Null for scalar/Enum/no return.
    string? ReturnUdtTypeName = null,
    // For a Function or Property Get declared to return an array of a scalar type or of Variant (`As Integer()`), that
    // array type, so a store into the return name can be checked against it (#986). Null otherwise, including `As
    // Object()` and `As Collection()`, whose element type is not modelled.
    Vb6Value.ValueType? CheckedArrayReturn = null,
    // Whether the return reads as a Variant's: declared with no As clause, `As Variant`, or `As Object`, which VB6 binds
    // late as it binds a Variant (measured, #1005: `GetObj().P` with `GetObj() As Object` is a Variant, and `GetFoo().P`
    // with `GetFoo() As Foo` is typed). A return the map does not type, such as `As Collection`, is neither.
    bool ReturnsVariant = false,
    // Whether the return is declared `As Object` (under DefObj too) or `As Collection`. Its name is an object variable, Nothing
    // until Set, so a Let into it is the held object's default member (measured, #1493: `F = 6` in `F() As Object` holding an
    // instance is 438, where in `F() As Variant` it drops the object and takes 6; and #169: before any Set it is 91). An `As
    // Object` one is read late all the same, as ReturnsVariant says.
    bool ReturnsObject = false)
{
    private IReadOnlyList<VB6Parser.VariableSubStmtContext>? declaredLocals;

    /// <summary>
    /// Every local this procedure declares, in declaration order — collected once and cached.
    ///
    /// In VB6 a <c>Dim</c> is a DECLARATION, not an executable statement: the local exists for the whole
    /// invocation, so reading it before its Dim line is legal and yields the declared type's zero. Allocating
    /// on execution instead meant a name resolved to a module-level PROCEDURE of the same name until its Dim
    /// ran, and a watch on it reported a change the program never made.
    ///
    /// Collection, not analysis: a list of declaration sites, no relationship between them. See the pre-pass
    /// boundary in CLAUDE.md.
    ///
    /// Nesting is not consulted because VB6 has none to consult — a Dim inside an If or a For is scoped to
    /// the procedure exactly as one at the top is.
    /// </summary>
    public IReadOnlyList<VB6Parser.VariableSubStmtContext> DeclaredLocals =>
        declaredLocals ??= CollectDeclaredLocals(Body);

    /// <summary>
    /// Every <c>Const</c> the procedure declares, in declaration order, wherever it sits in the body. Declared at
    /// entry ahead of the Dims, because a Dim's bounds may name one: <c>Const P = 2.5</c> then <c>Dim a(P)</c> is
    /// <c>a(0 To 2)</c> (measured), and the hoisted Dim was sized while <c>P</c> was still Empty.
    /// </summary>
    public IReadOnlyList<VB6Parser.ConstSubStmtContext> DeclaredConsts =>
        declaredConsts ??= CollectDeclaredConsts(Body);

    private IReadOnlyList<VB6Parser.ConstSubStmtContext>? declaredConsts;

    private static List<VB6Parser.ConstSubStmtContext> CollectDeclaredConsts(VB6Parser.BlockContext? body)
    {
        var found = new List<VB6Parser.ConstSubStmtContext>();
        if (body != null) Walk(body);
        return found;

        void Walk(Antlr4.Runtime.Tree.IParseTree node)
        {
            if (node is VB6Parser.ConstStmtContext c)
            {
                found.AddRange(c.constSubStmt());
                return;
            }
            for (var i = 0; i < node.ChildCount; i++)
                Walk(node.GetChild(i));
        }
    }

    private static List<VB6Parser.VariableSubStmtContext> CollectDeclaredLocals(VB6Parser.BlockContext? body)
    {
        var found = new List<VB6Parser.VariableSubStmtContext>();
        if (body != null) Walk(body);
        return found;

        void Walk(Antlr4.Runtime.Tree.IParseTree node)
        {
            if (node is VB6Parser.VariableStmtContext v
                && (v.DIM() != null || v.STATIC() != null || v.visibility() != null)
                && v.variableListStmt() is { } list)
            {
                foreach (var sub in list.variableSubStmt())
                    found.Add(sub);
                return;   // its own children hold no further declarations
            }
            for (var i = 0; i < node.ChildCount; i++)
                Walk(node.GetChild(i));
        }
    }
}

/// <summary>
/// A declared class <c>Property</c> — up to three accessors sharing one name, dispatched by <em>access
/// kind</em>: <c>Get</c> on read (<c>= x.P</c>), <c>Let</c> on value-assign (<c>x.P = v</c>), <c>Set</c> on
/// object-assign (<c>Set x.P = o</c>) — mirroring the variable Let/Set split. Each accessor is modelled as a
/// <see cref="ProcedureInfo"/> dispatched by the same interpreter-callback as a method: <c>Get</c> is
/// Function-like (returns via its own name); <c>Let</c>/<c>Set</c> are Sub-like, their single parameter
/// receiving the assigned value (so the value coerces to that parameter's declared type). Parameterized
/// properties (an index parameter before the value) are a wall — enforced in <see cref="PrePass"/>.
/// </summary>
public sealed class PropertyInfo
{
    public required string Name { get; init; }
    public ProcedureInfo? Get { get; set; }
    public ProcedureInfo? Let { get; set; }
    public ProcedureInfo? Set { get; set; }
}

/// <summary>One formal parameter. VB6's default passing convention is <c>ByRef</c>.</summary>
public sealed record ParamInfo(
    string Name,
    bool ByRef,
    bool Optional,
    bool ParamArray,
    Vb6Value.ValueType? DeclaredType,
    VB6Parser.ArgDefaultValueContext? Default,
    bool IsArray = false,
    // Whether the parameter reads as a Variant's: declared with no As clause, `As Variant`, or `As Object`, which VB6
    // binds late as it binds a Variant (measured, #1005). DeclaredType cannot say this: it is null for a class, an Enum
    // and a Collection too, which are typed (`s.P` with `s As Foo` overflows, and `c` with `c As MyEnum` is a Long).
    bool ReadsAsVariant = false,
    // The type name of an `As Name` clause that is not a base type: a class, a UDT or an Enum. An Enum parameter is a
    // Long, which BasicInterpreter.BindParameters resolves at the call, once every module's Enums are known.
    string? TypeName = null,
    // Whether it is declared `As Object`: it reads as a Variant does (ReadsAsVariant), but it is an object variable, so a
    // Let into it is the held object's default member (measured, #1493: `x = 1` with `x As Object`, ByRef or ByVal, is
    // 438 where `x As Variant` takes 1).
    bool DeclaredObject = false);

/// <summary>
/// An evaluated argument at a call site: its value plus, when the argument is a caller lvalue, the caller
/// memory slot it lives in (so a ByRef parameter can alias it). <c>Location</c> is null for rvalues. A UDT field
/// (`u.l`, #1451) or an array element (`a(1)`, #1455) has no slot, so it carries the target itself instead, which a ByRef
/// parameter aliases the same way. A named argument (`c:=5`, #1465) carries its <c>Name</c> and is not evaluated yet:
/// <c>Deferred</c> evaluates it, once binding has placed it, so named arguments are evaluated in parameter order as in VB6.
/// </summary>
public readonly record struct CallArg(Vb6Value Value, int? Location, IByRefTarget? Ref = null, string? Name = null,
    Func<Task<CallArg>>? Deferred = null);
