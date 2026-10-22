using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HexIDE.Dap;

// Explicit opt-in tool (#1368). Runs each program of a HexIDE.Runtime.Tests dump (HEXIDE_DUMP_PROGRAMS) under an
// external VB6 engine's debug adapter, one adapter process per program, and compares what each printed with what
// HexIDE printed. It uses an installed adapter named by HEXIDE_PARITY_ADAPTER, and never downloads or bundles one.
// The adapter is started as `<adapter> --dap --workspace <dir>` and launched with `module` and `entryPoint`, as in
// docs/dap-adapter-smoke.md.
var options = Options.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("""
        Usage: HexIDE.Dap.Parity <dump.jsonl> [--label <text>] [--timeout <seconds>] [--parallel <n>]
                                 [--project-template <file>] [--out <dir>] [--dry-run]

        Set HEXIDE_PARITY_ADAPTER to the debug adapter's executable. --dry-run writes the workspaces and starts nothing.
        Results go to <out>/results.jsonl, one line per program as it finishes, and the counts to <out>/summary.json.
        """);
    return 2;
}
var adapter = Environment.GetEnvironmentVariable("HEXIDE_PARITY_ADAPTER");
if (!options.DryRun && (string.IsNullOrEmpty(adapter) || !File.Exists(adapter)))
{
    Console.Error.WriteLine("HEXIDE_PARITY_ADAPTER must name the debug adapter's executable; nothing was run.");
    return 2;
}

var cases = File.ReadLines(options.Dump).Select((line, i) => (Index: i + 1, Record: JsonNode.Parse(line)!.AsObject())).ToList();
var outDir = Path.GetFullPath(options.Out ?? Path.Combine(RepoRoot(), "artifacts", "dap-parity", DateTime.Now.ToString("yyyyMMdd-HHmmss")));
Directory.CreateDirectory(outDir);
var template = options.ProjectTemplate is { } t ? File.ReadAllText(t) : DefaultProject;
// Readable output: a label such as "f3acd864 + fix" keeps its plus sign, and a test name its quotes.
var json = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var indented = new JsonSerializerOptions(json) { WriteIndented = true };
var results = new StreamWriter(Path.Combine(outDir, "results.jsonl"), append: false, new UTF8Encoding(false));
var gate = new object();
void Write(JsonObject line)
{
    lock (gate)
    {
        results.WriteLine(line.ToJsonString(json));
        results.Flush();
    }
}
Write(new JsonObject
{
    ["kind"] = "header",
    ["label"] = options.Label,
    ["dump"] = Path.GetFullPath(options.Dump),
    ["adapter"] = adapter is null ? null : Path.GetFileName(adapter),
    ["timeoutSeconds"] = options.TimeoutSeconds,
    ["parallel"] = options.Parallel,
    ["programs"] = cases.Count,
});

var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
// A program left out for several reasons counts under each.
var exclusions = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
var done = 0;
var clock = Stopwatch.StartNew();
using var slots = new SemaphoreSlim(options.Parallel);
await Task.WhenAll(cases.Select(async c =>
{
    await slots.WaitAsync();
    try
    {
        var result = await RunCase(c.Index, c.Record);
        Write(result);
        lock (gate)
        {
            var outcome = result["outcome"]!.GetValue<string>();
            counts[outcome] = counts.GetValueOrDefault(outcome) + 1;
            foreach (var reason in result["excludedFor"]?.AsArray() ?? [])
                exclusions[reason!.GetValue<string>()] = exclusions.GetValueOrDefault(reason!.GetValue<string>()) + 1;
            if (++done % 50 == 0 || done == cases.Count)
                Console.WriteLine($"{done}/{cases.Count} in {clock.Elapsed:hh\\:mm\\:ss}");
        }
    }
    finally
    {
        slots.Release();
    }
}));
results.Dispose();

