using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// The slots of <c>Static</c> locals, which outlive the call that declared them (#1491). One slot per declaration,
/// made the first time its procedure runs and found again by every later call, a recursive one included, which is why
/// a recursive Sub's Static counts across its own frames (measured). A standard module's procedures keep one set per
/// run (<see cref="ExecutionState.ModuleStatics"/>); a class's or a form's keep one set per instance
/// (<see cref="VbObject.Statics"/>), so a second instance starts again from zero, and so does a form's default
/// instance after <c>Set Form1 = Nothing</c>, though not after <c>Unload</c> (measured).
///
/// Collection, not analysis: a slot is found by the declaration's own parse node, and nothing relates one declaration
/// to another.
/// </summary>
internal sealed class StaticLocals
{
    private readonly Dictionary<VB6Parser.VariableSubStmtContext, int> slots = new();

    public bool TryGet(VB6Parser.VariableSubStmtContext declaration, out int slot) => slots.TryGetValue(declaration, out slot);

    public void Add(VB6Parser.VariableSubStmtContext declaration, int slot) => slots[declaration] = slot;

    /// <summary>
    /// The slots in the order the module declares them, which is the order an instance's Statics are released in, after
    /// its fields (measured: three methods' Statics made in the order Hold, B, A terminate in the source's order, Hold,
    /// A, B).
    /// </summary>
    public List<int> InSourceOrder()
    {
        var declarations = new List<VB6Parser.VariableSubStmtContext>(slots.Keys);
        declarations.Sort((a, b) => a.Start.TokenIndex.CompareTo(b.Start.TokenIndex));
        var ordered = new List<int>(declarations.Count);
        foreach (var declaration in declarations)
            ordered.Add(slots[declaration]);
        return ordered;
    }

    /// <summary>
    /// Whether a local is Static: declared with <c>Static</c>, or with <c>Dim</c> in a procedure declared
    /// <c>Static</c>, whose locals all are, though its parameters and a Function's own return value are not (measured).
    /// </summary>
    public static bool Declares(VB6Parser.VariableSubStmtContext local)
    {
        if (local.Parent?.Parent is VB6Parser.VariableStmtContext { } statement && statement.STATIC() != null)
            return true;
        for (var node = local.Parent; node != null; node = node.Parent)
        {
            switch (node)
            {
                case VB6Parser.SubStmtContext sub: return sub.STATIC() != null;
                case VB6Parser.FunctionStmtContext function: return function.STATIC() != null;
                case VB6Parser.PropertyGetStmtContext get: return get.STATIC() != null;
                case VB6Parser.PropertyLetStmtContext let: return let.STATIC() != null;
                case VB6Parser.PropertySetStmtContext set: return set.STATIC() != null;
            }
        }
        return false;
    }
}
