using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Conversations;
using HexIDE.Dap;
using HexIDE.Debugging;
using HexIDE.IDE;
using HexIDE.VisualDesigner;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using HexIDE.Tools;

namespace HexIDE.Tests.DebugAdapter;

public class ExternalDebugServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static async Task<string> Configuration(string directory, string mode)
    {
        var executable = Path.GetFullPath("../../../dotnet" + (OperatingSystem.IsWindows() ? ".exe" : ""), RuntimeEnvironment.GetRuntimeDirectory());
        var file = Path.Combine(directory, "launch.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new
        {
            executable, arguments = new[] { typeof(Dap.TestAdapter.Program).Assembly.Location, mode },
            adapterId = "fixture", launch = new { },
        }), Token);
        return file;
    }

    private static Task WaitFor(ExternalDebugService service, Func<bool> predicate)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed() { if (predicate()) ready.TrySetResult(); }
        service.Changed += Changed;
        Changed();
        return Wait();
        async Task Wait()
        {
            try { await ready.Task.WaitAsync(Patience, Token); }
            finally { service.Changed -= Changed; }
        }
    }

    [AvaloniaFact]
    public async Task Real_process_populates_existing_windows_on_UI_thread_then_clears_after_continue()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-ui-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log);
        var localization = Substitute.For<ILocalizationService>();
        var interpreter = Substitute.For<IDebugController>();
        var runner = new ProjectRunnerService(Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(),
            Substitute.For<IProjectManager>(), localization, interpreter, Substitute.For<IBreakpointService>(),
            new WatchService(), new RunScope(), service);
        var locals = new LocalsToolViewModel(localization, interpreter, service);
        var stack = new CallStackToolViewModel(localization, interpreter, service);
        var failures = new List<string>();
        var output = new List<string>();
        service.Changed += () => Dispatcher.UIThread.CheckAccess().Should().BeTrue();
        service.Failed += failures.Add;
        service.Output += output.Add;
        Task? run = null;
        try
        {
            run = service.StartAsync(await Configuration(directory, "session"));
            service.IsActive.Should().BeTrue();
            await WaitFor(service, () => service.Variables.Count == 1 && service.ThreadId == 1);
            locals.Roots.Single().Value.Should().Be("41");
            stack.Frames.Single().Line.Should().Be(6);
            service.Connection!.Kind.Should().Be(Lsp.LanguageConnectionKind.DebugAdapter);
            runner.IsRunning.Should().BeTrue();
            runner.CanContinueProject.Should().BeTrue();
            runner.CanBreakProject.Should().BeFalse();
            runner.CanRestartProject.Should().BeFalse();
            runner.CanStartDefaultProject.Should().BeFalse();
            var firstStop = service.Stop;
            runner.StepOverProject();
            await WaitFor(service, () => service.Stop is not null && service.Stop != firstStop && service.Variables.Count == 1 && !service.IsBusy);
            interpreter.DidNotReceive().StepOver();
            runner.ContinueProject();
            await run.WaitAsync(Patience, Token);
            service.IsActive.Should().BeFalse();
            runner.IsRunning.Should().BeFalse();
            locals.Roots.Should().BeEmpty();
            stack.Frames.Should().BeEmpty();
            output.Should().Contain("42\n");
            failures.Should().BeEmpty();
            interpreter.DidNotReceive().Continue();
        }
        finally
        {
            await service.StopAsync();
            if (run is not null) await run.WaitAsync(Patience, Token);
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task Selecting_a_caller_updates_locals_and_source_but_preserves_execution_location()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-frames-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log);
        var localization = Substitute.For<ILocalizationService>();
        var interpreter = Substitute.For<IDebugController>();
        var locals = new LocalsToolViewModel(localization, interpreter, service);
        var stack = new CallStackToolViewModel(localization, interpreter, service);
        var failures = new List<string>();
        service.Failed += failures.Add;
        var run = service.StartAsync(await Configuration(directory, "frames"));
        try
        {
            await WaitFor(service, () => service.Variables.Count == 1);
            var stop = service.Stop!;
            var rows = stack.Frames;
            stack.SelectedFrame.Should().BeSameAs(rows[0]);
            rows[0].FrameId.Should().Be(0);
            var execution = service.CurrentLocation;
            stack.SelectedFrame = rows[1];
            locals.Roots.Should().BeEmpty();
            locals.ContextLabel.Should().Be("Caller");
            await WaitFor(service, () => service.Variables.Count == 1);
            locals.Roots.Single().Expression.Should().Be("callerValue");
            locals.Roots.Single().Value.Should().Be("42");
            service.CurrentLocation.Should().Be(execution);
            service.SelectedLocation!.Value.Line.Should().Be(12);
            stack.Frames.Should().BeSameAs(rows);
            stack.SelectedFrame.Should().BeSameAs(rows[1]);
            await service.SelectFrameAsync(stop, 999); // Not a frame in this stack.
            stack.SelectedFrame.Should().BeSameAs(rows[1]);
            await service.CommandAsync("next");
            await WaitFor(service, () => service.Stop != stop && service.Variables.Count == 1);
            stack.SelectedFrame.Should().BeSameAs(stack.Frames[0]);
            locals.ContextLabel.Should().Be("Callee");
            await service.SelectFrameAsync(stop, 7); // IDs from a previous stop are not selectable.
            stack.SelectedFrame!.FrameId.Should().Be(0);
            failures.Should().BeEmpty();
        }
        finally
        {
            await service.StopAsync();
            await run.WaitAsync(Patience, Token);
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_caller_variables_cannot_repaint_after_reselection_or_resume(bool resume)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-frames-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log);
        var locals = new LocalsToolViewModel(Substitute.For<ILocalizationService>(), Substitute.For<IDebugController>(), service);
        var pending = false;
        var failures = new List<string>();
        service.Failed += failures.Add;
        service.Output += output => { if (output == "caller-pending\n") pending = true; };
        var run = service.StartAsync(await Configuration(directory, "frames-delayed"));
        try
        {
            await WaitFor(service, () => service.Variables.Count == 1);
            var caller = service.SelectFrameAsync(service.Stop!, 7);
            locals.Roots.Should().BeEmpty();
            await WaitFor(service, () => pending); // Peer has retained the caller request, no timing guesses.
            if (resume)
            {
                await service.CommandAsync("continue");
                await run.WaitAsync(Patience, Token);
                await caller.WaitAsync(Patience, Token);
                locals.Roots.Should().BeEmpty();
                service.SelectedFrame.Should().BeNull();
            }
            else
            {
                await service.SelectFrameAsync(service.Stop!, 0).WaitAsync(Patience, Token);
                await caller.WaitAsync(Patience, Token);
                locals.Roots.Single().Expression.Should().Be("calleeValue");
                service.SelectedFrame!.Value.GetProperty("id").GetInt32().Should().Be(0);
            }
            failures.Should().BeEmpty();
        }
        finally
        {
            await service.StopAsync();
            await run.WaitAsync(Patience, Token);
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task Ending_during_startup_and_startup_failure_both_release_active_state()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-ui-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log);
        try
        {
            var run = service.StartAsync(await Configuration(directory, "silent"));
            await service.StopAsync();
            await run.WaitAsync(Patience, Token);
            service.IsActive.Should().BeFalse();
            var failures = new List<string>();
            service.Failed += failures.Add;
            await service.StartAsync(await Configuration(directory, "exit")).WaitAsync(Patience, Token);
            service.IsActive.Should().BeFalse();
            failures.Should().NotBeEmpty();
        }
        finally { await service.StopAsync(); Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Configuration_resolves_paths_from_its_file_and_preserves_argument_boundaries()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-config-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "launch.json");
            await File.WriteAllTextAsync(path, """{"executable":"adapter","adapterId":"fixture","arguments":["two words","$literal"],"launch":{"module":"Main"},"breakpoints":[{"path":"a.bas","lines":[6]}]}""", Token);
            var configuration = await DapLaunchConfiguration.ReadAsync(path, Token);
            configuration.Executable.Should().Be(Path.Combine(directory, "adapter"));
            configuration.Breakpoints[0].Path.Should().Be(Path.Combine(directory, "a.bas"));
            configuration.Arguments.Should().Equal("two words", "$literal");
            await File.WriteAllTextAsync(path, """{"executable":"adapter","adapterId":"fixture","launch":{},"argumentz":[]}""", Token);
            await ((Func<Task>)(() => DapLaunchConfiguration.ReadAsync(path, Token))).Should().ThrowAsync<JsonException>();
        }
        finally { Directory.Delete(directory, true); }
    }
}
