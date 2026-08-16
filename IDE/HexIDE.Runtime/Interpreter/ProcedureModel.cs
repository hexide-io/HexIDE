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
    string? ReturnUdtTypeName = null);

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
    VB6Parser.ArgDefaultValueContext? Default);

/// <summary>
/// An evaluated argument at a call site: its value plus, when the argument is a caller lvalue, the caller
/// memory slot it lives in (so a ByRef parameter can alias it). <c>Location</c> is null for rvalues.
/// </summary>
public readonly record struct CallArg(Vb6Value Value, int? Location);