var summary = new JsonObject
{
    ["label"] = options.Label,
    ["programs"] = cases.Count,
    ["elapsed"] = clock.Elapsed.ToString(@"hh\:mm\:ss"),
    ["outcomes"] = new JsonObject(counts.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))),
    ["excludedFor"] = new JsonObject(exclusions.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))),
};
File.WriteAllText(Path.Combine(outDir, "summary.json"), summary.ToJsonString(indented));
Console.WriteLine(summary.ToJsonString(indented));
Console.WriteLine($"Results: {outDir}");
return 0;

async Task<JsonObject> RunCase(int index, JsonObject record)
{
    var modules = record["modules"]!.AsArray().Select(m => m!.AsObject()).ToList();
    var startup = modules[0];
    var result = new JsonObject
    {
        ["kind"] = "program",
        ["index"] = index,
        ["test"] = record["test"]?.GetValue<string>(),
        ["sequence"] = record["sequence"]?.GetValue<int>(),
    };
    var hexide = Hexide.From(record);
    result["hexide"] = hexide.ToJson();
    if (startup["bare"]?.GetValue<bool>() != true)
    {
        // HexIDE runs a module with no bare statements as its declarations alone, so there is nothing to call.
        result["outcome"] = "no-entry";
        return result;
    }
    // The engine runs the program for real, on the machine running this tool. A program that would act outside its own
    // process is counted and left out, whatever its paths say: a path built at run time can't be checked here.
    var effects = Effects.Of(modules.Select(m => m["source"]!.GetValue<string>()));
    if (effects.Count > 0)
    {
        result["outcome"] = "excluded";
        result["excludedFor"] = new JsonArray(effects.Select(e => (JsonNode?)e).ToArray());
        return result;
    }

    var dir = Path.Combine(outDir, "cases", index.ToString("D5"));
    Directory.CreateDirectory(dir);
    var files = new JsonArray();
    for (var i = 0; i < modules.Count; i++)
    {
        var m = modules[i];
        var name = m["name"]!.GetValue<string>();
        var kind = m["kind"]!.GetValue<string>();
        // The startup module as an engine that starts only at a named procedure can call it; the others as written,
        // since only the startup module has statements of its own.
        var source = i == 0 ? m["wrapped"]!.GetValue<string>() : m["source"]!.GetValue<string>();
        var file = name + (kind == "class" ? ".cls" : ".bas");
        File.WriteAllText(Path.Combine(dir, file), ModuleText(name, kind, source), new UTF8Encoding(false));
        files.Add(new JsonObject { ["RelativeUri"] = file });
    }
    var project = JsonNode.Parse(template)!.AsObject();
    var info = project["ProjectInfo"]!.AsObject();
    info["Name"] = "ParityCase";
    info["Modules"] = files;
    File.WriteAllText(Path.Combine(dir, ".rdproj"), project.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    if (options.DryRun)
    {
        result["outcome"] = "dry-run";
        return result;
    }

    var engine = await RunEngine(dir, startup["name"]!.GetValue<string>());
    result["engine"] = engine.ToJson();
    result["outcome"] = Outcome(hexide, engine);
    result["elapsedMs"] = engine.ElapsedMs;
    return result;
}

async Task<Engine> RunEngine(string workspace, string module)
{
    var engine = new Engine();
    var clock = Stopwatch.StartNew();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
    var token = timeout.Token;
    var terminated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    DapSession? session = null;
    Task? observer = null;
    try
    {
        session = DapSession.Start(new(adapter!)
        {
            Arguments = ["--dap", "--workspace", workspace],
            // The case's own workspace, so a relative path the engine resolves lands there. Programs that touch the
            // file system are left out anyway (Effects); this is the second line.
            WorkingDirectory = workspace,
            ShutdownTimeout = TimeSpan.FromSeconds(5),
        });
        var running = session;
        observer = Task.Run(async () =>
        {
            await foreach (var update in running.Updates.ReadAllAsync(token))
            {
                if (update.Event is not { } e)
                    continue;
                var body = e.Body;
                switch (e.Event)
                {
                    case "output" when body is { } b && b.TryGetProperty("output", out var text):
                        var category = b.TryGetProperty("category", out var c) ? c.GetString() : null;
                        (category == "stderr" ? engine.Stderr : engine.Stdout).Append(text.GetString());
                        break;
                    case "exited" when body is { } b && b.TryGetProperty("exitCode", out var code):
                        engine.ExitCode = code.GetInt32();
                        break;
                    case "stopped" when update.Snapshot.Stop is { } stop:
                        // An unhandled error stops under the adapter's default filter. Continuing lets the engine
                        // report it as a run-time error and end, as it does with no filter.
                        engine.Stops.Add(body is { } sb && sb.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "");
                        if (engine.Stops.Count > 20)
                            throw new InvalidOperationException("The program stopped more than 20 times.");
                        await running.ResumeAsync(stop, "continue", stop.ThreadId ?? 1, token);
                        break;
                    case "terminated":
                        terminated.TrySetResult();
                        break;
                }
            }
        }, token);
        await session.LaunchAsync("external", JsonSerializer.SerializeToElement(new { module, entryPoint = "HexDumpMain" }), [], token);
        await terminated.Task.WaitAsync(token);
        using var bye = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await session.DisconnectAsync(bye.Token); } catch (Exception) { /* the program already ended */ }
    }
    catch (OperationCanceledException) when (timeout.IsCancellationRequested)
    {
        engine.TimedOut = true;
    }
    catch (Exception e)
    {
        engine.Failure = $"{e.GetType().Name}: {e.Message}";
    }
    finally
    {
        if (session is not null)
        {
            await session.DisposeAsync();
            try
            {
                var process = await session.ProcessCompletion!.WaitAsync(TimeSpan.FromSeconds(30));
                engine.ProcessExitCode = process.ExitCode;
                engine.Forced = process.ForcedTermination;
                if (engine.Failure is not null || engine.TimedOut)
                    engine.StderrTail = process.StandardErrorTail;
            }
            catch (Exception e)
            {
                engine.Failure ??= $"cleanup: {e.GetType().Name}: {e.Message}";
            }
        }
        if (observer is not null)
            try { await observer; } catch (Exception) { /* cancelled with the session */ }
    }
    engine.ElapsedMs = clock.ElapsedMilliseconds;
    return engine;
}

