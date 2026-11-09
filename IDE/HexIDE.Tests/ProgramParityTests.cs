using System.Text.Json.Nodes;

namespace HexIDE.Tests;

public class ProgramParityTests
{
    [Theory]
    [InlineData("fixture-global", "Debug.Print il", "[\"il\"]")]
    [InlineData("form-header", "  VERSION 5.00\r\nBegin VB.Form Form1\r\nEnd", "[]")]
    public async Task A_program_missing_its_fixture_is_counted_and_not_run(string reason, string source, string globals)
    {
        var record = new JsonObject
        {
            ["modules"] = new JsonArray(new JsonObject
            {
                ["name"] = "Module1", ["kind"] = "startup", ["source"] = source,
                ["wrapped"] = "Public Sub HexDumpMain()\nEnd Sub",
            }),
            ["printed"] = new JsonArray(),
            ["injectedGlobals"] = JsonNode.Parse(globals),
        };
        await DryRun(record.ToJsonString(), (results, cases) =>
        {
            var result = JsonNode.Parse(results[1])!;
            result["outcome"]!.GetValue<string>().Should().Be("not-reproducible");
            result["notReproducible"]!.GetValue<string>().Should().Be(reason);
            Directory.Exists(cases).Should().BeFalse();
            var summary = JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(cases)!, "summary.json")))!;
            summary["outcomes"]!["not-reproducible"]!.GetValue<int>().Should().Be(1);
        });
    }

    [Theory]
    [InlineData(false, "LineLabel")]
    [InlineData(true, "DocumentLine")]
    public async Task The_launched_adapter_receives_the_selected_Erl_numbering(bool overrideDefault, string expected)
    {
        var adapter = Path.ChangeExtension(typeof(Dap.TestAdapter.Program).Assembly.Location, OperatingSystem.IsWindows() ? ".exe" : null);
        await RunTool("""
            {"modules":[{"name":"Module1","kind":"startup","source":"","wrapped":"Public Sub HexDumpMain()\nEnd Sub"}],"printed":[]}
            """, overrideDefault ? ["--erl-line-numbering", expected] : [], new()
        {
            ["HEXIDE_PARITY_ADAPTER"] = adapter,
            ["HEXIDE_DAP_FIXTURE_MODE"] = "erl-environment",
            ["Configuration__Environment__ErlLineNumbering"] = "inherited",
        }, (results, _) =>
        {
            // Read back from the real child over DAP, rather than only inspecting launch options (#3133).
            JsonNode.Parse(results[1])!["engine"]!["stdout"]!.GetValue<string>().Should().Be(expected + "\n");
            var header = JsonNode.Parse(results[0])!;
            header["erlLineNumbering"]!.GetValue<string>().Should().Be(expected);
            header["erlLineNumberingReason"]!.GetValue<string>().Should().ContainAll("vb6.exe", "line label", "DocumentLine", "by design", "RD-VBAL 6.1.2.7.1.14");
        });
    }

    [Theory]
    [InlineData(null, null, "", "adapter's default")]
    [InlineData(null, "Compiled", "Compiled", "adapter's default")]
    [InlineData("Interpreter", "Compiled", "Interpreter", "Interpreter")]
    public async Task The_results_header_names_the_engine_and_the_adapter_gets_the_one_chosen(
        string? chosen, string? inherited, string adapterSees, string header)
    {
        var adapter = Path.ChangeExtension(typeof(Dap.TestAdapter.Program).Assembly.Location, OperatingSystem.IsWindows() ? ".exe" : null);
        var environment = new Dictionary<string, string>
        {
            ["HEXIDE_PARITY_ADAPTER"] = adapter,
            ["HEXIDE_DAP_FIXTURE_MODE"] = "engine-environment",
        };
        if (inherited is not null) environment["Configuration__Environment__ExecutionEngine"] = inherited;
        await RunTool("""
            {"modules":[{"name":"Module1","kind":"startup","source":"","wrapped":"Public Sub HexDumpMain()\nEnd Sub"}],"printed":[]}
            """, chosen is null ? [] : ["--execution-engine", chosen], environment, (results, _) =>
        {
            JsonNode.Parse(results[1])!["engine"]!["stdout"]!.GetValue<string>().ReplaceLineEndings("\n").Should().Be(adapterSees + "\n");
            JsonNode.Parse(results[0])!["executionEngine"]!.GetValue<string>().Should().Be(header);
        });
    }

    [Theory]
    [InlineData(0, "vb6")]
    [InlineData(1, "Kept")]
    public async Task The_launched_adapter_receives_VB6_language_and_Err_Clear_settings(int line, string expected)
    {
        var adapter = Path.ChangeExtension(typeof(Dap.TestAdapter.Program).Assembly.Location, OperatingSystem.IsWindows() ? ".exe" : null);
        await RunTool("""
            {"modules":[{"name":"Module1","kind":"startup","source":"","wrapped":"Public Sub HexDumpMain()\nEnd Sub"}],"printed":[]}
            """, [], new()
        {
            ["HEXIDE_PARITY_ADAPTER"] = adapter,
            ["HEXIDE_DAP_FIXTURE_MODE"] = "vb6-environment",
            ["Configuration__Workspace__Language"] = "inherited",
            ["Configuration__Environment__ErlAfterErrClear"] = "inherited",
        }, (results, _) =>
        {
            // Read the launched child's environment back over DAP (#3317, #3188).
            JsonNode.Parse(results[1])!["engine"]!["stdout"]!.GetValue<string>()
                .ReplaceLineEndings("\n").Split('\n')[line].Should().Be(expected);
        });
    }

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

    [Theory]
    [InlineData("VBR00011", 11, "both-error-match")]
    [InlineData("VBA00006", 6, "both-error-match")]
    [InlineData("VBR00006", 11, "both-error-differ")]
    [InlineData("Overflow", 6, "hexide-run-time-error-engine-refused")]
    public void An_exception_stop_compares_the_number_in_its_id(string exceptionId, int hexideNumber, string outcome)
    {
        // Measured at RDCore 84278ae: VBR for a run-time error and VBA for an application error, with nothing on stderr.
        var engine = new Engine { Exception = new JsonObject { ["info"] = new JsonObject { ["exceptionId"] = exceptionId } } };
        Comparison.Outcome(new Hexide([], [], "VBRunTimeException", hexideNumber, null), engine).Should().Be(outcome);
    }

    [Fact]
    public void An_exception_stop_is_an_engine_error_where_HexIDE_ran_cleanly()
    {
        var engine = new Engine { Exception = new JsonObject { ["info"] = new JsonObject { ["exceptionId"] = "VBR00011" } } };
        Comparison.Outcome(new Hexide([], [], null, null, null), engine).Should().Be("engine-error-only");
    }

    [Theory]
    [InlineData("exception", "terminate")]
    [InlineData("exception-no-terminate", "disconnect")]
    public async Task An_unhandled_error_is_read_at_its_first_stop_and_the_program_ended_there(string mode, string endedBy)
    {
        // The fixture stops again on the same line if continued, as RDCore does since its #574, so a runner that
        // continues from the stop never sees the program end.
        var adapter = Path.ChangeExtension(typeof(Dap.TestAdapter.Program).Assembly.Location, OperatingSystem.IsWindows() ? ".exe" : null);
        await RunTool("""
            {"modules":[{"name":"Module1","kind":"startup","source":"","wrapped":"Public Sub HexDumpMain()\nEnd Sub"}],"printed":[{"text":"before","type":"String"}],"lines":["before"],"locale":"en-US","error":{"kind":"VBRunTimeException","number":11,"message":"Division by zero"}}
            """, ["--timeout", "30"], new() { ["HEXIDE_PARITY_ADAPTER"] = adapter, ["HEXIDE_DAP_FIXTURE_MODE"] = mode }, (results, _) =>
        {
            var result = JsonNode.Parse(results[1])!;
            result["outcome"]!.GetValue<string>().Should().Be("both-error-match");
            var engine = result["engine"]!;
            engine["stops"]!.AsArray().Select(s => s!.GetValue<string>()).Should().Equal("exception");
            engine["endedAtException"]!.GetValue<string>().Should().Be(endedBy);
            engine["errorNumber"]!.GetValue<int>().Should().Be(11);
            engine["stdout"]!.GetValue<string>().Should().Be("before\n");
        });
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

    [Theory]
    [InlineData("results.jsonl")]
    [InlineData("summary.json")]
    [InlineData("cases/00001/Module1.bas")]
    [InlineData("cases/00001/.rdproj")]
    public async Task Existing_output_is_preserved(string relativePath)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hexide-parity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var dump = Path.Combine(dir, "dump.jsonl");
            await File.WriteAllTextAsync(dump,
                """{"modules":[{"name":"Module1","kind":"startup","source":"","wrapped":"Public Sub HexDumpMain()\nEnd Sub"}],"printed":[]}""",
                TestContext.Current.CancellationToken);
            var output = Path.Combine(dir, "out");
            // The case paths are written with '/', which Windows lists back as '\'.
            var existing = Path.GetFullPath(Path.Combine(output, relativePath));
            Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
            await File.WriteAllTextAsync(existing, "user-owned content", TestContext.Current.CancellationToken);
            var start = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(typeof(Comparison).Assembly.Location);
            foreach (var arg in new[] { dump, "--dry-run", "--out", output })
                start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            await Task.WhenAll(stdout, stderr);
            File.ReadAllText(existing).Should().Be("user-owned content");
            process.ExitCode.Should().NotBe(0);
            Directory.GetFiles(output, "*", SearchOption.AllDirectories).Should().Equal(existing);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Runs the tool with <c>--dry-run</c> on a one-line dump, and checks its results and case folders.</summary>
    private static Task DryRun(string dumpLine, Action<string[], string> check) => RunTool(dumpLine, ["--dry-run"], [], check);

    /// <summary>Runs the tool on a one-line dump with the given options and environment, and checks its results and case folders.</summary>
    private static async Task RunTool(string dumpLine, string[] options, Dictionary<string, string> environment, Action<string[], string> check)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hexide-parity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var dump = Path.Combine(dir, "dump.jsonl");
            await File.WriteAllTextAsync(dump, dumpLine, TestContext.Current.CancellationToken);
            var start = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false };
            start.ArgumentList.Add(typeof(Comparison).Assembly.Location);
            foreach (var arg in new[] { dump, "--out", Path.Combine(dir, "out") }.Concat(options))
                start.ArgumentList.Add(arg);
            foreach (var (name, value) in environment)
                start.Environment[name] = value;
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
