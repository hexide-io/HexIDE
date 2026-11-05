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
    private sealed class InspectionLog : Serilog.Core.ILogEventSink
    {
        public List<Serilog.Events.LogEvent> Events { get; } = [];
        public void Emit(Serilog.Events.LogEvent logEvent) => Events.Add(logEvent);
    }

    [AvaloniaFact]
    public async Task Inspection_failure_logs_the_exception_and_stack_at_debug_level()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-inspection-log-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await using var capture = new ConversationLog();
        var service = new ExternalDebugService(capture);
        var sink = new InspectionLog();
        var previous = Serilog.Log.Logger;
        using var logger = new Serilog.LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        Serilog.Log.Logger = logger;
        var failure = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Failed += message => failure.TrySetResult(message);
        service.Changed += () =>
        {
            if (service.SelectedFrame is not null) throw new InvalidOperationException("inspection callback failed");
        };
        Task? run = null;
        try
        {
            run = service.StartAsync(await Configuration(directory, "frames"));
            (await failure.Task.WaitAsync(Patience, Token)).Should().Be("inspection callback failed");
            var entry = sink.Events.Should().ContainSingle(item => item.Exception is InvalidOperationException).Subject;
            entry.Level.Should().Be(Serilog.Events.LogEventLevel.Debug);
            entry.Exception!.Message.Should().Be("inspection callback failed");
            entry.Exception.StackTrace.Should().Contain(nameof(Inspection_failure_logs_the_exception_and_stack_at_debug_level));
        }
        finally
        {
            await service.StopAsync();
            if (run is not null) await run.WaitAsync(Patience, Token);
            Serilog.Log.Logger = previous;
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTheory]
    [InlineData("conditions", true, true)]
    [InlineData("conditions", true, false)]
    [InlineData("breakpoints", false, true)]
    [InlineData("breakpoints", false, false)]
    public async Task Conditional_breakpoints_send_capability_gated_expressions_and_notice_once_per_session(string mode, bool capable, bool initiallyConditional)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-conditions-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var module = TestHelpers.CreateModule();
        module.AbsolutePath = Path.Combine(directory, "Module1.bas");
        var document = HexIDE.Runtime.ProjectElements.DocumentIdentity.For(module);
        var store = new BreakpointService();
        store.SetDocument(document, [6, 20]);
        if (initiallyConditional) store.SetCondition(document, 6, "i = 3");
        var strings = Substitute.For<ILocalizationService>();
        strings.GetString("Str.Debug.Conditions.AdapterIgnored").Returns("conditions ignored");
        await using var log = new ConversationLog { ArmsEveryConnection = true };
        var service = new ExternalDebugService(log, strings, store);
        var output = new List<string>();
        service.Output += output.Add;
        var file = await Configuration(directory, mode);
        try
        {
            for (var iteration = 0; iteration < 2; ++iteration)
            {
                var run = service.StartAsync(file);
                await WaitFor(service, () => service.CanEditBreakpoints && service.Variables.Count > 0);
                var uri = new Uri(module.AbsolutePath).AbsoluteUri;
                service.GetCondition(uri, 6).Should().Be(initiallyConditional ? "i = 3" : null);
                service.GetBreakpoints(uri).Single(row => row.RequestedLine == 20).Verified.Should().BeFalse();
                service.GetBreakpoints(uri).Single(row => row.RequestedLine == 20).Message.Should().NotBeEmpty();
                service.SetCondition(uri, 6, "j = 4");
                await WaitFor(service, () => service.GetBreakpoints(uri).All(row => !row.Pending));
                service.SetCondition(uri, 6, "");
                await WaitFor(service, () => service.GetBreakpoints(uri).All(row => !row.Pending));
                service.GetCondition(uri, 6).Should().BeNull();
                service.GetBreakpoints(uri).Should().Contain(row => row.RequestedLine == 6);
                await log.DrainAsync();
                var frames = log.Snapshot(service.Connection!.Id)
                    .Where(row => row.Method == "setBreakpoints" && row.Kind == ConversationEntryKind.Request).ToArray();
                frames.Should().HaveCount(3);
                for (var index = 0; index < frames.Length; ++index)
                {
                    using var frame = JsonDocument.Parse(log.Body(service.Connection.Id, frames[index].Sequence)!.Head);
                    var arguments = frame.RootElement.GetProperty("arguments");
                    arguments.GetProperty("source").GetProperty("path").GetString().Should().Be(module.AbsolutePath);
                    var items = arguments.GetProperty("breakpoints");
                    items[0].GetProperty("line").GetInt32().Should().Be(6);
                    items[1].EnumerateObject().Select(property => property.Name).Should().Equal("line");
                    if (capable && (index == 1 || index == 0 && initiallyConditional))
                        items[0].GetProperty("condition").GetString().Should().Be(index == 0 ? "i = 3" : "j = 4");
                    else items[0].EnumerateObject().Select(property => property.Name).Should().Equal("line");
                }
                output.Count(line => line.Contains("conditions ignored", StringComparison.Ordinal)).Should().Be(capable ? 0 : iteration + 1);
                await service.StopAsync();
                await run.WaitAsync(Patience, Token);
            }
        }
        finally { await service.StopAsync(); Directory.Delete(directory, true); }
    }

    [AvaloniaTheory]
    [InlineData("goto")]
    [InlineData("goto-empty")]
    [InlineData("goto-refuse")]
    [InlineData("goto-targets-refuse")]
    public async Task Set_next_statement_uses_caret_source_first_target_and_stopped_thread_or_keeps_inspection(string mode)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-goto-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await using var log = new ConversationLog { ArmsEveryConnection = true };
        var service = new ExternalDebugService(log);
        var failures = new List<string>();
        service.Failed += failures.Add;
        var run = service.StartAsync(await Configuration(directory, mode));
        try
        {
            await WaitFor(service, () => service.CanEvaluate && service.Variables.Count > 0);
            service.SupportsSetNextStatement.Should().BeTrue();
            var stop = service.Stop;
            var frames = service.Frames;
            var variables = service.Variables;
            var path = Path.Combine(directory, "Caret source.bas");
            await service.SetNextStatementAsync(new Uri(path).AbsoluteUri, 19);
            if (mode == "goto")
            {
                await WaitFor(service, () => service.CurrentLocation?.Line == 19 && service.CanEvaluate);
                service.Stop.Should().NotBe(stop);
                failures.Should().BeEmpty();
            }
            else
            {
                await WaitFor(service, () => service.CanEvaluate);
                service.State.Should().Be(DapSessionState.Stopped);
                service.CurrentLocation!.Value.Line.Should().Be(6);
                service.Frames.Should().BeSameAs(frames);
                service.Variables.Should().BeSameAs(variables);
                failures.Should().Equal("fixture goto refusal");
            }
            var id = service.Connection!.Id;
            await log.DrainAsync();
            var requests = log.Snapshot(id).Where(entry => entry.Kind == ConversationEntryKind.Request &&
                entry.Method is "gotoTargets" or "goto").ToArray();
            requests.Select(entry => entry.Method).Should().Equal(mode is "goto-empty" or "goto-targets-refuse" ? ["gotoTargets"] : ["gotoTargets", "goto"]);
            if (requests.Length > 0)
            {
                using var frame = JsonDocument.Parse(log.Body(id, requests[0].Sequence)!.Head);
                var arguments = frame.RootElement.GetProperty("arguments");
                arguments.GetProperty("source").GetProperty("path").GetString().Should().Be(path);
                arguments.GetProperty("line").GetInt32().Should().Be(19);
            }
            if (requests.Length > 1)
            {
                using var frame = JsonDocument.Parse(log.Body(id, requests[1].Sequence)!.Head);
                var arguments = frame.RootElement.GetProperty("arguments");
                arguments.GetProperty("targetId").GetInt32().Should().Be(19);
                arguments.GetProperty("threadId").GetInt32().Should().Be(1);
            }
        }
        finally { await service.StopAsync(); await run.WaitAsync(Patience, Token); Directory.Delete(directory, true); }
    }

    [AvaloniaTheory]
    [InlineData("\"i = 3\"")]
    [InlineData("null")]
    public async Task Breakpoint_condition_is_refused_before_launch_with_localized_file_and_entry(string condition)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-condition-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Debug.External.BreakpointConditionUnsupported").Returns("conditions unavailable: {0}, entry {1}");
        var service = new ExternalDebugService(log, localization);
        var failures = new List<string>();
        service.Failed += failures.Add;
        try
        {
            var file = Path.Combine(directory, "launch.json");
            await File.WriteAllTextAsync(file, """
                {"executable":"must-not-start","adapterId":"fixture","launch":{},
                 "breakpoints":[{"path":"first.bas","lines":[1]},
                                {"path":"second.bas","lines":[2],"condition":CONDITION}]}
                """.Replace("CONDITION", condition), Token);
            await service.StartAsync(file).WaitAsync(Patience, Token);
            failures.Should().Equal($"conditions unavailable: {file}, entry 2");
            service.Connection.Should().BeNull();
            service.IsActive.Should().BeFalse();
        }
        finally { await service.StopAsync(); Directory.Delete(directory, true); }
    }

    [AvaloniaTheory]
    [InlineData("condition", "en")]
    [InlineData("condition", "de")]
    [InlineData("hitCondition", "en")]
    [InlineData("hitCondition", "de")]
    [InlineData("logMessage", "en")]
    [InlineData("logMessage", "de")]
    public async Task Unsupported_breakpoint_option_is_localized_before_launch(string option, string language)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-option-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        var localization = new LocalizationService();
        localization.Apply(language);
        var service = new ExternalDebugService(log, localization);
        var failures = new List<string>();
        service.Failed += failures.Add;
        try
        {
            var file = Path.Combine(directory, "launch.json");
            await File.WriteAllTextAsync(file, """
                {"executable":"must-not-start","adapterId":"fixture","launch":{},
                 "breakpoints":[{"path":"first.bas","lines":[1]},
                                {"path":"second.bas","lines":[2],"OPTION":"3"}]}
                """.Replace("OPTION", option), Token);
            await service.StartAsync(file).WaitAsync(Patience, Token);
            var expected = option == "condition"
                ? language == "de"
                    ? $"Legen Sie Bedingungen über Bedingung… im Kontextmenü der Haltepunktleiste fest. Datei '{file}', Haltepunkteintrag 2."
                    : $"Set conditions with Condition… in the breakpoint gutter menu. File '{file}', breakpoint entry 2."
                : language == "de"
                    ? $"Die Haltepunktoption '{option}' wird für externe Sitzungen noch nicht unterstützt. Datei '{file}', Haltepunkteintrag 2. Legen Sie Bedingungen über Bedingung… im Kontextmenü der Haltepunktleiste fest."
                    : $"Breakpoint option '{option}' isn't supported for external sessions yet. File '{file}', breakpoint entry 2. Set conditions with Condition… in the breakpoint gutter menu.";
            failures.Should().Equal(expected);
            service.Connection.Should().BeNull();
            service.IsActive.Should().BeFalse();
        }
        finally { await service.StopAsync(); Directory.Delete(directory, true); localization.Apply("en"); }
    }

    [AvaloniaTheory]
    [InlineData("{}")]
    [InlineData("{\"areas\":[]}")]
    [InlineData("{\"areas\":[\"stacks\"]}")]
    [InlineData("{\"areas\":[\"variables\"],\"stackFrameId\":7}")]
    [InlineData("{\"areas\":[\"all\"],\"threadId\":1}")]
    [InlineData("{\"areas\":[\"future-area\"]}")]
    public async Task Invalidated_refreshes_stopped_inspection_preserves_caller_and_expires_hover(string body)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-invalidated-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log);
        var watches = new WatchesToolViewModel(Substitute.For<ILocalizationService>(), new WatchService(),
            Substitute.For<IDebugController>(), Substitute.For<IWindowManager>(), service);
        watches.ExternalService.Add(new("value", WatchType.Expression, "Caller"));
        var failures = new List<string>();
        service.Failed += failures.Add;
        var pending = false;
        service.Output += output => { if (output == "evaluation-pending\n") pending = true; };
        var run = service.StartAsync(await Configuration(directory, "invalidated"));
        try
        {
            await WaitFor(service, () => service.Variables.Count == 1);
            await service.SelectFrameAsync(service.Stop!, 7);
            await WatchUpdated(() => watches.Rows.Single().Value == "42");
            var stop = service.Stop!;
            var hover = service.EvaluateHoverAsync("wait", Token);
            await WaitFor(service, () => pending);
            await service.EvaluateHoverAsync("invalidate:" + body, Token);
            await WaitFor(service, () => service.Stop != stop && service.Variables.Count == 1);
            service.State.Should().Be(DapSessionState.Stopped);
            service.SelectedFrame!.Value.GetProperty("id").GetInt32().Should().Be(7);
            service.SelectedLocation!.Value.Line.Should().Be(13);
            service.CurrentLocation!.Value.Line.Should().Be(6);
            service.Variables.Single().Value.Should().Be("99");
            (await hover.WaitAsync(Patience, Token)).Should().BeNull();
            await WatchUpdated(() => watches.Rows.Single().Value == "99");
            failures.Should().BeEmpty();
        }
        finally { await service.StopAsync(); await run.WaitAsync(Patience, Token); Directory.Delete(directory, true); }
    }

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
            if (mode == "frames")
            {
                service.SupportsSetNextStatement.Should().BeFalse();
                var stop = service.Stop;
                await service.SetNextStatementAsync(new Uri(Path.Combine(directory, "Callee.bas")).AbsoluteUri, 19);
                service.Stop.Should().Be(stop);
                await log.DrainAsync();
                log.Snapshot(service.Connection!.Id).Should().NotContain(entry => entry.Method == "gotoTargets" || entry.Method == "goto");
            }
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
            watches.SelectedItem = watches.Rows[1];
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

    internal static async Task<string> Configuration(string directory, string mode, DapSourceBreakpoints[]? breakpoints = null)
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

    internal static Task WaitFor(ExternalDebugService service, Func<bool> predicate)
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

    // Regression for the measured adapter refusal in hexide-io/HexIDE#2912.
    [AvaloniaTheory]
    [InlineData("continue", 0)]
    [InlineData("next", 0)]
    [InlineData("stepIn", 0)]
    [InlineData("stepOut", 0)]
    [InlineData("continue", 7)]
    [InlineData("next", 7)]
    [InlineData("stepIn", 7)]
    [InlineData("stepOut", 7)]
    public async Task Refused_resume_preserves_inspection_and_selection_until_an_accepted_resume(string command, int frameId)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-refusal-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log);
        var localization = Substitute.For<ILocalizationService>();
        var interpreter = Substitute.For<IDebugController>();
        var locals = new LocalsToolViewModel(localization, interpreter, service);
        var stack = new CallStackToolViewModel(localization, interpreter, service);
        var failures = new List<string>();
        service.Failed += failures.Add;
        var run = service.StartAsync(await Configuration(directory, "resume-refusal"));
        try
        {
            await WaitFor(service, () => service.CanEvaluate && service.Variables.Count == 1);
            await service.SelectFrameAsync(service.Stop!, frameId);
            var stop = service.Stop!;
            var frames = service.Frames;
            var variables = service.Variables;
            var location = service.CurrentLocation;
            var selectedLocation = service.SelectedLocation;
            var pendingWasReadOnly = false;
            var lostInspection = false;
            void Observe()
            {
                lostInspection |= service.Frames.Count == 0 || service.Variables.Count == 0 || service.CurrentLocation is null;
                if (service.State == DapSessionState.Running && service.Stop == stop)
                    pendingWasReadOnly |= !service.CanEvaluate && !service.IsCurrent(stop);
            }
            service.Changed += Observe;
            await service.CommandAsync(command);
            await WaitFor(service, () => service.State == DapSessionState.Stopped && service.Stop != stop);
            service.Changed -= Observe;
            lostInspection.Should().BeFalse();
            pendingWasReadOnly.Should().BeTrue();
            service.Frames.Should().BeSameAs(frames);
            service.Variables.Should().BeSameAs(variables);
            service.CurrentLocation.Should().Be(location);
            service.SelectedLocation.Should().Be(selectedLocation);
            stack.SelectedFrame!.FrameId.Should().Be(frameId);
            locals.Roots.Single().Value.Should().Be(frameId == 0 ? "41" : "42");
            service.IsCurrent(stop).Should().BeFalse();
            service.CanSelectFrame(stop, frameId).Should().BeFalse();
            service.CanEvaluate.Should().BeTrue();
            stack.SelectedFrame!.Stop.Should().Be(service.Stop);
            stack.SelectedFrame = stack.Frames.Single(row => row.FrameId != frameId);
            await WaitFor(service, () => service.CanEvaluate && service.SelectedFrame?.GetProperty("id").GetInt32() != frameId);
            failures.Should().ContainSingle().Which.Should().Contain("fixture resume refusal");
            var refusedStop = service.Stop;
            await service.CommandAsync("next");
            await WaitFor(service, () => service.CanEvaluate && service.Variables.Count == 1 && service.Stop != refusedStop && service.SelectedFrame?.GetProperty("id").GetInt32() == 0);
            service.Frames.Should().NotBeSameAs(frames);
            locals.Roots.Single().Value.Should().Be("41");
            await service.CommandAsync("stepIn"); // Accepted without a continued or stopped event.
            await WaitFor(service, () => service.State == DapSessionState.Running && service.Stop is null);
            service.Frames.Should().BeEmpty();
            service.Variables.Should().BeEmpty();
            service.CurrentLocation.Should().BeNull();
        }
        finally { await service.StopAsync(); await run.WaitAsync(Patience, Token); Directory.Delete(directory, true); }
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

    private static HexIDE.Addins.AddinLaunchConfiguration Contributed(string mode) => new()
    {
        Executable = Path.GetFullPath("../../../dotnet" + (OperatingSystem.IsWindows() ? ".exe" : ""), RuntimeEnvironment.GetRuntimeDirectory()),
        Arguments = [typeof(Dap.TestAdapter.Program).Assembly.Location, mode],
        AdapterId = "fixture",
        Launch = JsonDocument.Parse("{}").RootElement,
    };

    [AvaloniaFact]
    public async Task A_configuration_made_as_the_session_starts_is_given_the_saved_gutter_marks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-made-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var module = TestHelpers.CreateModule();
        module.AbsolutePath = Path.Combine(directory, "Module1.bas");
        var store = new BreakpointService();
        store.SetDocument(HexIDE.Runtime.ProjectElements.DocumentIdentity.For(module), [6]);
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log, breakpoints: store);
        var run = service.StartAsync(_ => Task.FromResult(DapLaunchConfiguration.FromContributed(Contributed("breakpoints"))));
        try
        {
            await WaitFor(service, () => service.CanEditBreakpoints);
            service.GetBreakpoints(new Uri(module.AbsolutePath).AbsoluteUri).Should().ContainSingle(row => row.RequestedLine == 6,
                "the configuration carries no breakpoints; the session is given the IDE's");
        }
        finally
        {
            await service.StopAsync();
            await run.WaitAsync(Patience, Token);
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task Ending_while_the_configuration_is_being_made_calls_the_making_off()
    {
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log);
        var making = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = service.StartAsync(async token =>
        {
            making.TrySetResult(token);
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        });
        var token = await making.Task.WaitAsync(Patience, Token);
        await service.StopAsync();
        await run.WaitAsync(Patience, Token);

        token.IsCancellationRequested.Should().BeTrue();
        service.IsActive.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Ending_while_a_maker_that_ignores_its_token_makes_the_configuration_still_ends_the_session()
    {
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log);
        var making = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource<DapLaunchConfiguration>(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = service.StartAsync(_ =>
        {
            making.TrySetResult();
            return never.Task;
        });
        await making.Task.WaitAsync(Patience, Token);
        await service.StopAsync();
        await run.WaitAsync(Patience, Token);
        service.IsActive.Should().BeFalse("End must end it, whether or not the add-in listens");

        // The maker fails afterwards, with nobody waiting for it: the failure is logged rather than lost. The deadline
        // only bounds a log line that never comes.
        using (var captured = new HexIDE.Tests.Sidecar.CapturedLog())
        {
            never.SetException(new InvalidOperationException("rdcore.json was not found"));
            var deadline = DateTime.UtcNow + Patience;
            while (!captured.Messages.ToArray().Any(m => m.Contains("failed after its start was ended")) && DateTime.UtcNow < deadline)
                await Task.Delay(10, Token);
            captured.Messages.ToArray().Should().Contain(m => m.Contains("failed after its start was ended"));
        }

        var asked = false;
        await service.StartAsync(_ =>
        {
            asked = true;
            throw new InvalidOperationException("the next start is asked for its configuration");
        }).WaitAsync(Patience, Token);
        asked.Should().BeTrue("the session is idle again, so a new start is not ignored");
    }

    [Fact]
    public void A_contributed_configuration_is_checked_as_a_file_is_but_takes_its_paths_whole()
    {
        var made = DapLaunchConfiguration.FromContributed(Contributed("silent") with
        {
            Arguments = ["two words", "$literal"],
            Environment = new Dictionary<string, string?> { ["RDCORE_ROOT"] = "x", ["GONE"] = null },
        });
        made.WorkingDirectory.Should().Be(Path.GetDirectoryName(made.Executable), "the executable's folder when none is given");
        made.Arguments.Should().Equal("two words", "$literal");
        made.Environment.Should().Contain("GONE", null);
        made.Breakpoints.Should().BeEmpty();

        var relative = () => DapLaunchConfiguration.FromContributed(Contributed("silent") with { Executable = "adapter.exe" });
        relative.Should().Throw<InvalidDataException>("there is no file for a relative path to be relative to");
        var relativeFolder = () => DapLaunchConfiguration.FromContributed(Contributed("silent") with { WorkingDirectory = "bin" });
        relativeFolder.Should().Throw<InvalidDataException>();
        var notAnObject = () => DapLaunchConfiguration.FromContributed(Contributed("silent") with
        {
            Launch = JsonDocument.Parse("[]").RootElement,
        });
        notAnObject.Should().Throw<InvalidDataException>();
        var noAdapter = () => DapLaunchConfiguration.FromContributed(Contributed("silent") with { AdapterId = " " });
        noAdapter.Should().ThrowExactly<InvalidDataException>("refused as its xmldoc says, as every other part is");
    }
}
