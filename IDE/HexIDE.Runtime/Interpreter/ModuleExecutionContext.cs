using Antlr4.Runtime;

namespace HexIDE.Runtime.Interpreter;

public class ModuleExecutionContext
{
    private ExecutionState state = new();

    public ExecutionState State => state;

    /// <summary>
    /// Write a named variable, coercing to the slot's declared type on the way in.
    ///
    /// This is the single sink every named write goes through — Let, Set, For counters, For Each elements,
    /// field writes — so the coercion belongs here rather than at each call site. A slot with no declared
    /// type is a Variant and takes the value unchanged, which is exactly the old behaviour.
    /// </summary>
    /// <param name="fillingConstant">
    /// Set only by the Const statement, which fills the slot the pre-pass hoisted, and fills it again for each instance
    /// of a class. Every other store into a Const is refused.
    /// </param>
    public bool TryUpdateVariable(ExecutionEnvironment env, string name, Vb6Value value, ParserRuleContext? ctx = null,
        bool fillingConstant = false)
    {
        if (env.TryGetVariableLocation(name, out var loc))
        {
            // An Enum member and a user Const are constants. VB6 refuses `pTwo = 5`, `MK = 2` and `Set MK = Nothing` at
            // compile time with "Assignment to constant not permitted" (measured, #1003: a module Const, a local one, a
            // class's, `Module1.MK = 2` and `MK = MK + 1` alike); the walk can only refuse it here, as the statement runs,
            // which is the translation interpreter-core:40-42 prescribes. Checked before the coercion, because the write
            // is not going to happen and a coercion error would name the wrong problem. A statement VB6 refuses with
            // another message checks first (StatementExecutor.RefuseConstantTarget): For, For Each, Mid, ReDim and Erase.
            if (state.IsReadOnly(loc) || state.IsConstant(loc) && !fillingConstant)
                throw new VBCompileErrorException($"Assignment to constant not permitted ({name})");

            // A variable holding an array one of whose elements a running call holds ByRef is locked: `d = e`, and `v = 5` for a
            // Variant holding the array, raise 10 and leave it as it was (measured, #1455).
            if (state.HasRefSlots && state[loc].Value is VBArray { IsLocked: true })
                throw new VBRunTimeException(ctx, VBStandardError.ThisArrayIsFixedOrTemporarilyLocked);
            if (state.DeclaredTypeOf(loc) is { } declared)
                value = VbNumeric.CoerceOnStore(value, declared, ctx);
            // A class-typed slot enforces its name: the object must BE that class or implement it as an
            // interface. Anything else is Err 13 "Type mismatch" at the Set — measured, both for two unrelated
            // classes and for an interface-typed slot given a non-implementer. Nothing always stores (its Value
            // is null); a control or proxy is outside this model.
            CheckObjectStore(value, state.DeclaredClassOf(loc), ctx);
            state[loc] = value;
            return true;
        }

        return false;
    }

    // Scalar slots and array elements enforce the same object type (measured, #1482).
    internal static void CheckObjectStore(Vb6Value value, string? declaredClass, ParserRuleContext? ctx = null)
    {
        if (declaredClass is not null
            && Components.InBox.ControlLibraries.ForTypeName(declaredClass) is { } library
            && declaredClass.Equals(library.TypeName("ProgressBar"), StringComparison.OrdinalIgnoreCase)
            && value.Value is not null
            && (value.Value is not BuiltinControls.VBProgressBar bar || !ReferenceEquals(bar.Library, library)))
            throw new VBRunTimeException(ctx, VBStandardError.TypeMismatch);
        if (declaredClass is not null
            && value.Value is VbObject incoming
            && !VbInterface.IsAssignableTo(incoming, declaredClass))
            throw new VBRunTimeException(ctx, VBStandardError.TypeMismatch);
        // A Collection, likewise, goes only into a Collection-typed slot of the class-typed ones (measured, #1471).
        if (declaredClass is { } classTyped && value.Value is VbCollection
            && !ExpressionExecutor.IsCollectionType(classTyped))
            throw new VBRunTimeException(ctx, VBStandardError.TypeMismatch);
    }

    public bool TryGetVariable(ExecutionEnvironment env, string name, out Vb6Value value) =>
        TryRead(env, name, out value, inspecting: false);