static string Outcome(Hexide hexide, Engine engine)
{
    if (engine.TimedOut) return "timeout";
    if (engine.Failure is not null) return "launch-failed";
    var printedAlike = Lines(engine.Stdout.ToString()).SequenceEqual(hexide.Printed.Select(p => p.Trim()));
    var engineError = engine.ExitCode is { } code && code != 0 || engine.Stderr.Length > 0;
    var engineNumber = engine.ErrorNumber;
    return hexide.ErrorKind switch
    {
        null => !engineError ? (printedAlike ? "match" : "mismatch") : "engine-error-only",
        "VBRunTimeException" => !engineError ? "hexide-error-only"
            : engineNumber is null ? "hexide-run-time-error-engine-refused"
            : engineNumber == hexide.ErrorNumber && printedAlike ? "both-error-match" : "both-error-differ",
        "NotImplementedException" => !engineError ? "hexide-unimplemented-engine-runs" : "hexide-unimplemented-engine-error",
        // Compile errors, and the run-time translations of VB6's compile errors (Variable not defined and the rest).
        _ => !engineError ? "hexide-refused-engine-runs" : "both-refused",
    };
}

static IEnumerable<string> Lines(string text)
{
    var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
    if (lines.Count > 0 && lines[^1].Length == 0)
        lines.RemoveAt(lines.Count - 1);
    return lines.Select(l => l.Trim());
}

static string ModuleText(string name, string kind, string source)
{
    var text = new StringBuilder();
    if (kind == "class" && !source.TrimStart().StartsWith("VERSION", StringComparison.OrdinalIgnoreCase))
        // Without the class header the engine reads the file as a standard module (measured, #1368).
        text.Append("VERSION 1.0 CLASS\nBEGIN\n  MultiUse = -1  'True\nEND\n");
    if (!source.Contains("Attribute VB_Name", StringComparison.OrdinalIgnoreCase))
        text.Append($"Attribute VB_Name = \"{name}\"\n");
    text.Append(source);
    return text.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
}

