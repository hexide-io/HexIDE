// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

using HexIDE.IDE;
using HexIDE.Runtime.Interpreter;
using HexIDE.VbLspServer;

// Run ONE case per process: StackOverflowException cannot be caught. The caller owns the timeout.
if (args.Length != 3 || !int.TryParse(args[2], out var depth) || depth < 1)
{
    Console.Error.WriteLine("Usage: ParseDepthProbe <runtime|lsp|symbols|types|scope|source> <chain|continuations|continued-chain> <depth>");
    return 2;
}

var expression = args[1] switch
{
    "chain" => string.Concat(Enumerable.Repeat("a+", depth)) + "a",
    "continuations" => string.Concat(Enumerable.Repeat(" _\n", depth)) + "a",
    "continued-chain" => string.Concat(Enumerable.Repeat("a+ _\n", depth)) + "a",
    _ => throw new ArgumentException("Unknown shape")
};
var source = "Sub Main()\nDim a As Long\na = 1&\nDim x\nx = " + expression + "\nEnd Sub\n";
if (args[0] == "source")
{
    Console.Write(source);
    return 0;
}
Console.WriteLine($"{args[0]} {args[1]} {depth}: {source.Length} characters");
try
{
    if (args[0] == "runtime")
    {
        Console.WriteLine("construct / parse / pre-pass");
        var interpreter = new BasicInterpreter(new ProbeLibrary(), new ModuleExecutionContext(),
            new ExecutionEnvironment(), source);
        Console.WriteLine("execute");
        await interpreter.RunStartupSubMain();
    }
    else if (args[0] is "lsp" or "symbols" or "types" or "scope")
    {
        Console.WriteLine("diagnostics / parse / protected regions");
        var (diagnostics, tree) = VbDiagnosticsProvider.GetDiagnosticsAndTree(source);
        Console.WriteLine($"diagnostics: {diagnostics.Count}; tree: {tree is not null}");
        foreach (var diagnostic in diagnostics) Console.WriteLine(diagnostic.Message);
        if (args[0] == "symbols")
            Console.WriteLine(VbSymbolProvider.GetSymbols(tree).Count);
        if (tree is not null && args[0] == "types")
            Console.WriteLine(VbScopeAnalyzer.GetDeclaredTypes(tree).Count);
        if (tree is not null && args[0] == "scope")
            Console.WriteLine(VbScopeAnalyzer.GetOptionExplicitDiagnostics(tree).Count);
        Console.WriteLine("folding");
        Console.WriteLine(VbFoldingProvider.GetFoldings(source).Count);
    }
    else throw new ArgumentException("Unknown host");
    Console.WriteLine("completed");
}
catch (Exception exception)
{
    Console.WriteLine($"caught {exception.GetType().Name}: {exception.Message}");
    return 1;
}
return 0;

sealed class ProbeLibrary : IBasicStandardLibrary
{
    public void DebugPrint(Vb6Value value) { }
    public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        => throw new NotSupportedException();
    public Task<string?> InputBox(string prompt, string? title, string defaultText)
        => throw new NotSupportedException();
}
