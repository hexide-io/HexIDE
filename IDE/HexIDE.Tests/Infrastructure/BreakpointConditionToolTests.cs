using System.Text.Json;
using HexIDE.Debugging;
using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Infrastructure;

public class BreakpointConditionToolTests : CompositionTestBase
{
    private sealed class Fixture : IDisposable
    {
        public readonly ProjectDefinition Project = TestHelpers.CreateProjectWithForm("P");
        public readonly BreakpointService Marks = new();
        public readonly IdeContext Context;
        public readonly HexIdeTools Tools;
        public DocumentIdentity Document => DocumentIdentity.For(Project.Forms[0]);

        public Fixture(HexIDE.MainViewViewModel root)
        {
            Project.Forms[0].UpdateCode("Sub Test()\r\nDebug.Print 1\r\nEnd Sub\r\n");
            var projects = Substitute.For<IProjectManager>();
            projects.LoadedProjects.Returns([Project]);
            Context = new IdeContext(projects, Substitute.For<IDocumentDockService>(), Substitute.For<ILspClient>(),
                null!, Substitute.For<IProjectRunnerService>(), null!, null!, null!, Marks, null!, root,
                null!, null!, null!, null!, null!, null!, new LocalizationService(), null!,
                new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader());
            Tools = new HexIdeTools(Context);
        }
        public void Dispose() => Context.Dispose();
    }

    // Bind by name so the same tests exercise main's signature for the regression proof.
    private static async Task<MutateResult> Set(Fixture f, int[] lines, Dictionary<int, string>? conditions = null)
    {
        var method = typeof(HexIdeTools).GetMethod("SetBreakpointsAsync")!;
        var values = new Dictionary<string, object?> { ["name"] = "Form1", ["lines"] = lines,
            ["conditions"] = conditions, ["project"] = null, ["ct"] = TestContext.Current.CancellationToken };
        return await (Task<MutateResult>)method.Invoke(f.Tools, method.GetParameters().Select(p => values[p.Name!]).ToArray())!;
    }

    [AvaloniaFact]
    public async Task Set_stores_a_condition_and_reports_the_conditional_line()
    {
        using var f = new Fixture(Composition.Root);
        var result = await Set(f, [2, 3], new() { [2] = "i = 3" });
        result.Success.Should().BeTrue(result.Error);
        f.Marks.GetCondition(f.Document, 2).Should().Be("i = 3");
        f.Marks.GetCondition(f.Document, 3).Should().BeNull();
        result.Note.Should().Contain("Conditional breakpoint lines: 2").And.Contain("Set line 2's condition \"i = 3\"");
    }

    [AvaloniaFact]
    public async Task Get_reports_conditions_only_when_present()
    {
        using var f = new Fixture(Composition.Root);
        f.Marks.SetDocument(f.Document, [2, 3]);
        f.Marks.SetCondition(f.Document, 2, "i = 3");
        var result = JsonSerializer.SerializeToElement(await f.Tools.GetBreakpointsAsync("Form1"), ReplyJson.Options);
        result.GetProperty("lines").EnumerateArray().Select(x => x.GetInt32()).Should().Equal(2, 3);
        result.TryGetProperty("conditions", out var conditions).Should().BeTrue();
        conditions.GetProperty("2").GetString().Should().Be("i = 3");
        conditions.EnumerateObject().Should().ContainSingle();
        result.GetProperty("note").GetString().Should().Be("Conditional breakpoint lines: 2.");
    }

    [AvaloniaFact]
    public async Task Set_without_conditions_keeps_an_existing_condition()
    {
        using var f = new Fixture(Composition.Root);
        f.Marks.SetDocument(f.Document, [2]);
        f.Marks.SetCondition(f.Document, 2, "i = 3");
        (await Set(f, [2])).Success.Should().BeTrue();
        f.Marks.GetCondition(f.Document, 2).Should().Be("i = 3");
        var result = JsonSerializer.SerializeToElement(await f.Tools.GetBreakpointsAsync("Form1"), ReplyJson.Options);
        result.GetProperty("conditions").GetProperty("2").GetString().Should().Be("i = 3");
        result.GetProperty("lines").EnumerateArray().Select(x => x.GetInt32()).Should().Equal(2);
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Explicit_blank_clears_and_names_the_old_condition(string blank)
    {
        using var f = new Fixture(Composition.Root);
        f.Marks.SetDocument(f.Document, [2, 3]);
        f.Marks.SetCondition(f.Document, 2, "i = 3");
        f.Marks.SetCondition(f.Document, 3, "keep");
        var result = await Set(f, [2, 3], new() { [2] = blank });
        f.Marks.GetCondition(f.Document, 2).Should().BeNull();
        f.Marks.GetCondition(f.Document, 3).Should().Be("keep");
        result.Note.Should().Contain("Cleared line 2's condition \"i = 3\"");
    }

    [AvaloniaFact]
    public async Task A_condition_outside_the_requested_breakpoints_refuses_every_change()
    {
        using var f = new Fixture(Composition.Root);
        f.Marks.SetDocument(f.Document, [3]);
        f.Marks.SetCondition(f.Document, 3, "old");
        var result = await Set(f, [2], new() { [3] = "new" });
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("3").And.Contain("not a breakpoint");
        f.Marks.GetBreakpoints(f.Document).Should().Equal(3);
        f.Marks.GetCondition(f.Document, 3).Should().Be("old");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Plain_and_empty_replies_keep_lines_without_conditions(bool empty)
    {
        using var f = new Fixture(Composition.Root);
        var lines = empty ? Array.Empty<int>() : new[] { 2, 3 };
        (await Set(f, lines)).Success.Should().BeTrue();
        var result = JsonSerializer.SerializeToElement(await f.Tools.GetBreakpointsAsync("Form1"), ReplyJson.Options);
        result.GetProperty("lines").EnumerateArray().Select(x => x.GetInt32()).Should().Equal(lines);
        result.TryGetProperty("conditions", out _).Should().BeFalse();
    }

    [Fact]
    public void Conditions_are_optional_in_the_generated_schema()
    {
        using var f = new Fixture(Composition.Root);
        var tool = ToolSchemas.For(f.Tools).Single(tool => tool.ProtocolTool.Name == "set_breakpoints");
        var schema = tool.ProtocolTool.InputSchema;
        schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).Should().BeEquivalentTo("name", "lines");
        schema.GetProperty("properties").GetProperty("conditions").GetProperty("propertyNames")
            .GetProperty("pattern").GetString().Should().Be("^[0-9]+$");
    }
}
