using System;
using System.Collections.Generic;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime;

public interface IModuleExecutionRoot
{
    // args carries a control-array event's leading `Index As Integer` (null for a parameterless handler).
    void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null);

    /// <summary>
    /// Whether the module has a procedure of this name. An event raised as often as MouseMove is skipped where nothing
    /// handles it, rather than dispatched to find out.
    /// </summary>
    bool Handles(string name) => true;

    /// <summary>
    /// Raise an event whose arguments are worked out as it is dispatched (#974). One HexIDE can't work out, such as a
    /// mouse position in a ScaleMode it doesn't convert, is then refused as the run's own error, reported like any other,
    /// rather than thrown out of an input handler.
    /// </summary>
    void ExecuteEvent(string name, Func<IReadOnlyList<Vb6Value>> args) => ExecuteSub(name, args());
}
