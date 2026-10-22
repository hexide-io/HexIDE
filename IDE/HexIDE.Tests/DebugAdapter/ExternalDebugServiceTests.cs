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
    [AvaloniaTheory]
    [InlineData("evaluate")]
    [InlineData("frames")]
    public async Task Hover_uses_selected_frame_refuses_errors_and_gates_capability(string mode)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-hover-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log);
        var run = service.StartAsync(await Configuration(directory, mode));
        try
        {
            await WaitFor(service, () => service.CanEvaluate);
            service.CanEvaluateHover.Should().Be(mode == "evaluate");
            (await service.EvaluateHoverAsync("answer", Token)).Should().Be(mode == "evaluate" ? "frame:0:answer" : null);
            await service.SelectFrameAsync(service.Stop!, 7);
            (await service.EvaluateHoverAsync("answer", Token)).Should().Be(mode == "evaluate" ? "frame:7:answer" : null);
            (await service.EvaluateHoverAsync("reject", Token)).Should().BeNull();
            (await service.EvaluateHoverAsync("answer", Token)).Should().Be(mode == "evaluate" ? "frame:7:answer" : null);
            await service.StopAsync();
            (await service.EvaluateHoverAsync("answer", Token)).Should().BeNull();
        }
        finally { await service.StopAsync(); await run.WaitAsync(Patience, Token); Directory.Delete(directory, true); }
    }

    [AvaloniaTheory]
    [InlineData("frame")]
    [InlineData("next")]
    [InlineData("cancel")]
    [InlineData("immediate")]
    public async Task Hover_discards_delayed_reply_after_inspection_changes(string change)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-hover-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log);
        var pending = false;
        service.Output += output => { if (output == "evaluation-pending\n") pending = true; };
        var run = service.StartAsync(await Configuration(directory, "evaluate"));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        try
        {
            await WaitFor(service, () => service.CanEvaluateHover);
            var hover = service.EvaluateHoverAsync("wait", cancellation.Token);
            await WaitFor(service, () => pending);
            if (change == "cancel")
            {
                await cancellation.CancelAsync();
                await ((Func<Task>)(() => hover)).Should().ThrowAsync<OperationCanceledException>();
            }
            else
            {
                if (change == "frame") await service.SelectFrameAsync(service.Stop!, 7);
                else if (change == "next") await service.CommandAsync("next");
                else await service.EvaluateAsync("answer"); // refreshed scopes release the old hover
                (await hover.WaitAsync(Patience, Token)).Should().BeNull();
            }
        }
        finally { await service.StopAsync(); await run.WaitAsync(Patience, Token); Directory.Delete(directory, true); }
    }

    private static async Task WatchUpdated(Func<bool> predicate)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(Patience);
        while (!predicate()) await Task.Delay(10, deadline.Token);
    }

    [AvaloniaFact]
    public async Task External_watches_follow_frames_expand_recover_and_keep_interpreter_definitions_separate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-watches-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log);
        var interpreter = Substitute.For<IDebugController>();
        var saved = new WatchService();
        saved.Add(new("interpreterOnly", WatchType.BreakWhenTrue, "Main"));
        var watches = new WatchesToolViewModel(Substitute.For<ILocalizationService>(), saved, interpreter,
            Substitute.For<IWindowManager>(), service);
        watches.ExternalService.Add(new("aggregate", WatchType.Expression, "Callee"));
        watches.ExternalService.Add(new("reject", WatchType.Expression, "Callee"));
        watches.ExternalService.Add(new("answer", WatchType.Expression, "Callee"));
        var config = await Configuration(directory, "evaluate");
        var run = service.StartAsync(config);
        try
        {
            await WatchUpdated(() => watches.Rows.Count == 3 && watches.Rows[2].Value == "frame:0:answer");
            watches.Rows[0].TypeName.Should().Be("Long");
            watches.Rows[1].Value.Should().Contain("fixture refusal");
            watches.Rows[0].IsExpanded = true;
            await WatchUpdated(() => watches.Rows[0].Children.Any(child => child.Expression == "callerValue"));
            await service.SelectFrameAsync(service.Stop!, 7);
            await WatchUpdated(() => watches.Rows[2].Value == "frame:7:answer");
            await service.EvaluateAsync("callerValue = 99");
            await WatchUpdated(() => watches.Rows[0].Children.Any(child => child.Value == "99"));
            watches.SelectedRow = watches.Rows[1];
            watches.DeleteSelected();
            watches.ExternalService.Watches.Count.Should().Be(2);
            await WatchUpdated(() => watches.Rows.Count == 2 && watches.Rows[1].Value == "frame:7:answer");
            await service.StopAsync();
            await run.WaitAsync(Patience, Token);
            watches.Rows.Single().Expression.Should().Be("interpreterOnly");
            saved.Watches.Single().Type.Should().Be(WatchType.BreakWhenTrue);
            await interpreter.DidNotReceive().EvaluateWatchAsync(Arg.Any<string>());
            run = service.StartAsync(config);
            await WatchUpdated(() => watches.Rows.Count == 2 && watches.Rows[1].Value == "frame:0:answer");
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
    public async Task Delayed_watch_cannot_replace_edited_expression_or_leak_after_end(bool end)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-watches-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log);
        var watches = new WatchesToolViewModel(Substitute.For<ILocalizationService>(), new WatchService(),
            Substitute.For<IDebugController>(), Substitute.For<IWindowManager>(), service);
        var pending = false;
        service.Output += output => { if (output == "evaluation-pending\n") pending = true; };
        var run = service.StartAsync(await Configuration(directory, "evaluate"));
        try
        {
            await WaitFor(service, () => service.Variables.Count == 1);
            watches.ExternalService.Add(new("wait", WatchType.Expression, "Callee"));
            await WaitFor(service, () => pending);
            if (end)
            {
                await service.StopAsync();
                await run.WaitAsync(Patience, Token);
                watches.Rows.Should().BeEmpty();
            }
            else
            {
                watches.ExternalService.Watches[0] = new("newExpression", WatchType.Expression, "Caller");
                await service.SelectFrameAsync(service.Stop!, 7); // scopes releases the old evaluation
                await WatchUpdated(() => watches.Rows[0].Value == "frame:7:newExpression");
                watches.Rows[0].Value.Should().NotContain("obsolete");
                await service.CommandAsync("next");
                await WatchUpdated(() => watches.Rows[0].Value == "frame:0:newExpression");
            }
        }
        finally
        {
            await service.StopAsync();
            await run.WaitAsync(Patience, Token);
            Directory.Delete(directory, true);
        }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static async Task<string> Configuration(string directory, string mode, DapSourceBreakpoints[]? breakpoints = null)
    {
        var executable = Path.GetFullPath("../../../dotnet" + (OperatingSystem.IsWindows() ? ".exe" : ""), RuntimeEnvironment.GetRuntimeDirectory());
        var file = Path.Combine(directory, "launch.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new
        {
            executable, arguments = new[] { typeof(Dap.TestAdapter.Program).Assembly.Location, mode },
            adapterId = "fixture", launch = new { }, breakpoints = breakpoints ?? [],
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
    public async Task Immediate_uses_selected_external_frame_refreshes_locals_and_recovers_after_refusal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-evaluate-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log);
        var interpreter = Substitute.For<IDebugController>();
        var immediate = new ImmediateToolViewModel(Substitute.For<ILocalizationService>(), interpreter, service);
        var run = service.StartAsync(await Configuration(directory, "evaluate"));
        try
        {
            await WaitFor(service, () => service.Variables.Count == 1);
            immediate.IsPaused.Should().BeTrue();
            (await immediate.EvaluateAsync("? calleeValue")).Should().Be("frame:0:? calleeValue");
            await service.SelectFrameAsync(service.Stop!, 7);
            var stop = service.Stop;
            (await immediate.EvaluateAsync("callerValue = 99")).Should().Be("frame:7:callerValue = 99");
            service.Variables.Single().Value.Should().Be("99");
            service.SelectedFrame!.Value.GetProperty("id").GetInt32().Should().Be(7);
            service.Stop.Should().Be(stop);
            (await immediate.EvaluateAsync("reject")).Should().Contain("fixture refusal");
            service.CanEvaluate.Should().BeTrue();
            (await immediate.EvaluateAsync("? callerValue")).Should().Be("frame:7:? callerValue");
            await interpreter.DidNotReceiveWithAnyArgs().EvaluateAsync(default!);
        }
        finally
        {
            await service.StopAsync();
            await run.WaitAsync(Patience, Token);
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTheory]
    [InlineData("select")]
    [InlineData("next")]
    [InlineData("continue")]
    [InlineData("end")]
    public async Task Late_evaluation_is_discarded_and_overlapping_submission_is_refused(string action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-evaluate-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log);
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Tool.Immediate.Expired").Returns("expired-context");
        localization.GetString("Str.Tool.Immediate.BreakModeOnly").Returns("not-ready");
        var interpreter = Substitute.For<IDebugController>();
        var immediate = new ImmediateToolViewModel(localization, interpreter, service);
        var pending = false;
        service.Output += output => { if (output == "evaluation-pending\n") pending = true; };
        var run = service.StartAsync(await Configuration(directory, "evaluate"));
        try
        {
            await WaitFor(service, () => service.Variables.Count == 1);
            var evaluation = immediate.EvaluateAsync("wait");
            await WaitFor(service, () => pending);
            service.IsEvaluating.Should().BeTrue();
            (await immediate.EvaluateAsync("second")).Should().Be("not-ready");
            if (action == "select") await service.SelectFrameAsync(service.Stop!, 7);
            else if (action == "end") await service.StopAsync();
            else await service.CommandAsync(action);
            (await evaluation.WaitAsync(Patience, Token)).Should().Be("expired-context");
            if (action is "continue" or "end")
            {
                await run.WaitAsync(Patience, Token);
                service.Variables.Should().BeEmpty();
            }
            else
            {
                await WaitFor(service, () => service.Variables.Count == 1 && service.CanEvaluate);
                service.Variables.Single().Name.Should().Be(action == "select" ? "callerValue" : "calleeValue");
            }
            await interpreter.DidNotReceiveWithAnyArgs().EvaluateAsync(default!);
        }
        finally
        {
            await service.StopAsync();
            await run.WaitAsync(Patience, Token);
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task Immediate_without_external_run_keeps_interpreter_evaluation()
    {
        await using var log = new ConversationLog();
        var interpreter = Substitute.For<IDebugController>();
        interpreter.State.Returns(DebugState.Paused);
        interpreter.EvaluateAsync("? answer").Returns(Task.FromResult<string?>("42"));
        var immediate = new ImmediateToolViewModel(Substitute.For<ILocalizationService>(), interpreter, new(log));
        immediate.IsPaused.Should().BeTrue();
        (await immediate.EvaluateAsync("? answer")).Should().Be("42");
        await interpreter.Received(1).EvaluateAsync("? answer");
    }

    [AvaloniaFact]
    public async Task External_breakpoints_preserve_verification_relocation_events_and_clear_confirmations()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-breakpoints-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Callee.bas");
        var uri = new Uri(path).AbsoluteUri;
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log);
        var config = await Configuration(directory, "breakpoints", [new(path, [6, 10, 20])]);
        var saved = await File.ReadAllTextAsync(config, Token);
        var run = service.StartAsync(config);
        try
        {
            await WaitFor(service, () => service.CanEditBreakpoints && service.Variables.Count == 1);
            var rows = service.GetBreakpoints(uri);
            rows[0].Verified.Should().BeTrue();
            rows[1].Relocated.Should().BeTrue();
            rows[1].Line.Should().Be(11);
            rows[2].Verified.Should().BeFalse();
            rows[2].Message.Should().Be("no executable statement");
            await service.CommandAsync("next");
            await WaitFor(service, () => service.GetBreakpoints(uri)[2].Verified);
            service.GetBreakpoints(uri)[2].Line.Should().Be(21);
            service.ToggleBreakpoint(uri, 6).Should().BeTrue();
            service.GetBreakpoints(uri).Should().OnlyContain(row => row.Pending);
            await WaitFor(service, () => service.GetBreakpoints(uri).Count == 2 && service.GetBreakpoints(uri).All(row => !row.Pending));
            service.ClearAllBreakpoints();
            await WaitFor(service, () => service.GetBreakpoints(uri).Count == 0);
            (await File.ReadAllTextAsync(config, Token)).Should().Be(saved);
        }
        finally
        {
            await service.StopAsync();
            await run.WaitAsync(Patience, Token);
            service.GetBreakpoints(uri).Should().BeEmpty();
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rapid_breakpoint_edits_coalesce_and_old_replies_cannot_revert_the_latest_set(bool end)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-breakpoints-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var uri = new Uri(Path.Combine(directory, "Callee.bas")).AbsoluteUri;
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log);
        var pending = false;
        service.Output += output => { if (output == "breakpoints-pending\n") pending = true; };
        var run = service.StartAsync(await Configuration(directory, "breakpoints-delayed"));
        try
        {
            await WaitFor(service, () => service.CanEditBreakpoints && service.Variables.Count == 1);
            service.ToggleBreakpoint(uri, 30).Should().BeTrue();
            await WaitFor(service, () => pending);
            service.ToggleBreakpoint(uri, 31).Should().BeTrue();
            service.ToggleBreakpoint(uri, 30).Should().BeTrue();
            if (end)
            {
                await service.StopAsync();
                await run.WaitAsync(Patience, Token);
                service.GetBreakpoints(uri).Should().BeEmpty();
                return;
            }
            await service.CommandAsync("next"); // Releases the obsolete [30] reply.
            await WaitFor(service, () => service.GetBreakpoints(uri).Count == 1 && !service.GetBreakpoints(uri)[0].Pending);
            service.GetBreakpoints(uri)[0].RequestedLine.Should().Be(31);
            service.GetBreakpoints(uri)[0].Verified.Should().BeTrue();
        }
        finally
        {
            await service.StopAsync();
            await run.WaitAsync(Patience, Token);
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task Refused_clear_keeps_uncertain_breakpoints_visible_instead_of_claiming_removal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-breakpoints-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Callee.bas");
        var uri = new Uri(path).AbsoluteUri;
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log);
        var run = service.StartAsync(await Configuration(directory, "breakpoints-refuse-clear", [new(path, [6])]));
        try
        {
            await WaitFor(service, () => service.CanEditBreakpoints);
            service.ClearAllBreakpoints();
            await WaitFor(service, () => service.GetBreakpoints(uri).Any(row => !row.Pending && !row.Verified));
            service.GetBreakpoints(uri).Single().Message.Should().Contain("cannot clear");
            service.GetBreakpoints(uri).Single().RequestedLine.Should().Be(6);
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
