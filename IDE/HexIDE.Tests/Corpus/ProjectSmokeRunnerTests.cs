using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Simple;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform;
using Avalonia.Threading;
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
    // Timer armed; a Timer event finished.
    internal const string StartupFormMarker = ".form";
    internal const string ShownMarker = ".shown";
    internal const string ArmedMarker = ".armed";
    internal const string TickedMarker = ".ticked";

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
        int timerSeconds = TimerSeconds)
    {
        var result = project + ".smoke-result.json";
        var ready = result + ".ready";
        var started = result + ".started";
        var form = result + StartupFormMarker;
        var shown = result + ShownMarker;
        var armed = result + ArmedMarker;
        var ticked = result + TickedMarker;
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(project)!
        };
        start.ArgumentList.Add(typeof(ProjectSmokeRunner).Assembly.Location);
        start.ArgumentList.Add("-method");
        start.ArgumentList.Add("HexIDE.Tests.Corpus.ProjectSmokeRunnerTests.Startup_worker");
        start.Environment["HEXIDE_SMOKE_PROJECT"] = project;
        start.Environment["HEXIDE_SMOKE_RESULT"] = result;
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

            // What the waits saw, so a run that reached its limit says how far startup got. Null when there was nothing to
            // see, as for a Sub Main project.
            string? Watched()
            {
                var seen = new[]
                {
                    !File.Exists(form) ? null : File.Exists(shown) ? "the startup form showed"
                        : $"the startup form did not show within {showSeconds} seconds",
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
            foreach (var marker in new[] { result, ready, started, form, shown, armed, ticked })
                File.Delete(marker);
        }
    }
}

public class ProjectSmokeRunnerTests
{
    // A call to a procedure the program Declares from a DLL is HexIDE's refusal to call native code, not a crash; the
    // same error for a name nothing declares is still a crash (#4774). NeonAurora's Form_Load reaches winmm's midiOutOpen.
    [Theory]
    [InlineData("midiOutOpen", "refused")]
    [InlineData("MIDIOUTOPEN", "refused")]
    [InlineData("midiOutClose", "refused")]
    [InlineData("NotDeclared", "crashed")]
    public void A_call_to_a_Declared_procedure_is_a_refusal(string called, string expected)
    {
        var declared = SmokeStage.DeclaredProcedures([
            "Option Explicit\r\nPrivate Declare Function midiOutOpen Lib \"winmm.dll\" (lphMidiOut As Long) As Long\r\n",
            "Public Declare PtrSafe Sub midiOutClose Lib \"winmm.dll\" (ByVal h As LongPtr)\r\n' Declare Sub InAComment Lib \"x\" ()\r\n"
        ]);
        declared.Should().BeEquivalentTo(["midiOutOpen", "midiOutClose"]);
        var stage = SmokeStage.FromException(new VBSubOrFunctionNotDefinedException(called), declared);
        stage.Status.Should().Be(expected, stage.Detail);
        SmokeStage.FromException(new VBSubOrFunctionNotDefinedException(called)).Status.Should().Be("crashed",
            "with no declared names, as before");
    }

    // Off Windows the same call raises 53 naming its Lib string, as VBA on a Mac does (#4820), and NeonAurora's
    // Form_Load doesn't trap its gdi32 call. Only a library the project declares counts.
    [Theory]
    [InlineData(53, "File not found: winmm.dll", "refused")]
    [InlineData(53, "File not found: gdi32", "refused")]
    [InlineData(53, "File not found: other.dll", "crashed")]
    [InlineData(53, "File not found: kernel32", "crashed")]
    [InlineData(53, "File not found", "crashed")]
    [InlineData(5, "File not found: gdi32", "crashed")]
    public void A_missing_Declared_library_is_a_refusal(int number, string description, string expected)
    {
        var libraries = SmokeStage.DeclaredLibraries([
            "Private Declare Function midiOutOpen Lib \"winmm.dll\" (lphMidiOut As Long) As Long\r\n",
            "Public Declare Function CreateCompatibleDC LIB \"gdi32\" (ByVal hdc As Long) As Long\r\n"
            + "Private Declare Function Wrapped Lib _\r\n    \"kernel32\" () As Long\r\n' Declare Sub InAComment Lib \"x\" ()\r\n"
        ]);
        libraries.Should().BeEquivalentTo(["winmm.dll", "gdi32"], "a Lib on a continuation line is missed");
        var error = new VBRunTimeException(new VBStandardError(number, description));
        SmokeStage.FromException(error, libraries: libraries).Status.Should().Be(expected, description);
        SmokeStage.FromException(error).Status.Should().Be("crashed", "with no declared libraries");
    }

