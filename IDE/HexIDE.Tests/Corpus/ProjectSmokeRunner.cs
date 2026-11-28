using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Simple;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform;
using Avalonia.Threading;
using HexIDE.IDE;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Dialogs;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Corpus;

internal sealed partial record SmokeStage(string Status, string? Detail = null)
{
    public bool Failed => Status == "crashed";
    public static SmokeStage FromException(Exception exception, IReadOnlySet<string>? declared = null,
        IReadOnlySet<string>? libraries = null) =>
        exception is NotImplementedException
        // Two evidence projects under corpus/member-attributes keep a Public Const in a class, which vb6.exe's
        // IDE saves and its compiler refuses (#3335, #3350). Refusing them is the right answer, not a crash.
        || exception is VBCompileErrorException && exception.Message.Contains(PrePass.PublicMemberOfObjectModule)
        // HexIDE calls no native code. On Windows a call to a Declare'd procedure is "Sub or Function not defined" by
        // design, a refusal and not a crash. NeonAurora's Form_Load makes one, which the longer startup watch of
        // #4774 reaches.
        || exception is VBSubOrFunctionNotDefinedException && declared is not null
            && NotDefinedName().Match(exception.Message) is { Success: true } name && declared.Contains(name.Groups[1].Value)
        // Off Windows the same call raises 53, "File not found: " plus its Lib string, as VBA on a Mac does (#4820).
        // Untrapped, it ends the program there too. Only a library the project itself declares counts: a 53 from
        // Open, or one naming any other file, is still a crash.
        || exception is VBRunTimeException { Error.ErrNo: 53 } missing && libraries is not null
            && missing.Error.Description.StartsWith(MissingLibrary, StringComparison.Ordinal)
            && libraries.Contains(missing.Error.Description[MissingLibrary.Length..])
        ? new("refused", exception.Message) : new("crashed", exception.ToString());

    private const string MissingLibrary = "File not found: ";

    /// <summary>The procedures a project's code Declares from a DLL, by name, whatever their case (#4774).</summary>
    public static IReadOnlySet<string> DeclaredProcedures(IEnumerable<string> code) => code
        .SelectMany(text => DeclareStatement().Matches(text).Select(match => match.Groups[1].Value))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The Lib strings a project's code Declares, as written (#4820). One on a continuation line below its
    /// Declare is missed, so its 53 reads as a crash rather than passing unnoticed.</summary>
    public static IReadOnlySet<string> DeclaredLibraries(IEnumerable<string> code) => code
        .SelectMany(text => DeclareStatement().Matches(text).Where(match => match.Groups[2].Success)
            .Select(match => match.Groups[2].Value.Replace("\"\"", "\"")))
        .ToHashSet(StringComparer.Ordinal);

    [System.Text.RegularExpressions.GeneratedRegex(@"Sub or Function not defined \(([^()]+)\)")]
    private static partial System.Text.RegularExpressions.Regex NotDefinedName();

