using System.Text.Json;
using Avalonia.Headless.XUnit;
using HexIDE.Conversations;
using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Runtime.Serialization;
using HexIDE.Tests.DebugAdapter;

namespace HexIDE.Tests.Infrastructure;

public class ExternalInspectionToolTests : CompositionTestBase
{
    [AvaloniaTheory]
    [InlineData("stack", false)]
    [InlineData("stack", true)]
    [InlineData("locals", false)]
    [InlineData("locals", true)]
    [InlineData("evaluate", false)]
    [InlineData("evaluate", true)]
    [InlineData("add", false)]
    [InlineData("add", true)]
    [InlineData("watches", false)]
    [InlineData("watches", true)]
    [InlineData("set", false)]
    [InlineData("set", true)]
    [InlineData("clear", false)]
    [InlineData("clear", true)]
    [InlineData("locals-tree", false)]
    [InlineData("locals-depth", false)]
    [InlineData("stack-no-source", false)]
    [InlineData("stack-limit", false)]
    [InlineData("locals-caller", false)]
    [InlineData("evaluate-caller", false)]
    [InlineData("watch-caller", false)]
    [InlineData("watch-break", false)]
    [InlineData("watch-reject", false)]
    [InlineData("set-unsupported", false)]
    [InlineData("set-unsaved", false)]
    [InlineData("set-preserve", false)]
    [InlineData("set-blank", false)]
    [InlineData("set-empty", false)]
    [InlineData("clear-refused", false)]
    public async Task Tools_use_the_external_windows_data_and_breakpoint_wire(string tool, bool running)
    {
        var composition = Composition;
        var external = composition.ExternalDebugger;
        var capture = composition.Capture;
        capture.ArmsEveryConnection = true;
        var directory = Path.Combine(Path.GetTempPath(), "hexide-inspection-tools-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var project = TestHelpers.CreateProjectWithModule("P", "Callee");
        var module = project.Modules[0];
        module.AbsolutePath = Path.Combine(directory, "Callee.bas");
        module.UpdateCode(string.Join(Environment.NewLine, Enumerable.Repeat("' target", 25)));
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        using var context = new IdeContext(projects, Substitute.For<IDocumentDockService>(), Substitute.For<ILspClient>(),
            null!, composition.ProjectRunnerService, null!, null!, null!, composition.BreakpointService,
            composition.DebugController, composition.Root, null!, null!, null!, null!, capture, null!,
            composition.LocalizationService, null!, new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader());
        var tools = new HexIdeTools(context);
        var mode = running ? "conditions-running" : tool switch { "locals-tree" or "locals-depth" => "locals-tree", "stack-no-source" => "session", "set-unsupported" => "breakpoints", "clear-refused" => "breakpoints-refuse-clear", _ => "conditions" };
        var run = external.StartAsync(await ExternalDebugServiceTests.Configuration(directory, mode));
        try
        {
            await ExternalDebugServiceTests.WaitFor(external, () => external.CanEvaluate && external.Variables.Count == 1);
            if (tool.EndsWith("-caller", StringComparison.Ordinal))
            {
                await external.SelectFrameAsync(external.Stop!, 7);
                await ExternalDebugServiceTests.WaitFor(external, () => external.CanEvaluate && external.Variables.Count == 1);
            }
            if (tool == "set-unsaved") module.AbsolutePath = null;
            if (tool is "watches" or "watch-caller" or "watch-reject")
                composition.Root.Watches.ExternalService.Add(new(tool == "watch-reject" ? "reject" : "value", HexIDE.Debugging.WatchType.Expression, "Callee"));
            if (tool is "clear" or "clear-refused" or "set-preserve" or "set-blank" or "set-empty")
            {
                external.ToggleBreakpoint(new Uri(module.AbsolutePath!).AbsoluteUri, 10);
                external.SetCondition(new Uri(module.AbsolutePath!).AbsoluteUri, 10, "value = 3");
            }
            if (running)
            {
                await external.CommandAsync("continue");
                await ExternalDebugServiceTests.WaitFor(external, () => external.Stop is null && external.State == HexIDE.Dap.DapSessionState.Running);
            }
            object reply = tool switch
            {
                "stack" or "stack-no-source" => await tools.GetCallStackAsync(),
                "stack-limit" => await tools.GetCallStackAsync(1),
                "locals" or "locals-caller" or "locals-tree" => await tools.GetLocalsAsync(),
                "locals-depth" => await tools.GetLocalsAsync(1),
                "evaluate" or "evaluate-caller" => await tools.EvaluateAsync("value"),
                "add" => await tools.AddWatchAsync("value"),
                "watch-break" => await tools.AddWatchAsync("value", "BreakWhenTrue"),
                "watches" or "watch-caller" or "watch-reject" => await tools.GetWatchesAsync(),
                "set-preserve" => await tools.SetBreakpointsAsync("Callee", [10]),
                "set-blank" => await tools.SetBreakpointsAsync("Callee", [10], conditions: new() { [10] = "" }),
                "set-empty" => await tools.SetBreakpointsAsync("Callee", []),
                "set" or "set-unsupported" or "set-unsaved" => await tools.SetBreakpointsAsync("Callee", [10, 20], conditions: new() { [10] = "value = 3" }),
                _ => await tools.ClearAllBreakpointsAsync(TestContext.Current.CancellationToken),
            };
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(reply));
            var result = json.RootElement;
            if (running && tool is not ("set" or "clear"))
            {
                result.GetProperty("Success").GetBoolean().Should().BeFalse();
                result.GetProperty("Error").GetString().Should().Contain("running").And.Contain("break_program").And.Contain("breakpoint");
                return;
            }
            if (tool is "watch-break" or "set-unsaved" or "clear-refused")
            {
                result.GetProperty("Success").GetBoolean().Should().BeFalse();
                result.GetProperty("Error").GetString().Should().Contain(tool == "watch-break" ? "Expression" : tool == "set-unsaved" ? "saved" : "cannot clear fixture breakpoints");
                if (tool == "watch-break") composition.Root.Watches.ExternalService.Watches.Should().BeEmpty();
                if (tool != "clear-refused") return;
            }
            else result.GetProperty("Success").GetBoolean().Should().BeTrue();
            if (reply is CallStackResult or LocalsResult or WatchesResult)
                result.TryGetProperty("Project", out _).Should().BeFalse("external frames have no interpreter project identity");
            if (reply is EvaluateResult || tool is "add" or "watches" or "watch-caller" or "watch-reject")
            {
                await capture.DrainAsync();
                var id = external.Connection!.Id;
                var sent = capture.Snapshot(id).Last(entry => entry.Kind == ConversationEntryKind.Request && entry.Method == "evaluate");
                using var wire = JsonDocument.Parse(capture.Body(id, sent.Sequence)!.Head);
                wire.RootElement.GetProperty("arguments").GetProperty("context").GetString().Should().Be(reply is EvaluateResult ? "repl" : "watch");
            }
            switch (tool)
            {
                case "stack-no-source":
                    var noSource = ((CallStackResult)reply).Frames.Should().ContainSingle().Subject;
                    noSource.Proc.Should().Be("Main");
                    JsonSerializer.Serialize(noSource).Should().NotContain("Module");
                    break;
                case "stack-limit":
                    ((CallStackResult)reply).Frames.Should().ContainSingle();
                    ((CallStackResult)reply).TotalFrames.Should().Be(2);
                    ((CallStackResult)reply).Note.Should().Contain("1 rows omitted");
                    break;
                case "stack":
                    var frames = ((CallStackResult)reply).Frames;
                    frames.Select(row => row.Proc).Should().Equal("Callee", "Caller");
                    frames.Select(row => row.Line).Should().Equal(6, 12);
                    frames[0].Module.Should().EndWith("Callee.bas");
                    break;
                case "locals-tree":
                    var root = ((LocalsResult)reply).Locals.Should().ContainSingle().Subject;
                    root.HasChildren.Should().BeTrue();
                    var child = root.Children.Should().ContainSingle().Subject;
                    child.Expression.Should().Be("self");
                    child.Repeats.Should().BeTrue();
                    child.Children.Should().BeNull();
                    break;
                case "locals-depth":
                    var capped = ((LocalsResult)reply).Locals.Should().ContainSingle().Subject;
                    capped.HasChildren.Should().BeTrue();
                    capped.Children.Should().BeNull();
                    break;
                case "locals-caller":
                    ((LocalsResult)reply).Locals.Should().ContainSingle().Which.Expression.Should().Be("callerValue");
                    break;
                case "evaluate-caller": ((EvaluateResult)reply).Result.Should().Be("frame:7:value"); break;
                case "locals":
                    ((LocalsResult)reply).Locals.Should().ContainSingle().Which.Should().BeEquivalentTo(
                        new LocalsRow("calleeValue", "41", "Long", false, null));
                    break;
                case "evaluate": ((EvaluateResult)reply).Result.Should().Be("frame:0:value"); break;
                case "watch-reject":
                    var rejected = ((WatchesResult)reply).Watches.Should().ContainSingle().Subject;
                    rejected.Ok.Should().BeFalse();
                    rejected.Value.Should().Contain("fixture refusal");
                    break;
                case "add":
                case "watch-caller":
                case "watches":
                    var watch = ((WatchesResult)reply).Watches.Should().ContainSingle().Subject;
                    watch.Value.Should().Be(tool == "watch-caller" ? "frame:7:value" : "frame:0:value");
                    watch.Type.Should().Be("Long");
                    watch.Ok.Should().BeTrue();
                    composition.Root.Watches.ExternalService.Watches.Should().ContainSingle();
                    composition.Root.Watches.Service.Watches.Should().BeEmpty();
                    break;
                default:
                    await capture.DrainAsync();
                    var id = external.Connection!.Id;
                    var sent = capture.Snapshot(id).Last(entry => entry.Kind == ConversationEntryKind.Request && entry.Method == "setBreakpoints");
                    using (var wire = JsonDocument.Parse(capture.Body(id, sent.Sequence)!.Head))
                    {
                        var args = wire.RootElement.GetProperty("arguments");
                        args.GetProperty("source").GetProperty("path").GetString().Should().Be(module.AbsolutePath);
                        var points = args.GetProperty("breakpoints").EnumerateArray().ToArray();
                        var expectedLines = tool switch { "set-preserve" or "set-blank" => new[] { 10 }, "set" or "set-unsupported" => new[] { 10, 20 }, _ => Array.Empty<int>() };
                        points.Select(point => point.GetProperty("line").GetInt32()).Should().Equal(expectedLines);
                        if (tool is "set" or "set-preserve") points[0].GetProperty("condition").GetString().Should().Be("value = 3");
                        if (tool is "set-unsupported" or "set-blank") points[0].TryGetProperty("condition", out _).Should().BeFalse();
                    }
                    if (tool.StartsWith("set", StringComparison.Ordinal))
                    {
                        if (tool is "set" or "set-unsupported") ((MutateResult)reply).Note.Should().Contain("11").And.Contain("no executable statement");
                        if (tool is "set-blank" or "set-empty") external.GetCondition(new Uri(module.AbsolutePath!).AbsoluteUri, 10).Should().BeNull();
                        if (tool == "set-empty") external.GetBreakpoints(new Uri(module.AbsolutePath!).AbsoluteUri).Should().BeEmpty();
                        if (tool == "set-unsupported") ((MutateResult)reply).Note.Should().Contain("unconditional");
                        if (tool == "set")
                        {
                            // get_breakpoints reads back the set set_breakpoints edited, not the interpreter's.
                            var read = await tools.GetBreakpointsAsync("Callee");
                            read.Lines.Should().Equal(10, 20);
                            read.Conditions.Should().BeEquivalentTo(new Dictionary<int, string> { [10] = "value = 3" });
                            read.Note.Should().Contain("11").And.Contain("no executable statement");
                        }
                    }
                    else if (tool == "clear-refused") external.GetBreakpoints(new Uri(module.AbsolutePath!).AbsoluteUri).Should().NotBeEmpty();
                    else external.GetBreakpoints(new Uri(module.AbsolutePath!).AbsoluteUri).Should().BeEmpty();
                    break;
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
