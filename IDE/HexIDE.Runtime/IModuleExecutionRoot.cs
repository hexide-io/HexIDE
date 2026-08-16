using System.Collections.Generic;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime;

public interface IModuleExecutionRoot
{
    // args carries a control-array event's leading `Index As Integer` (null for a parameterless handler).
    void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null);
}