    /// <summary>
    /// Read a variable for the debugger: as <see cref="TryGetVariable"/> does, except that a form's control is read even
    /// before the form has loaded, and a form's name holding no instance reads as the Nothing it holds. Locals and the
    /// watch window only look: a program reading either loads the form or makes its instance (#822), and the debugger
    /// mustn't.
    /// </summary>
    public bool TryInspectVariable(ExecutionEnvironment env, string name, out Vb6Value value) =>
        TryRead(env, name, out value, inspecting: true);

    private bool TryRead(ExecutionEnvironment env, string name, out Vb6Value value, bool inspecting)
    {
        if (env.TryGetVariableLocation(name, out var loc))
        {
            if (!inspecting && state.PendingControlOf(loc) is { } form)
                throw new FormLoadNeeded(form, name);
            if (inspecting)
            {
                if (!state.TryRead(loc, out value))
                    return false;
            }
            else
                value = state[loc];
            // A form's name holding no instance makes one where it's read, as VB6's predeclared name does (#822). Only
            // through the name: a ByRef parameter aliasing the slot reads the Nothing it holds.
            if (!inspecting && value.Type == Vb6Value.ValueType.Object && value.Value is null
                && state.DefaultFormOf(loc) is { } module
                && string.Equals(name, module.Name, StringComparison.OrdinalIgnoreCase))
                throw new FormInstanceNeeded(module);
            // A variable declared As New holding Nothing makes its object where it's read, any read (#435). Not through a
            // parameter, the one other name a slot is reached by: a ByRef parameter aliasing the slot reads the Nothing it
            // holds, even one of the same name (measured: `Set x = Nothing` in the callee, then `x Is Nothing` there, is
            // True).
            if (!inspecting && value.Type == Vb6Value.ValueType.Object && value.Value is null
                && state.AutoNewOf(loc) is { } autoNew && !env.Parameters.Contains(name))
                throw new AutoInstanceNeeded(loc, autoNew);
            // A slot with a declared type is a ceiling: overflowing it is Err 6, where a Variant would widen.
            // Marked on the VALUE because fixedness propagates through sub-expressions — `(a + 0) * 3` with a
            // declared `a` still raises Err 6, measured. (#122)
            //
            // Set AND cleared, because the flag rides on the value and would otherwise be inherited from
            // whatever was stored: `a = 30000` puts a LITERAL (fixed) into an undeclared slot, and reading it
            // back as fixed would make `a * b` overflow where VB6 widens. The slot decides, not the history.
            //
            // An untyped or Variant parameter is a Variant, though it may share a caller's typed slot: measured, #1001, a
            // `v As Variant` or an untyped `v` given a `Dim n As Integer` holding 5 widens `v * 7000` to a Long 35000. The
            // name decides that, since the slot is the caller's. A slot known to be a Variant reads as a Variant's too
            // (#987), and that includes a Const declared As Variant. Every other Const, and an Enum member, is typed
            // (measured, #1005: `K * m` with `Const K = 5` and `m As Integer` 7000 overflows, and `Select Case mFive`
            // converts `Case 4.6` into 5). A slot that is none of these, such as an object's, reads as neither, and
            // compares as it always has.
            value = env.VariantParameters.Contains(name) ? value.AsVariant()
                : state.DeclaredTypeOf(loc) != null ? value.AsFixedType()
                : state.IsVariant(loc) ? value.AsVariant()
                : state.IsConstant(loc) || state.IsReadOnly(loc) ? value.AsFixedType()
                : value.AsVariantSubtype();
            // And the NAME the slot was declared with, for a class-typed or Object slot — set and cleared by the same
            // rule and for the same reason. This is what an `As IFoo` read carries into member dispatch.
            value = value.ViewedAs(state.DeclaredViewOf(loc));
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Allocate a slot for <paramref name="name"/>. Pass <paramref name="declaredType"/> for a
    /// variable declared <c>As T</c> with a coercible scalar T; leave it null for a Variant, an array, an
    /// object or a UDT, whose slots are replaced wholesale rather than coerced.</summary>
    public void AllocVariable(ExecutionEnvironment env, string name, Vb6Value value,
        Vb6Value.ValueType? declaredType = null, string? declaredClass = null)
    {
        var loc = state.Alloc(value, declaredType, declaredClass);
        env.AllocatedSlots?.Add(loc);
        env.DefineVariable(name, loc);
    }
}
