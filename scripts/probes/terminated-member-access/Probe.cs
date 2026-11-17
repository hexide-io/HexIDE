using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HexIDE.Runtime.Interpreter;

internal static class TerminatedAccessProbe
{
    private static readonly object Gate = new();
    private static long hits;

    [ModuleInitializer]
    internal static void Initialize()
    {
        var directory = Environment.GetEnvironmentVariable("HEXIDE_TERMINATED_ACCESS_REPORT");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Environment.ProcessId}.jsonl");
        void Write(object value)
        {
            lock (Gate) File.AppendAllText(path, JsonSerializer.Serialize(value) + Environment.NewLine);
        }
        Write(new { kind = "start", project = Environment.GetEnvironmentVariable("HEXIDE_SMOKE_PROJECT") });
        BasicInterpreter.TerminatedMemberAccessObserved = hit =>
        {
            var test = Xunit.TestContext.Current.Test?.TestDisplayName ?? "outside a test";
            // The diagnostic's own deliberate stale references aren't reference-count bugs in programs.
            bool forced = test.Contains("TerminatedMemberAccessTests", StringComparison.Ordinal);
            lock (Gate)
            {
                if (!forced) hits++;
                Write(new { kind = forced ? "forced" : "hit", test, hit.Module, hit.Member, hit.Line,
                    project = Environment.GetEnvironmentVariable("HEXIDE_SMOKE_PROJECT") });
            }
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Write(new { kind = "end", hits });
            Console.WriteLine($"#4375 terminated member accesses: {hits}");
        };
    }
}
