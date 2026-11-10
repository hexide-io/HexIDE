using System.Text.Json;
using Avalonia.Headless.XUnit;
using HexIDE.Conversations;
using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Tests.DebugAdapter;

namespace HexIDE.Tests.Infrastructure;

public class ExternalRunControlTests : CompositionTestBase
{
    [Fact]
    public void Source_paths_resolve_symlinked_parent_directories()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-source-path-" + Guid.NewGuid());
        var root = Path.Combine(directory, "root");
        var rootLink = Path.Combine(directory, "root-link");
        var real = Path.Combine(rootLink, "real");
        var link = Path.Combine(rootLink, "link");
        Directory.CreateDirectory(Path.Combine(root, "real", "nested"));
        try
        {
            Directory.CreateSymbolicLink(rootLink, root);
            Directory.CreateSymbolicLink(link, real);
            var expected = ResolvePath(Path.Combine(real, "nested", "Callee.bas"));
            ResolvePath(Path.Combine(link, "nested", "Callee.bas")).Should().Be(expected);
            ResolvePath(Path.Combine(link, "nested", "Other.bas")).Should().NotBe(expected);
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            if (Directory.Exists(rootLink)) Directory.Delete(rootLink);
            Directory.Delete(directory, true);
        }
    }

    // The adapter builds source paths from its process cwd, which resolves macOS's /var symlink.
    // Walk parents too: the fixture reports source filenames without creating the files.
    private static string ResolvePath(string path)
    {
        var full = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(full);
        if (parent is null) return full;
        var resolved = Path.Combine(ResolvePath(parent), Path.GetFileName(full));
        FileSystemInfo info = Directory.Exists(resolved) ? new DirectoryInfo(resolved) : new FileInfo(resolved);
        var target = info.Exists ? info.ResolveLinkTarget(true) : null;
        return target is null ? resolved : ResolvePath(target.FullName);
    }

    [AvaloniaTheory]
    [InlineData("goto")]
    [InlineData("disconnect-held")]
    public async Task Debug_state_and_stop_describe_the_external_session(string mode)
    {
        var composition = Composition;
        var external = composition.ExternalDebugger;
        var directory = Path.Combine(Path.GetTempPath(), "hexide-state-dap-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var projects = Substitute.For<IProjectManager>();
        var project = TestHelpers.CreateProjectWithModule("P", "ChangedModule");
        projects.LoadedProjects.Returns([project]);
        composition.ProjectManager.AddProject(project);
        using var context = new IdeContext(projects, Substitute.For<IDocumentDockService>(),
            Substitute.For<ILspClient>(), null!, composition.ProjectRunnerService, null!, null!, null!, null!,
            composition.DebugController, composition.Root, null!, null!, null!, null!, composition.Capture,
            null!, composition.LocalizationService, null!, new Vb6ProjectFileFormat());
        var tools = new HexIdeTools(context);
        var printed = new ProgramPrinted();
        printed.FollowRuns(composition.ProjectRunnerService);
        printed.Append("earlier run\r\n");
        var run = external.StartAsync(await ExternalDebugServiceTests.Configuration(directory, mode));
        try
        {
            await ExternalDebugServiceTests.WaitFor(external, () => external.CanEvaluate);
            printed.Take().Should().BeEmpty("an external session also begins with a fresh output buffer");
            printed.Append("pending\r\n");
            var state = await tools.GetDebugStateAsync(TestContext.Current.CancellationToken);
            state.Running.Should().BeTrue();
            state.State.Should().Be("Paused");
            state.StopReason.Should().Be("Breakpoint");
            state.AdapterStopReason.Should().Be("breakpoint");
            state.Module.Should().Be(external.Frames[0].GetProperty("source").GetProperty("path").GetString());
            ResolvePath(state.Module!).Should().Be(ResolvePath(Path.Combine(directory, "Callee.bas")));
            state.Project.Should().BeNull();
            if (mode == "goto")
            {
                await external.SetNextStatementAsync(new Uri(Path.Combine(directory, "Callee.bas")).AbsoluteUri, 19);
                await ExternalDebugServiceTests.WaitFor(external, () => external.Stop?.Reason == "goto" && external.CanEvaluate);
                state = await tools.GetDebugStateAsync(TestContext.Current.CancellationToken);
                state.StopReason.Should().Be("Step");
                state.AdapterStopReason.Should().Be("goto");
            }
            project.Modules[0].UpdateCode("' prepared for the next run");
            var edited = JsonSerializer.Serialize(await tools.GetDebugStateAsync(TestContext.Current.CancellationToken));
            edited.Should().Contain("changed since").And.Contain("P.ChangedModule");
            var reply = await tools.StopProjectAsync(TestContext.Current.CancellationToken);
            reply.Success.Should().BeTrue();
            reply.Note.Should().Contain("External session output is not captured");
            reply.Printed.Should().BeNull();
            printed.Take().Should().Be("pending\n", "ending a session must not reset the buffer");
            await ExternalDebugServiceTests.WaitFor(external, () => external.State == HexIDE.Dap.DapSessionState.Ended);
            state = await tools.GetDebugStateAsync(TestContext.Current.CancellationToken);
            state.Running.Should().BeFalse();
            state.State.Should().Be("Ended");
            JsonSerializer.Serialize(state).Should().NotContain("changed since");
            state.StopReason.Should().BeNull();
            state.AdapterStopReason.Should().BeNull();
            await run.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            state = await tools.GetDebugStateAsync(TestContext.Current.CancellationToken);
            state.Running.Should().BeFalse();
            state.State.Should().Be("Ended");
            JsonSerializer.Serialize(state).Should().NotContain("changed since");
        }
        finally
        {
            await external.StopAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTheory]
    [InlineData("step_into", "stepIn", "run-control")]
    [InlineData("step_into", "stepIn", "run-control-delayed")]
    [InlineData("step_over", "next", "run-control")]
    [InlineData("step_out", "stepOut", "run-control")]
    [InlineData("continue_program", "continue", "run-control")]
    [InlineData("continue_program", "continue", "run-control-running")]
    [InlineData("continue_program", "continue", "run-control-end")]
    [InlineData("break_program", "pause", "run-control-running")]
    [InlineData("step_into", "stepIn", "resume-refusal")]
    [InlineData("step_over", "next", "resume-refusal")]
    [InlineData("step_out", "stepOut", "resume-refusal")]
    [InlineData("continue_program", "continue", "resume-refusal")]
    [InlineData("set_next_statement", "goto", "goto")]
    [InlineData("set_next_statement", "goto", "goto-refuse")]
    [InlineData("set_next_statement", "gotoTargets", "goto-empty")]
    [InlineData("set_next_statement", "gotoTargets", "goto-targets-refuse")]
    [InlineData("run_to_cursor", "", "run-control")]
    [InlineData("set_next_statement_unsaved", "", "goto")]
    [InlineData("step_over", "next", "session")]
    [InlineData("step_into", "", "evaluate-running")]
    public async Task Tools_send_menu_commands_and_report_external_state(string tool, string command, string mode)
    {
        var composition = Composition;
        var external = composition.ExternalDebugger;
        await using var capture = composition.Capture;
        capture.ArmsEveryConnection = true;
        var directory = Path.Combine(Path.GetTempPath(), "hexide-tools-dap-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var projects = Substitute.For<IProjectManager>();
        var project = TestHelpers.CreateProjectWithModule("Target", "Callee");
        var document = project.Modules[0];
        document.AbsolutePath = tool == "set_next_statement_unsaved" ? null : Path.Combine(directory, "Target.bas");
        document.UpdateCode(string.Join(Environment.NewLine, Enumerable.Repeat("' target", 25)));
        projects.LoadedProjects.Returns([project]);
        using var context = new IdeContext(projects, Substitute.For<IDocumentDockService>(), Substitute.For<ILspClient>(),
            null!, composition.ProjectRunnerService, null!, null!, null!, null!, composition.DebugController,
            composition.Root, null!, null!, null!, null!, capture, null!, composition.LocalizationService, null!, new Vb6ProjectFileFormat());
        var tools = new HexIdeTools(context);
        var run = external.StartAsync(await ExternalDebugServiceTests.Configuration(directory, mode));
        try
        {
            await ExternalDebugServiceTests.WaitFor(external, () => external.CanEvaluate && external.Variables.Count == 1);
            if (tool == "break_program" || mode == "evaluate-running")
            {
                await external.CommandAsync("continue");
                await ExternalDebugServiceTests.WaitFor(external, () => composition.ProjectRunnerService.CanBreakProject && external.Stop is null);
            }
            var id = external.Connection!.Id;
            var inspection = external.Frames;
            var variables = external.Variables;
            var reply = tool switch
            {
                "step_into" => await tools.StepIntoAsync(TestContext.Current.CancellationToken),
                "step_over" => await tools.StepOverAsync(TestContext.Current.CancellationToken),
                "step_out" => await tools.StepOutAsync(TestContext.Current.CancellationToken),
                "break_program" => await tools.BreakProgramAsync(TestContext.Current.CancellationToken),
                "set_next_statement" or "set_next_statement_unsaved" => await tools.SetNextStatementAsync("Callee", 19, ct: TestContext.Current.CancellationToken),
                "run_to_cursor" => await tools.RunToCursorAsync("Callee", 19, ct: TestContext.Current.CancellationToken),
                _ => await tools.ContinueProgramAsync(TestContext.Current.CancellationToken),
            };
            if (tool == "run_to_cursor")
            {
                reply.Success.Should().BeFalse();
                reply.Error.Should().Contain("one-shot breakpoint");
                return;
            }
            if (tool == "set_next_statement_unsaved" || mode == "evaluate-running")
            {
                reply.Success.Should().BeFalse();
                reply.Error.Should().Contain(tool == "set_next_statement_unsaved" ? "must be saved" : "must be paused");
                external.Frames.Should().BeSameAs(inspection);
                await capture.DrainAsync();
                capture.Snapshot(id).Should().NotContain(entry => entry.Kind == ConversationEntryKind.Request &&
                    (entry.Method == "stepIn" || entry.Method == "gotoTargets" || entry.Method == "goto"));
                return;
            }
            var refused = mode is "resume-refusal" or "goto-refuse" or "goto-empty" or "goto-targets-refuse";
            reply.Success.Should().Be(!refused);
            if (refused)
            {
                reply.Error.Should().Contain(mode == "resume-refusal" ? "fixture resume refusal" : "fixture goto refusal");
                reply.Note.Should().Contain("inspection was kept");
                external.Frames.Should().BeSameAs(inspection);
                external.Variables.Should().BeSameAs(variables);
            }
            else if (mode == "run-control-end") reply.Note.Should().Contain("ended");
            else if (mode == "run-control-delayed" || mode == "run-control-running" && tool == "continue_program") reply.Note.Should().Contain("Running");
            else if (mode == "session") reply.Note.Should().Contain("Paused; the adapter supplied no source location");
            else reply.Note.Should().Contain("Paused at").And.Contain("Callee.bas").And.Contain(mode == "goto" ? "line 19" : "line 9");
            await capture.DrainAsync();
            var sent = capture.Snapshot(id).Last(entry => entry.Kind == ConversationEntryKind.Request && entry.Method == command);
            using var frame = JsonDocument.Parse(capture.Body(id, sent.Sequence)!.Head);
            var arguments = frame.RootElement.GetProperty("arguments");
            if (command != "gotoTargets") arguments.GetProperty("threadId").GetInt32().Should().Be(1);
            if (command == "goto") arguments.GetProperty("targetId").GetInt32().Should().Be(19);
            if (tool == "set_next_statement")
            {
                reply.Project.Should().Be("Target");
                reply.Document.Should().Be("Callee");
                var targets = capture.Snapshot(id).Last(entry => entry.Kind == ConversationEntryKind.Request && entry.Method == "gotoTargets");
                using var targetFrame = JsonDocument.Parse(capture.Body(id, targets.Sequence)!.Head);
                var target = targetFrame.RootElement.GetProperty("arguments");
                target.GetProperty("source").GetProperty("path").GetString().Should().Be(document.AbsolutePath);
                target.GetProperty("line").GetInt32().Should().Be(19);
            }
        }
        finally
        {
            await external.StopAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Directory.Delete(directory, true);
        }
    }
}