    [Fact]
    public void Slow_host_does_not_spend_the_workers_preparation_budget()
    {
        var budget = new ProjectSmokeRunner.PreparationBudget();
        budget.Timeout(false, TimeSpan.FromSeconds(90)).Should().BeNull();
        budget.Timeout(true, TimeSpan.FromSeconds(98)).Should().BeNull();
        budget.Timeout(true, TimeSpan.FromSeconds(157)).Should().BeNull();
        budget.Timeout(true, TimeSpan.FromSeconds(158)).Should()
            .Be("Startup worker did not become ready within 60 seconds of entering the worker.");
    }

    [Fact]
    public void Host_that_never_enters_the_worker_has_a_separate_bound()
    {
        var budget = new ProjectSmokeRunner.PreparationBudget();
        budget.Timeout(false, TimeSpan.FromSeconds(299)).Should().BeNull();
        budget.Timeout(false, TimeSpan.FromSeconds(300)).Should()
            .Be("Test host did not enter the startup worker within 300 seconds.");
    }

    [Fact]
    public void Preparation_hang_is_bounded_even_while_the_started_marker_stays_present()
    {
        var budget = new ProjectSmokeRunner.PreparationBudget();
        budget.Timeout(true, TimeSpan.FromSeconds(5)).Should().BeNull();
        budget.Timeout(true, TimeSpan.FromSeconds(64)).Should().BeNull();
        budget.Timeout(true, TimeSpan.FromSeconds(65)).Should()
            .Be("Startup worker did not become ready within 60 seconds of entering the worker.");
    }

    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) { }
    }

    // Err.Raise 5: docs/vb6-fidelity-oracle/when-err-is-cleared-and-what-a-running-handler-traps.md (#877).
    [Theory]
    [InlineData(null, "refused", false)]
    [InlineData("", "ran to completion", false)]
    [InlineData("Do\r\nLoop", "ran to the time limit", false)]
    [InlineData("AppActivate \"missing\"", "refused", false)]
    [InlineData("Err.Raise 5", "crashed", false)]
    [InlineData("MsgBox \"hello\"\r\nDim answer As String\r\nanswer = InputBox(\"prompt\", , \"scripted\")", "ran to completion", false)]
    [InlineData("", "ran to completion", true)]
    [InlineData("AppActivate \"missing\"", "refused", true)]
    [InlineData("Err.Raise 5", "crashed", true)]
    public async Task Isolated_startup_reports_completion_timeout_refusal_fault_and_answers_dialogs(string? code, string expected, bool form)
    {
        var directory = Directory.CreateTempSubdirectory("hexide-startup-test-").FullName;
        try
        {
            var project = Path.Join(directory, "Test.vbp");
            var marker = Path.Join(directory, "marker.txt");
            File.WriteAllText(project, "Type=Exe\r\nModule=Module1; Module1.bas\r\nStartup=\"Sub Main\"\r\nName=\"Test\"\r\n");
            File.WriteAllText(Path.Join(directory, "Module1.bas"),
                "Attribute VB_Name = \"Module1\"\r\nSub Main()\r\n" + code
                + $"\r\nOpen \"{marker}\" For Output As #1\r\nPrint #1, \"executed\"\r\nClose #1\r\nEnd Sub\r\n");
            if (form)
            {
                File.WriteAllText(project, "Type=Exe\r\nForm=Form1.frm\r\nStartup=\"Form1\"\r\nName=\"Test\"\r\n");
                var body = File.ReadAllText(Path.Join(directory, "Module1.bas"))
                    .Replace("Sub Main()", "Private Sub Form_Load()")
                    .Replace("End Sub", "Unload Me\r\nEnd Sub")
                    .Replace("Attribute VB_Name = \"Module1\"", "Attribute VB_Name = \"Form1\"");
                File.WriteAllText(Path.Join(directory, "Form1.frm"),
                    "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption = \"Test\"\r\nEnd\r\n" + body);
            }
            if (code is null)
                File.WriteAllText(project, "Type=Exe\r\nStartup=\"(None)\"\r\nName=\"Test\"\r\n");
            var result = await ProjectSmokeRunner.Run(project, expected == "ran to the time limit" ? ProjectSmokeRunner.StartupSeconds : 30);
            result.Status.Should().Be(expected, result.Detail);
            if (expected == "ran to completion") File.ReadAllText(marker).Should().Contain("executed");
            if (expected == "refused") result.Detail.Should().Contain(code is null ? "no startup path" : "AppActivate");
            if (expected == "crashed") result.Detail.Should().Contain("Run-time error '5'");
        }
        finally { Directory.Delete(directory, true); }
    }

    // A Timer on the startup form, enabled in Form_Load as the corpus demos enable theirs: the Timer is saved with no
    // Interval, or disabled (#4774). The startup window opens when the form shows, and an armed Timer is watched until one
    // of its events has finished, so an error in its handler fails the run whenever its first tick comes.
    // A run that reaches a limit names what it saw, which each such row checks. Without the form's window marker the
    // clean tick would still pass, 30 seconds slower, so its detail is what keeps the marker honest.
    [Theory]
    [InlineData(true, "Timer1.Interval = 5000", "Err.Raise 5", "crashed", null)]
    [InlineData(false, "Timer1.Interval = 5000\r\nTimer1.Enabled = True", "Err.Raise 5", "crashed", null)]
    [InlineData(true, "Timer1.Interval = 5000", "Caption = \"ticked\"", "ran to the time limit",
        "the startup form showed; a Timer armed; a Timer event finished.")]
    [InlineData(true, "Dim t As Single\r\nt = Timer\r\nDo While Timer < t + 4\r\nLoop\r\nTimer1.Interval = 100", "Err.Raise 5",
        "crashed", null)]
    [InlineData(true, "Timer1.Interval = 60000", "Err.Raise 5", "timer did not fire",
        "the startup form showed; a Timer armed; none of its events finished within 5 seconds of the form showing.")]
    [InlineData(true, "Do\r\nLoop", "Err.Raise 5", "ran to the time limit", "the startup form did not show within 5 seconds.")]
    [InlineData(true, "Timer1.Interval = 100", null, "ran to the time limit", "the startup form showed.")]
    public async Task A_startup_Timer_is_watched_until_one_of_its_events_finishes(bool savedEnabled, string load, string? tick,
        string expected, string? watched)
    {
        var directory = Directory.CreateTempSubdirectory("hexide-startup-timer-").FullName;
        try
        {
            var project = Path.Join(directory, "Test.vbp");
            File.WriteAllText(project, "Type=Exe\r\nForm=Form1.frm\r\nStartup=\"Form1\"\r\nName=\"Test\"\r\n");
            File.WriteAllText(Path.Join(directory, "Form1.frm"),
                "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption = \"Test\"\r\n   Begin VB.Timer Timer1 \r\n"
                + (savedEnabled ? "" : "      Enabled = 0   'False\r\n")
                + "      Left = 0\r\n      Top = 0\r\n   End\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n"
                + "Private Sub Form_Load()\r\n" + load + "\r\nEnd Sub\r\n"
                + (tick is null ? "" : "Private Sub Timer1_Timer()\r\n" + tick + "\r\nEnd Sub\r\n"));
            // The two bounds are shortened only where a case runs to them, to keep it quick.
            var result = await ProjectSmokeRunner.Run(project,
                showSeconds: load == "Do\r\nLoop" ? 5 : ProjectSmokeRunner.ShowSeconds,
                timerSeconds: expected == "timer did not fire" ? 5 : ProjectSmokeRunner.TimerSeconds);
            result.Status.Should().Be(expected, result.Detail);
            if (expected == "crashed") result.Detail.Should().Contain("Run-time error '5'");
            else result.Detail.Should().Be(watched);
        }
        finally { Directory.Delete(directory, true); }
    }

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
            VBTimer.Armed -= Armed;
            VBWindowContext.TimerEventFinished -= Ticked;
        }
    }
}
