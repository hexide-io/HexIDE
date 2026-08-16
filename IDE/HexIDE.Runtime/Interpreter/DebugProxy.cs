using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// The runtime's <c>Debug</c> object. A statement like <c>Debug.Print expr</c> resolves the leading
/// <c>Debug</c> name to this proxy and lands here as <see cref="Call"/> with method <c>"Print"</c>; the value
/// is handed to the host's <see cref="IBasicStandardLibrary.DebugPrint"/> — the Immediate window when running
/// in the IDE, or a capture list under test. Seeded into the root environment by
/// <see cref="BasicInterpreter"/> so <c>Debug.Print</c> works in the live F5 run, not only in the test fixture.
/// </summary>
public class DebugProxy(IBasicStandardLibrary stdLib) : ICSharpProxy
{
    private readonly IBasicStandardLibrary stdLib = stdLib;

    public void Call(string method, List<Vb6Value> args)
    {
        if (method == "Print")
            // args[0] preserves the typed value (so tests keep asserting on the real Vb6Value);
            // a bare `Debug.Print` with no argument prints a blank line.
            stdLib.DebugPrint(args.Count > 0 ? args[0] : new Vb6Value(""));
        else
            throw new Exception("No method named " + method);
    }
}
