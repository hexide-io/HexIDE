using System.Collections.Generic;
using System.Threading.Tasks;

namespace HexIDE.Runtime.Interpreter;

public interface ICSharpProxy
{
    void Call(string method, List<Vb6Value> args);
}

/// <summary>A host object whose methods can await platform work and return a VB6 value.</summary>
public interface ICSharpAsyncProxy : ICSharpProxy
{
    Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args);
}
