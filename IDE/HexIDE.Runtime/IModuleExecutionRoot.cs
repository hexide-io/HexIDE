using System.Collections.Generic;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime;

public interface IModuleExecutionRoot
{
    // args carries a control-array event's leading `Index As Integer` (null for a parameterless handler).
    void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null);

    /// <summary>
    /// A Timer control's tick. While that Timer's own handler runs the tick is dropped, and while another handler runs
    /// at most one waits (measured, #766). A root with no queue of its own runs it as any event.
    /// </summary>
    void ExecuteTimer(string name, IReadOnlyList<Vb6Value>? args = null) => ExecuteSub(name, args);
}