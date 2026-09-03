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
    public bool TryUpdateVariable(ExecutionEnvironment env, string name, Vb6Value value, ParserRuleContext? ctx = null)
    {
        if (env.TryGetVariableLocation(name, out var loc))
        {
            // An Enum member is a constant. VB6 refuses `pTwo = 5` at compile time with "Assignment to
            // constant not permitted"; the walk can only refuse it here, as the statement runs, which is
            // the translation interpreter-core:40-42 prescribes. Checked before the coercion, because the
            // write is not going to happen and a coercion error would name the wrong problem.
            if (state.IsReadOnly(loc))
                throw new VBCompileErrorException($"Assignment to constant not permitted ({name})");

            if (state.DeclaredTypeOf(loc) is { } declared)
                value = VbNumeric.CoerceOnStore(value, declared, ctx);
            // A class-typed slot enforces its name: the object must BE that class or implement it as an
            // interface. Anything else is Err 13 "Type mismatch" at the Set — measured, both for two unrelated
            // classes and for an interface-typed slot given a non-implementer. Nothing always stores (its Value
            // is null); a control or proxy is outside this model.
            if (state.DeclaredClassOf(loc) is { } declaredClass
                && value.Value is VbObject incoming
                && !VbInterface.IsAssignableTo(incoming, declaredClass))
                throw new VBRunTimeException(ctx, VBStandardError.TypeMismatch);
            state[loc] = value;
            return true;
        }

        return false;
    }

    public bool TryGetVariable(ExecutionEnvironment env, string name, out Vb6Value value)
    {
        if (env.TryGetVariableLocation(name, out var loc))
        {
            value = state[loc];
            // A slot with a declared type is a ceiling: overflowing it is Err 6, where a Variant would widen.
            // Marked on the VALUE because fixedness propagates through sub-expressions — `(a + 0) * 3` with a
            // declared `a` still raises Err 6, measured. (#122)
            //
            // Set AND cleared, because the flag rides on the value and would otherwise be inherited from
            // whatever was stored: `a = 30000` puts a LITERAL (fixed) into an undeclared slot, and reading it
            // back as fixed would make `a * b` overflow where VB6 widens. The slot decides, not the history.
            value = state.DeclaredTypeOf(loc) != null ? value.AsFixedType() : value.AsVariantSubtype();
            // And the NAME the slot was declared with, for a class-typed slot — set and cleared by the same
            // rule and for the same reason. This is what an `As IFoo` read carries into member dispatch.
            value = value.ViewedAs(state.DeclaredClassOf(loc));
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
        env.DefineVariable(name, loc);
    }
}
