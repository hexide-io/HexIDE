using System.Text.Json;
using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Interpreter;
using ModelContextProtocol.Server;

namespace HexIDE.Integration.Tests.Debugging;

public class CallStackReplyTests
{
    private static async Task<JsonElement> Read(HexIdeTools tools, int? limit = null)
    {
        var method = typeof(HexIdeTools).GetMethod("GetCallStackAsync")!;
        var args = method.GetParameters().Select(p => p.ParameterType == typeof(CancellationToken)
            ? (object)TestContext.Current.CancellationToken : limit ?? p.DefaultValue).ToArray();
        var task = (Task)method.Invoke(tools, args)!;
        await task;
        return JsonSerializer.SerializeToElement(task.GetType().GetProperty("Result")!.GetValue(task), ReplyJson.Options);
    }

    private static IdeContext Context(IDebugController debug, MainViewViewModel root) => new(
        Substitute.For<IProjectManager>(), Substitute.For<IDocumentDockService>(), Substitute.For<ILspClient>(),
        null!, Substitute.For<IProjectRunnerService>(), null!, null!, null!, null!, debug, root,
        null!, null!, null!, null!, null!, null!, null!, null!, new Vb6ProjectFileFormat());

    [AvaloniaFact]
    public async Task Deep_recursion_returns_the_deepest_repeated_and_outermost_frames()
    {
        await using var owned = new RealComposition();
        var debug = new DebugController { TimeSliceMs = -1 };
        var vb = new BasicInterpreter(Substitute.For<IBasicStandardLibrary>(),
            new ModuleExecutionContext(), new ExecutionEnvironment(),
            "Main\nSub Main()\nRunaway 1\nEnd Sub\nSub Runaway(ByVal n As Long)\nIf n = 3000 Then\nStop\nExit Sub\nEnd If\nRunaway n + 1\nEnd Sub\n", "Module1")
            { DebugController = debug };
        using var context = Context(debug, owned.Composition.Root);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        debug.Stopped += _ => stopped.TrySetResult();
        var run = vb.Execute();
        try
        {
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
            var reply = await Read(new HexIdeTools(context));
            reply.GetProperty("success").GetBoolean().Should().BeTrue();
            reply.GetProperty("totalFrames").GetInt32().Should().Be(3001);
            var rows = reply.GetProperty("frames").EnumerateArray().ToArray();
            rows.Should().HaveCount(3);
            rows.Select(r => r.GetProperty("proc").GetString()).Should().Equal("Runaway", "Runaway", "Main");
            rows.Select(r => r.GetProperty("line").GetInt32()).Should().Equal(7, 10, 3);
            rows.Select(r => r.GetProperty("count").GetInt32()).Should().Equal(1, 2999, 1);
            reply.TryGetProperty("note", out var note).Should().BeFalse();
        }
        finally
        {
            // Resume to return normally: aborting thousands of frames exercises the separate error-unwind issue #3247.
            if (debug.State == DebugState.Paused) debug.Continue();
            else debug.Stop();
            await run.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        }
    }

    [AvaloniaTheory]
    [InlineData(null, 200)]
    [InlineData(2, 2)]
    [InlineData(301, 301)]
    public async Task Rows_are_bounded_after_collapsing_and_the_note_explains_how_to_expand(int? limit, int expected)
    {
        await using var owned = new RealComposition();
        var debug = Substitute.For<IDebugController>();
        debug.State.Returns(DebugState.Paused);
        debug.GetCallStack().Returns(Enumerable.Range(1, 301)
            .SelectMany(i => Enumerable.Repeat(new CallStackFrame("P", "M", i), 2)).ToArray());
        using var context = Context(debug, owned.Composition.Root);
        var reply = await Read(new HexIdeTools(context), limit);
        reply.GetProperty("totalFrames").GetInt32().Should().Be(602);
        var rows = reply.GetProperty("frames").EnumerateArray().ToArray();
        rows.Should().HaveCount(expected);
        rows.Should().OnlyContain(r => r.GetProperty("count").GetInt32() == 2);
        rows.Select(r => r.GetProperty("line").GetInt32()).Should().Equal(Enumerable.Range(1, expected));
        if (expected < 301)
            reply.GetProperty("note").GetString().Should().Contain($"{301 - expected} rows").And.Contain("maxFrames").And.Contain("301");
        else
            reply.TryGetProperty("note", out var note).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Only_consecutive_frames_identical_in_all_three_fields_collapse()
    {
        await using var owned = new RealComposition();
        var debug = Substitute.For<IDebugController>();
        debug.State.Returns(DebugState.Paused);
        debug.GetCallStack().Returns(new[]
        {
            new CallStackFrame("A", "M", 1), new CallStackFrame("A", "M", 1),
            new CallStackFrame("B", "M", 1), new CallStackFrame("B", "N", 1),
            new CallStackFrame("B", "N", 2), new CallStackFrame("A", "M", 1),
        });
        using var context = Context(debug, owned.Composition.Root);
        var reply = await Read(new HexIdeTools(context));
        reply.GetProperty("frames").EnumerateArray().Select(r =>
            (r.GetProperty("proc").GetString(), r.GetProperty("module").GetString(),
                r.GetProperty("line").GetInt32(), r.GetProperty("count").GetInt32()))
            .Should().Equal(("A", "M", 1, 2), ("B", "M", 1, 1), ("B", "N", 1, 1),
                ("B", "N", 2, 1), ("A", "M", 1, 1));
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Nonpositive_limits_are_refused(int limit)
    {
        await using var owned = new RealComposition();
        var debug = Substitute.For<IDebugController>();
        debug.State.Returns(DebugState.Paused);
        using var context = Context(debug, owned.Composition.Root);
        var reply = await Read(new HexIdeTools(context), limit);
        reply.GetProperty("success").GetBoolean().Should().BeFalse();
        reply.GetProperty("error").GetString().Should().Contain("maxFrames");
    }

    [Fact]
    public void The_generated_schema_makes_maxFrames_optional()
    {
        var tool = McpServerTool.Create(typeof(HexIdeTools).GetMethod("GetCallStackAsync")!, new HexIdeTools(null!));
        var schema = tool.ProtocolTool.InputSchema;
        schema.GetProperty("properties").TryGetProperty("maxFrames", out _).Should().BeTrue();
        if (schema.TryGetProperty("required", out var required))
            required.EnumerateArray().Select(p => p.GetString()).Should().NotContain("maxFrames");
    }
}
