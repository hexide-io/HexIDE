using System;
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
using HexIDE.Runtime.Dialogs;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Corpus;

internal sealed record SmokeStage(string Status, string? Detail = null)
{
    public bool Failed => Status == "crashed";
    public static SmokeStage FromException(Exception exception) => exception is NotImplementedException
        // Two evidence projects under corpus/member-attributes keep a Public Const in a class, which vb6.exe's
        // IDE saves and its compiler refuses (#3335, #3350). Refusing them is the right answer, not a crash.
        || exception is VBCompileErrorException && exception.Message.Contains(PrePass.PublicMemberOfObjectModule)
        ? new("refused", exception.Message) : new("crashed", exception.ToString());
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
    public static async Task<SmokeStage> Run(string project, int startupSeconds = StartupSeconds)
    {
        var result = project + ".smoke-result.json";
        var ready = result + ".ready";
        var started = result + ".started";
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
            while (!process.HasExited && !File.Exists(result) && clock.Elapsed < TimeSpan.FromSeconds(startupSeconds))
                await Task.Delay(20, CancellationToken.None);
            if (File.Exists(result))
                return await ReadResult(result);
            return process.HasExited
                ? new("crashed", $"Startup worker exited {process.ExitCode}: {await stdout}\n{await stderr}")
                : new("ran to the time limit");
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            File.Delete(result);
            File.Delete(ready);
            File.Delete(started);
        }
    }
}

public class ProjectSmokeRunnerTests
{
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

    [AvaloniaFact]
    public async Task Startup_worker()
    {
        var path = Environment.GetEnvironmentVariable("HEXIDE_SMOKE_PROJECT");
        if (path is null) return; // Only the isolated invocation runs a corpus startup.
        var output = Environment.GetEnvironmentVariable("HEXIDE_SMOKE_RESULT")!;
        File.WriteAllText(output + ".started", "started");
        // Match the Standalone host's theme and runtime control resources.
        Application.Current!.Styles.Add(new SimpleTheme());
        VBLoader.DefaultFormIcon = new WindowIcon(AssetLoader.Open(new Uri("avares://HexIDE.Standalone/form.ico")));
        foreach (var dictionary in new[] { "SystemColors.axaml", "VBLabel.axaml", "Resources.axaml" })
            Application.Current.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://HexIDE.Standalone/"))
            {
                Source = new Uri("avares://HexIDE.Runtime/BuiltinControls/" + dictionary)
            });
        var recorder = new SmokeStageRecorder(output);
        void Record(SmokeStage stage) => recorder.Record(stage);
        void Fault(VBWindowContext _, Exception exception) => Record(SmokeStage.FromException(exception));
        void RuntimeFault(VBWindowContext context, VBRunTimeException exception) => Fault(context, exception);
        void CompileFault(VBWindowContext context, VBCompileErrorException exception) => Fault(context, exception);
        VBWindowContext.Faulted += Fault;
        VBWindowContext.RunTimeError += RuntimeFault;
        VBWindowContext.CompileError += CompileFault;
        using var dialogs = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            Dispatcher.UIThread.Post(() =>
            {
                var button = window.Content switch
                {
                    MessageBox box => box.GetLogicalDescendants().OfType<Button>().FirstOrDefault(),
                    InputBox box => box.FindControl<Button>("PART_Ok"),
                    _ => null
                };
                button?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }, DispatcherPriority.Background));
        try
        {
            var project = (await new ProjectLoader().LoadAsync(path, new Sink())).Project;
            if (!project.StartsAtSubMain && project.StartupForm is null
                && project.StartupFormName is null or "(None)")
                throw new NotImplementedException("Project has no startup path to run.");
            File.WriteAllText(output + ".ready", "ready");
            // This is the entry point used by HexIDE.Standalone.App.
            await VBLoader.RunMadeProgram(project, TestContext.Current.CancellationToken, out _);
            Record(new("ran to completion"));
        }
        catch (Exception exception)
        {
            Record(SmokeStage.FromException(exception));
        }
        finally
        {
            VBWindowContext.Faulted -= Fault;
            VBWindowContext.RunTimeError -= RuntimeFault;
            VBWindowContext.CompileError -= CompileFault;
        }
    }
}
