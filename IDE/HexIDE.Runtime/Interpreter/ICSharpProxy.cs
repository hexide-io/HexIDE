using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

public interface ICSharpProxy
{
    void Call(string method, List<Vb6Value> args);
}