    [System.Text.RegularExpressions.GeneratedRegex(
        @"^[ \t]*(?:(?:Public|Private)[ \t]+)?Declare[ \t]+(?:PtrSafe[ \t]+)?(?:Sub|Function)[ \t]+([A-Za-z][A-Za-z0-9_]*)"
        + @"(?:[$%&!#@^]?[ \t]+Lib[ \t]+""((?:[^""]|"""")*)"")?",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline)]
    private static partial System.Text.RegularExpressions.Regex DeclareStatement();
}

internal sealed class SmokeStageRecorder(string output)
{
    private readonly object gate = new();
    private bool recorded;

    public void Record(SmokeStage stage)
    {
        lock (gate)
        {
            if (recorded) return; // The first fault wins; later reports describe its aftermath (#3019).
            AtomicFileWriter.WriteAllText(output, JsonSerializer.Serialize(stage));
            recorded = true;
        }
    }
}

internal static class ProjectSmokeRunner
{
    internal const int StartupSeconds = 3;
    internal const int ShowSeconds = 30;
    internal const int TimerSeconds = 30;

    // The worker's marker files beside the result, each written once (#4774): the startup is a form; its window opened; a
    // Timer armed; a Timer event finished; Sub Main entered (#4863).
    internal const string StartupFormMarker = ".form";
    internal const string ShownMarker = ".shown";
    internal const string ArmedMarker = ".armed";
    internal const string TickedMarker = ".ticked";
    internal const string MainMarker = ".main";

    internal sealed class PreparationBudget
    {
        private TimeSpan? entered;

        public string? Timeout(bool started, TimeSpan elapsed)
        {
            if (started) entered ??= elapsed;
            if (entered is { } began)
                return elapsed - began >= TimeSpan.FromSeconds(60)
                    ? "Startup worker did not become ready within 60 seconds of entering the worker." : null;
            // Host startup is separate from project preparation, but must still be bounded (#2934).
            return elapsed >= TimeSpan.FromSeconds(300)
                ? "Test host did not enter the startup worker within 300 seconds." : null;
        }
    }

    internal static async Task<SmokeStage> ReadResult(string result, Func<string, string>? read = null)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            try { return JsonSerializer.Deserialize<SmokeStage>((read ?? File.ReadAllText)(result))!; }
            catch (IOException) when (clock.Elapsed < TimeSpan.FromSeconds(2))
            {
                await Task.Delay(20, CancellationToken.None);
            }
        }
    }

    // A separate process bounds even a synchronous infinite loop in a startup event.
    public static async Task<SmokeStage> Run(string project, int startupSeconds = StartupSeconds, int showSeconds = ShowSeconds,
        int timerSeconds = TimerSeconds, int preparationDelayMilliseconds = 0)
    {
        var result = project + ".smoke-result.json";
        var ready = result + ".ready";
        var started = result + ".started";
        var form = result + StartupFormMarker;
        var shown = result + ShownMarker;
        var armed = result + ArmedMarker;
        var ticked = result + TickedMarker;
        var main = result + MainMarker;
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(project)!
        };
        start.ArgumentList.Add(typeof(ProjectSmokeWorker).Assembly.Location);
        start.ArgumentList.Add("-method");
        start.ArgumentList.Add($"{typeof(ProjectSmokeWorker).FullName}.{nameof(ProjectSmokeWorker.Startup_worker)}");
        start.Environment["HEXIDE_SMOKE_PROJECT"] = project;
        start.Environment["HEXIDE_SMOKE_RESULT"] = result;
        start.Environment["HEXIDE_SMOKE_PREPARATION_DELAY"] = preparationDelayMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            var clock = Stopwatch.StartNew();
            var budget = new PreparationBudget();
            while (!process.HasExited && !File.Exists(ready) && !File.Exists(result))
            {
                if (budget.Timeout(File.Exists(started), clock.Elapsed) is { } timeout)
                    return new("crashed", timeout);
                await Task.Delay(20, CancellationToken.None);
            }
            clock.Restart();
            bool Ended() => process.HasExited || File.Exists(result);
            // A startup form is watched from when its window opens. Building it and running its Load can take longer
            // than the window on a cold runner, and a Timer that Load sets going arms only then (#4774). A Load that
            // never ends is given up on at showSeconds, and reported as running to the time limit, as it was.
            if (File.Exists(form))
            {
                while (!Ended() && !File.Exists(shown) && clock.Elapsed < TimeSpan.FromSeconds(showSeconds))
                    await Task.Delay(20, CancellationToken.None);
                if (File.Exists(shown))
                    clock.Restart();
            }
            else
            {
                // Parsing, the pre-pass and module declarations do not spend Sub Main's window (#4863).
                while (!Ended() && !File.Exists(main) && clock.Elapsed < TimeSpan.FromSeconds(showSeconds))
                    await Task.Delay(20, CancellationToken.None);
                if (File.Exists(main))
                    clock.Restart();
            }
            while (!Ended() && clock.Elapsed < TimeSpan.FromSeconds(startupSeconds))
                await Task.Delay(20, CancellationToken.None);
            // An armed Timer is waited for until one of its events has finished, so an error in its handler is seen
            // whenever its first tick comes, not only when it comes inside the window (#4774).
            if (!Ended() && File.Exists(armed))
            {
                while (!Ended() && !File.Exists(ticked) && clock.Elapsed < TimeSpan.FromSeconds(timerSeconds))
                    await Task.Delay(20, CancellationToken.None);
                if (!Ended() && !File.Exists(ticked))
                    return new("timer did not fire", Watched());
            }
            if (File.Exists(result))
                return await ReadResult(result);
            return process.HasExited
                ? new("crashed", $"Startup worker exited {process.ExitCode}: {await stdout}\n{await stderr}")
                : new("ran to the time limit", Watched());

            // What the waits saw, so a run that reached its limit says how far startup got.
            string? Watched()
            {
                var seen = new[]
                {
                    !File.Exists(form) ? null : File.Exists(shown) ? "the startup form showed"
                        : $"the startup form did not show within {showSeconds} seconds",
                    File.Exists(form) ? null : File.Exists(main) ? "Sub Main started"
                        : $"Sub Main did not start within {showSeconds} seconds",
                    File.Exists(armed) ? "a Timer armed" : null,
                    File.Exists(armed) && !File.Exists(ticked)
                        ? $"none of its events finished within {timerSeconds} seconds of the form showing" : null,
                    File.Exists(ticked) ? "a Timer event finished" : null,
                }.OfType<string>().ToArray();
                return seen.Length == 0 ? null : string.Join("; ", seen) + ".";
            }
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            foreach (var marker in new[] { result, ready, started, form, shown, armed, ticked, main })
                File.Delete(marker);
        }
    }
}

/// <summary>
/// The runner's worker runs a corpus startup only in a process the runner started with
/// <c>HEXIDE_SMOKE_PROJECT</c> set, returning at once and passing everywhere else.
/// A project that compiles this file discovers its own copy, and that project's runner starts that copy.
/// </summary>
public class ProjectSmokeWorker
{
    [AvaloniaFact]
    public async Task Startup_worker()
    {
        var path = Environment.GetEnvironmentVariable("HEXIDE_SMOKE_PROJECT");
        if (path is null) return; // Only the isolated invocation runs a corpus startup.
        var output = Environment.GetEnvironmentVariable("HEXIDE_SMOKE_RESULT")!;
        File.WriteAllText(output + ".started", "started");
        // Match the Standalone host's theme and runtime control resources.
        Application.Current!.Styles.Add(new SimpleTheme());
        VBLoader.DefaultFormIcon = new WindowIcon(AssetLoader.Open(new Uri("avares://HexIDE.Runtime/form.ico")));
        foreach (var dictionary in new[] { "SystemColors.axaml", "VBLabel.axaml", "Resources.axaml" })
            Application.Current.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://HexIDE.Standalone/"))
            {
                Source = new Uri("avares://HexIDE.Runtime/BuiltinControls/" + dictionary)
            });
        var recorder = new SmokeStageRecorder(output);
        void Record(SmokeStage stage) => recorder.Record(stage);
        IReadOnlySet<string> declared = new HashSet<string>();
        IReadOnlySet<string> libraries = new HashSet<string>();
        void Fault(VBWindowContext _, Exception exception) => Record(SmokeStage.FromException(exception, declared, libraries));
        void RuntimeFault(VBWindowContext context, VBRunTimeException exception) => Fault(context, exception);
        void CompileFault(VBWindowContext context, VBCompileErrorException exception) => Fault(context, exception);
        VBWindowContext.Faulted += Fault;
        VBWindowContext.RunTimeError += RuntimeFault;
        VBWindowContext.CompileError += CompileFault;
        // Each marker once, for the runner's waits (#4774).
        var marked = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();
        void Mark(string marker)
        {
            if (marked.TryAdd(marker, true))
                File.WriteAllText(output + marker, marker);
        }
        // A Timer with no handler for its event raises nothing that could finish, so it isn't waited for.
        void Armed(VBTimer timer)
        {
            if (ControlEvents.Handlers(timer, timer.FindLogicalAncestorOfType<IModuleExecutionRoot>(),
                    TimerComponentClass.TimerEvent.Name).Any(handler => handler.Root.Handles(handler.Name)))
                Mark(ProjectSmokeRunner.ArmedMarker);
        }
        void Ticked(VBWindowContext _) => Mark(ProjectSmokeRunner.TickedMarker);
        void MainStarted() => Mark(ProjectSmokeRunner.MainMarker);
        BasicInterpreter.StartupSubMainStarted += MainStarted;
        VBTimer.Armed += Armed;
        VBWindowContext.TimerEventFinished += Ticked;
        using var dialogs = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            if (window is VBFormRuntime)
                Mark(ProjectSmokeRunner.ShownMarker);
            Dispatcher.UIThread.Post(() =>
            {
                var button = window.Content switch
                {
                    MessageBox box => box.GetLogicalDescendants().OfType<Button>().FirstOrDefault(),
                    InputBox box => box.FindControl<Button>("PART_Ok"),
                    _ => null
                };
                button?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }, DispatcherPriority.Background);
        });
        try
        {
            var project = (await new ProjectLoader().LoadAsync(path, new Sink())).Project;
            var code = project.Modules.Select(module => module.Code).Concat(project.Forms.Select(form => form.Code)).ToList();
            declared = SmokeStage.DeclaredProcedures(code);
            libraries = SmokeStage.DeclaredLibraries(code);
            if (!project.StartsAtSubMain && project.StartupForm is null
                && project.StartupFormName is null or "(None)")
                throw new NotImplementedException("Project has no startup path to run.");
            if (!project.StartsAtSubMain)
                Mark(ProjectSmokeRunner.StartupFormMarker);
            File.WriteAllText(output + ".ready", "ready");
            if (int.TryParse(Environment.GetEnvironmentVariable("HEXIDE_SMOKE_PREPARATION_DELAY"), out var delay))
                Thread.Sleep(delay); // Stand in for a cold parse, after ready (#4863); test worker only.
            // This is the entry point used by HexIDE.Standalone.App.
            await VBLoader.RunMadeProgram(project, TestContext.Current.CancellationToken, out _);
            Record(new("ran to completion"));
        }
        catch (Exception exception)
        {
            Record(SmokeStage.FromException(exception, declared, libraries));
        }
        finally
        {
            VBWindowContext.Faulted -= Fault;
            VBWindowContext.RunTimeError -= RuntimeFault;
            VBWindowContext.CompileError -= CompileFault;
            BasicInterpreter.StartupSubMainStarted -= MainStarted;
            VBTimer.Armed -= Armed;
            VBWindowContext.TimerEventFinished -= Ticked;
        }
    }

    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) { }
    }
}
