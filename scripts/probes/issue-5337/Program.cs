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
var timed = new List<(string Name, Func<double, double> Fast, Func<double, double> Exact)>();
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
    timed.Add((name, fast, exact));
}

// Every function is warmed before any is timed, and each pass times all four in turn, so whichever is listed first
// can't be timed before the JIT has optimized the code the four share (#5590).
var inputs = Enumerable.Range(1, 10000).Select(i => i / 100.0).ToArray();
double sink = 0;
foreach (var (_, fast, exact) in timed)
    for (int i = 0; i < inputs.Length; ++i)
        sink += fast(inputs[i]) + exact(inputs[i]);
var fastNs = new double[timed.Count];
var exactNs = new double[timed.Count];
Array.Fill(fastNs, double.MaxValue);
Array.Fill(exactNs, double.MaxValue);
// The fastest of five passes each, so one preemption on a loaded runner can't decide the ratio.
for (int pass = 0; pass < 5; ++pass)
{
    for (int f = 0; f < timed.Count; ++f)
    {
        var (_, fast, exact) = timed[f];
        var watch = Stopwatch.StartNew();
        foreach (double x in inputs)
            sink += fast(x);
        fastNs[f] = Math.Min(fastNs[f], watch.Elapsed.TotalNanoseconds / inputs.Length);
        watch.Restart();
        foreach (double x in inputs)
            sink += exact(x);
        exactNs[f] = Math.Min(exactNs[f], watch.Elapsed.TotalNanoseconds / inputs.Length);
    }
}
for (int f = 0; f < timed.Count; ++f)
{
    Console.WriteLine($"{timed[f].Name}: fast {fastNs[f] / 1000:F3} us, exact {exactNs[f] / 1000:F3} us; ratio {fastNs[f] / exactNs[f]:F4}; checksum {sink:R}");
    if (fastNs[f] > exactNs[f] / 10)
        Environment.ExitCode = 1;
}