static string RepoRoot()
{
    for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
        if (Directory.Exists(Path.Combine(dir.FullName, "IDE")) && Directory.Exists(Path.Combine(dir.FullName, "LspServer")))
            return dir.FullName;
    return Environment.CurrentDirectory;
}

partial class Program
{
    // The project file docs/dap-adapter-smoke.md describes, with no references: the intrinsics need none (measured).
    const string DefaultProject = """
        {
          "Version": "0.1.0",
          "Configuration": [],
          "ProjectInfo": { "Name": "", "References": [], "Modules": [], "OtherFiles": [], "Folders": [], "PrecompilerConstants": {} }
        }
        """;
}

sealed record Options(string Dump, string? Label, int TimeoutSeconds, int Parallel, string? ProjectTemplate, string? Out, bool DryRun)
{
    public static Options? Parse(string[] args)
    {
        string? dump = null, label = null, template = null, output = null;
        int timeout = 60, parallel = 1;
        var dryRun = false;
        for (var i = 0; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (args[i])
            {
                case "--label": label = Next(); break;
                case "--timeout" when int.TryParse(Next(), out var s) && s > 0: timeout = s; break;
                case "--parallel" when int.TryParse(Next(), out var n) && n is > 0 and <= 32: parallel = n; break;
                case "--project-template": template = Next(); break;
                case "--out": output = Next(); break;
                case "--dry-run": dryRun = true; break;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal) || dump is not null)
                        return null;
                    dump = args[i];
                    break;
            }
        }
        return dump is not null && File.Exists(dump) ? new Options(dump, label, timeout, parallel, template, output, dryRun) : null;
    }
}

/// <summary>
/// What a program would do outside its own process, read from its source token by token. Nothing is bound, and the read
/// errs toward finding too much, since a false find only leaves a program out. Strings, comments and members
/// (<c>x.Name</c>) are skipped.
/// </summary>
static class Effects
{
    // Each acts outside the process whatever its arguments: a dialog, another program, a native call, an automation
    // object, the file system, the registry, the clipboard, the printer, the speaker.
    static readonly HashSet<string> Anywhere = new(StringComparer.OrdinalIgnoreCase)
    {
        "MsgBox", "InputBox", "Shell", "SendKeys", "Declare", "CreateObject", "GetObject", "AppActivate",
        "Kill", "RmDir", "MkDir", "FileCopy", "SetAttr", "ChDir", "ChDrive",
        "SaveSetting", "DeleteSetting", "Clipboard", "Printer", "Beep",
    };

    // Each is a statement only where a statement starts: `Name a As b` renames a file, `Open` opens one, and `Date = d`
    // and `Time = t` set the system clock. Elsewhere `Name` is a property and `Date` a function.
    static readonly HashSet<string> AsStatement = new(StringComparer.OrdinalIgnoreCase) { "Name", "Open", "Date", "Time", "Date$", "Time$" };

    public static SortedSet<string> Of(IEnumerable<string> sources)
    {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
            foreach (var line in Regex.Split(source.Replace("\r\n", "\n"), @" _\n").SelectMany(l => l.Split('\n')))
                Scan(line, found);
        return found;
    }

