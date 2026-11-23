using System.Diagnostics;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Tests;

bool benchmarkOnly = args.Contains("--benchmark");
int count = benchmarkOnly ? 0 : args.Length == 0 ? 20000 : int.Parse(args[0]);
var functions = new (string Name, Func<double, double> Fast, Func<double, double> Exact)[]
{
    ("Sin", X87Math.Sin, X87Math.SinExact),
    ("Cos", X87Math.Cos, X87Math.CosExact),
    ("Tan", X87Math.Tan, X87Math.TanExact),
    ("Atn", X87Math.Atan, X87Math.AtanExact)
};
var values = X87MathArguments.Create(count);
foreach (var (name, fast, exact) in functions)
{
    if (!benchmarkOnly && args.Length > 1 && args[1] != name)
        continue;
    int mismatches = 0;
    int completed = 0;
    foreach (double x in values)
    {
        if (++completed % 1000000 == 0)
            Console.WriteLine($"{name}: {completed} checked, {mismatches} disagreements so far");
        double a = fast(x), b = exact(x);
        if (BitConverter.DoubleToUInt64Bits(a) != BitConverter.DoubleToUInt64Bits(b))
        {
            if (++mismatches <= 10)
                Console.WriteLine($"{name} {x:R}: {a:R} != {b:R}");
        }
    }
    Console.WriteLine($"{name}: {count} comparisons, {mismatches} disagreements");
    if (mismatches != 0)
        Environment.ExitCode = 1;
    var inputs = Enumerable.Range(1, 10000).Select(i => i / 100.0).ToArray();
    double sink = 0;
    for (int i = 0; i < 10000; ++i)
        sink += fast(inputs[i]) + exact(inputs[i]);
    // The fastest of three rounds each, so one preemption on a loaded runner can't decide the ratio.
    double fastNs = double.MaxValue, exactNs = double.MaxValue;
    for (int round = 0; round < 3; ++round)
    {
        var watch = Stopwatch.StartNew();
        foreach (double x in inputs)
            sink += fast(x);
        fastNs = Math.Min(fastNs, watch.Elapsed.TotalNanoseconds / inputs.Length);
        watch.Restart();
        foreach (double x in inputs)
            sink += exact(x);
        exactNs = Math.Min(exactNs, watch.Elapsed.TotalNanoseconds / inputs.Length);
    }
    Console.WriteLine($"{name}: fast {fastNs / 1000:F3} us, exact {exactNs / 1000:F3} us; ratio {fastNs / exactNs:F4}; checksum {sink:R}");
    if (fastNs > exactNs / 10)
        Environment.ExitCode = 1;
}
