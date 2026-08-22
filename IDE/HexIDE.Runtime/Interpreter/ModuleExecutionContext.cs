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
            if (state.DeclaredTypeOf(loc) is { } declared)
                value = VbNumeric.CoerceOnStore(value, declared, ctx);
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
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Allocate a slot for <paramref name="name"/>. Pass <paramref name="declaredType"/> for a
    /// variable declared <c>As T</c> with a coercible scalar T; leave it null for a Variant, an array, an
    /// object or a UDT, whose slots are replaced wholesale rather than coerced.</summary>
    public void AllocVariable(ExecutionEnvironment env, string name, Vb6Value value,
        Vb6Value.ValueType? declaredType = null)
    {
        var loc = state.Alloc(value, declaredType);
        env.DefineVariable(name, loc);
    }
}