    static void Scan(string line, SortedSet<string> found)
    {
        var statementStart = true;
        var afterMember = false;
        for (var i = 0; i < line.Length;)
        {
            var c = line[i];
            if (c == '"')
            {
                // A string, with "" for a quote inside it.
                for (i++; i < line.Length; i++)
                {
                    if (line[i] != '"')
                        continue;
                    if (i + 1 < line.Length && line[i + 1] == '"')
                        i++;
                    else
                        break;
                }
                i++;
                statementStart = afterMember = false;
                continue;
            }
            if (c == '\'')
                return;
            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_'))
                    i++;
                // `Date$` and `Time$` are the string forms of the clock statements.
                if (i < line.Length && line[i] == '$')
                    i++;
                var word = line[start..i];
                var next = line[i..].TrimStart();
                if (statementStart && word.Equals("Rem", StringComparison.OrdinalIgnoreCase))
                    return;
                if (!afterMember)
                {
                    if (Anywhere.Contains(word))
                        found.Add(word);
                    else if (statementStart && AsStatement.Contains(word))
                    {
                        var isDateOrTime = word.TrimEnd('$').Equals("Date", StringComparison.OrdinalIgnoreCase)
                            || word.TrimEnd('$').Equals("Time", StringComparison.OrdinalIgnoreCase);
                        // `Date = d` is the statement; `Name = x` and `Open = x` assign a variable.
                        if (isDateOrTime == next.StartsWith('='))
                            found.Add(isDateOrTime ? word.TrimEnd('$') + " =" : word);
                    }
                }
                // A statement starts again after Then and Else: `If x Then Name a As b`.
                statementStart = word.Equals("Then", StringComparison.OrdinalIgnoreCase) || word.Equals("Else", StringComparison.OrdinalIgnoreCase);
                afterMember = false;
                continue;
            }
            if (char.IsDigit(c) && statementStart)
            {
                // A line number keeps the statement's start.
                while (i < line.Length && char.IsDigit(line[i]))
                    i++;
                continue;
            }
            if (c == ':')
                statementStart = true;
            else if (!char.IsWhiteSpace(c))
                statementStart = false;
            afterMember = c is '.' or '!' || (char.IsWhiteSpace(c) && afterMember);
            i++;
        }
    }
}

/// <summary>What HexIDE did with the program, from the dump.</summary>
sealed record Hexide(IReadOnlyList<string> Printed, IReadOnlyList<string> Types, string? ErrorKind, int? ErrorNumber, string? ErrorMessage)
{
    public static Hexide From(JsonObject record)
    {
        var printed = record["printed"]!.AsArray().Select(p => p!["text"]!.GetValue<string>()).ToList();
        var types = record["printed"]!.AsArray().Select(p => p!["type"]!.GetValue<string>()).ToList();
        var error = record["error"]?.AsObject();
        return new(printed, types, error?["kind"]?.GetValue<string>(), error?["number"]?.GetValue<int>(), error?["message"]?.GetValue<string>());
    }

    public JsonObject ToJson() => new()
    {
        ["printed"] = new JsonArray(Printed.Select(p => (JsonNode?)p).ToArray()),
        ["types"] = new JsonArray(Types.Select(p => (JsonNode?)p).ToArray()),
        ["errorKind"] = ErrorKind,
        ["errorNumber"] = ErrorNumber,
        ["errorMessage"] = ErrorMessage,
    };
}

/// <summary>What the engine did with the program, from its debug adapter.</summary>
sealed partial class Engine
{
    public StringBuilder Stdout { get; } = new();
    public StringBuilder Stderr { get; } = new();
    public List<string> Stops { get; } = [];
    public int? ExitCode { get; set; }
    public int? ProcessExitCode { get; set; }
    public bool Forced { get; set; }
    public bool TimedOut { get; set; }
    public string? Failure { get; set; }
    public string? StderrTail { get; set; }
    public long ElapsedMs { get; set; }

    // The engine reports an unhandled error as "Run-time error 'N': description" (measured, #1368).
    public int? ErrorNumber => RunTimeError().Match(Stderr.ToString()) is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;

    [GeneratedRegex(@"Run-time error '(\d+)'")]
    private static partial Regex RunTimeError();

    public JsonObject ToJson() => new()
    {
        ["stdout"] = Stdout.ToString(),
        ["stderr"] = Stderr.ToString(),
        ["errorNumber"] = ErrorNumber,
        ["stops"] = new JsonArray(Stops.Select(s => (JsonNode?)s).ToArray()),
        ["exitCode"] = ExitCode,
        ["processExitCode"] = ProcessExitCode,
        ["forced"] = Forced,
        ["timedOut"] = TimedOut,
        ["failure"] = Failure,
        ["stderrTail"] = StderrTail,
    };
}
