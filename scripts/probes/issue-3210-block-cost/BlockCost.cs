using System.Diagnostics;
using HexIDE.Runtime.Interpreter;

var depth = int.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture);
var source = "Sub Main()\nDim v\n" + string.Concat(Enumerable.Repeat("If True Then\n", depth))
    + "v = 1\n" + string.Concat(Enumerable.Repeat("End If\n", depth)) + "End Sub\n";
using var process = Process.GetCurrentProcess();
process.Refresh();
var before = process.PrivateMemorySize64;
var watch = Stopwatch.StartNew();
new SyntaxChecker().Run(source);
watch.Stop();
process.Refresh();
var after = process.PrivateMemorySize64;
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
process.Refresh();
Console.WriteLine($"depth={depth}, seconds={watch.Elapsed.TotalSeconds:F3}, private bytes={before} -> {after} -> {process.PrivateMemorySize64}");
