using System.Text.Json.Nodes;

namespace HexIDE.Tests;

public class ProgramParityTests
{
    [Theory]
    [InlineData("Run-time error '11': Division by zero")]
    [InlineData("Application error '11': Division by zero")]
    public void Both_error_prefixes_compare_the_number(string message)
    {
        var engine = new Engine { ExitCode = 1 };
        engine.Stderr.Append(message);
        Comparison.Outcome(new Hexide([], [], "VBRunTimeException", 11, null), engine)
            .Should().Be("both-error-match");
    }

    [Fact]
    public void Statement_lines_are_compared_instead_of_individual_items()
    {
        var record = JsonNode.Parse("""
            {"printed":[{"text":"1","type":"Integer"},{"text":"a","type":"String"}],"lines":["1a"],"locale":"en-US"}
            """)!.AsObject();
        var engine = new Engine();
        engine.Stdout.Append("1a\r\n");
        var hexide = Hexide.From(record);
        hexide.Printed.Should().Equal("1", "a");
        hexide.Types.Should().Equal("Integer", "String");
        Comparison.Outcome(hexide, engine).Should().Be("match");
    }

    [Fact]
    public void A_different_recorded_locale_is_reported_separately()
    {
        var record = JsonNode.Parse("""
            {"printed":[],"lines":[],"locale":"de-DE"}
            """)!.AsObject();
        Comparison.Outcome(Hexide.From(record), new Engine()).Should().Be("locale-differs");
    }

    [Fact]
    public void Each_case_has_its_own_project_name()
    {
        const string template = """{"ProjectInfo":{"Name":"ParityCase"}}""";
        Comparison.Project(template, 1, [new JsonObject { ["RelativeUri"] = "Module1.bas" }])["ProjectInfo"]!["Name"]!
            .GetValue<string>().Should().Be("ParityCase00001");
        Comparison.Project(template, 2, [])["ProjectInfo"]!["Name"]!.GetValue<string>().Should().Be("ParityCase00002");
    }

    [Theory]
    [InlineData("stderr")]
    [InlineData("failure")]
    public void The_missing_entry_failure_is_identified_for_retry(string field)
    {
        var result = new JsonObject
        {
            ["engine"] = new JsonObject { [field] = "'Module1.HexDumpMain' is not a procedure of the module" }
        };
        Comparison.NeedsRetry(result).Should().BeTrue();
        result["engine"]![field] = "Run-time error '11': Division by zero";
        Comparison.NeedsRetry(result).Should().BeFalse();
    }

    [Fact]
    public async Task A_module_without_bare_statements_reaches_workspace_generation()
    {
        await DryRun("""
            {"modules":[{"name":"Module1","kind":"startup","bare":false,"source":"Dim n As Long","wrapped":"Dim n As Long\nPublic Sub HexDumpMain()\nEnd Sub\n"}],"printed":[],"lines":[],"locale":"en-US"}
            """, (results, cases) =>
        {
            JsonNode.Parse(results[1])!["outcome"]!.GetValue<string>().Should().Be("dry-run");
            File.ReadAllText(Path.Combine(cases, "00001", "Module1.bas"))
                .ReplaceLineEndings("\n").Should().Contain("Public Sub HexDumpMain()\nEnd Sub\n");
        });
    }

    [Fact]
    public async Task A_startup_module_the_dump_could_not_wrap_is_counted_and_not_run()
    {
        // Running what is left would compare another engine's answer to a program HexIDE never ran (#2211).
        await DryRun("""
            {"modules":[{"name":"Module1","kind":"startup","source":"#ElseIf 1","notWrapped":"the source does not parse here"}],"printed":[],"lines":[],"locale":"en-US"}
            """, (results, cases) =>
        {
            var result = JsonNode.Parse(results[1])!;
            result["outcome"]!.GetValue<string>().Should().Be("not-wrapped");
            result["notWrapped"]!.GetValue<string>().Should().Be("the source does not parse here");
            Directory.Exists(Path.Combine(cases, "00001")).Should().BeFalse();
        });
    }

    /// <summary>Runs the tool with <c>--dry-run</c> on a one-line dump, and checks its results and case folders.</summary>
    private static async Task DryRun(string dumpLine, Action<string[], string> check)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hexide-parity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var dump = Path.Combine(dir, "dump.jsonl");
            await File.WriteAllTextAsync(dump, dumpLine, TestContext.Current.CancellationToken);
            var start = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false };
            start.ArgumentList.Add(typeof(Comparison).Assembly.Location);
            foreach (var arg in new[] { dump, "--dry-run", "--out", Path.Combine(dir, "out") })
                start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start)!;
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            process.ExitCode.Should().Be(0);
            check(File.ReadAllLines(Path.Combine(dir, "out", "results.jsonl")), Path.Combine(dir, "out", "cases"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
