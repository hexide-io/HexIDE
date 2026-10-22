using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// The runtime's <c>Debug</c> object. <c>Debug.Print</c> is read where it is written, as <c>Print #</c> is, so that its
/// separators survive (#1380): the statement prints its list through this object as a <see cref="VbPrint.ITarget"/>.
/// Two channels reach the host, an <see cref="IBasicStandardLibrary"/>: <see cref="IBasicStandardLibrary.DebugPrint"/>
/// takes each item's typed value, which the test suites assert on, and <see cref="IBasicStandardLibrary.DebugWrite"/> the
/// text as the Immediate window shows it. The column is the host's, <see cref="IBasicStandardLibrary.DebugColumn"/>, so
/// it is one for the whole program and carries from one run to the next. Seeded into the root environment by
/// <see cref="BasicInterpreter"/> so <c>Debug.Print</c> works in the live F5 run, not only in the test fixture.
/// </summary>
public class DebugProxy(IBasicStandardLibrary stdLib) : ICSharpProxy, VbPrint.ITarget
{
    private readonly IBasicStandardLibrary stdLib = stdLib;

    int VbPrint.ITarget.Column => stdLib.DebugColumn;

    void VbPrint.ITarget.Write(string text) => stdLib.DebugWrite(text);

    /// <summary>An item the statement printed, to the typed channel.</summary>
    internal void Printed(Vb6Value value) => stdLib.DebugPrint(value);

    /// <summary>A bare <c>Debug.Print</c>: a line break, and an empty String to the typed channel.</summary>
    internal void PrintBlankLine()
    {
        stdLib.DebugPrint(new Vb6Value(""));
        VbPrint.EndLine(this);
    }

    public void Call(string method, List<Vb6Value> args)
    {
        // Reached only where Debug.Print isn't a statement of its own, so there is no separator to read: each argument is
        // printed after the one before, and the line ends.
        if (string.Equals(method, "Print", StringComparison.OrdinalIgnoreCase))   // `debug.print` too (#811)
        {
            if (args.Count == 0)
            {
                PrintBlankLine();
                return;
            }
            foreach (var arg in args)
            {
                // An object prints its default member, and one with none is 438 (ObjectAsValue).
                var value = ObjectAsValue.Scalar(arg, null);
                Printed(value);
                VbPrint.Item(this, value);
            }
            VbPrint.EndLine(this);
        }
        else
            // Debug.Assert and anything else VB6's Debug object has. A NotImplementedException so a run that reaches
            // one says HexIDE does not implement it, rather than reporting an internal error (#754).
            throw new NotImplementedException($"Debug.{method} is not implemented");
    }
}
