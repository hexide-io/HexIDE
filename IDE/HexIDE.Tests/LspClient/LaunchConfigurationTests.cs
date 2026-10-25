using System.Text.Json;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;

namespace HexIDE.Tests.LspClient;

public class LaunchConfigurationTests
{
    [Theory]
    [InlineData("stdio", false)]
    [InlineData("pipe", false)]
    [InlineData("stdio", true)]
    [InlineData("pipe", true)]
    public async Task ConfiguredArgumentsAndEnvironmentReachTheChild(string transport, bool array)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var windows = OperatingSystem.IsWindows();
            var pipeName = "hexide-" + Guid.NewGuid().ToString("N")[..8];
            var script = Path.Combine(directory, windows ? "probe.ps1" : "probe.sh");
            var output = Path.Combine(directory, "received.txt");
            File.WriteAllText(script, windows
                ? "@($env:HEXIDE_LAUNCH_PROBE, $env:PATH) + @($args | ForEach-Object { '[' + $_ + ']' }) | Set-Content -LiteralPath $env:HEXIDE_LAUNCH_OUTPUT"
                : "{ printf '%s\n' \"$HEXIDE_LAUNCH_PROBE\" \"$PATH\"; printf '[%s]\n' \"$@\"; } > \"$HEXIDE_LAUNCH_OUTPUT\"");
            string[] values = array ? ["", " spaced ", "a\"b", @"a\b", "{pipe}"] : ["legacy"];
            string[] prefix = windows ? ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script] : [script];
            object arguments = array ? prefix.Concat(values).ToArray()
                : string.Join(' ', prefix.Select(a => $"\"{a}\"")) + " legacy";
            var json = JsonSerializer.Serialize(new
            {
                id = "probe", extensions = new[] { ".probe" }, languageId = "probe", transport,
                command = windows ? "powershell.exe" : "/bin/sh", arguments, pipeName,
                environment = new Dictionary<string, string>
                {
                    ["HEXIDE_LAUNCH_PROBE"] = "configured", ["HEXIDE_LAUNCH_OUTPUT"] = output,
                },
            });
            var entry = JsonSerializer.Deserialize(json, LanguageServerConfigJsonContext.Default.LanguageServerEntry)!;
            var factory = new LanguageServerRegistrationFactory(
                Substitute.For<ILoggerFactory>().WithNullLoggers());
            await using var client = factory.Create([entry]).Single().CreateClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.StartAsync(timeout.Token);
            File.Exists(output).Should().BeTrue("the configured child must have written its received values");
            var received = File.ReadAllLines(output);
            received[0].Should().Be("configured");
            received[1].Should().Be(Environment.GetEnvironmentVariable("PATH"), "unspecified variables are inherited");
            received.Skip(2).Should().Equal(values.Select(v =>
                "[" + (transport == "pipe" ? v.Replace("{pipe}", pipeName) : v) + "]"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void EnvironmentOnAWebSocketEntryIsReportedAsIgnored()
    {
        var path = Path.Combine(Path.GetTempPath(), "hexide-launch-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
                {"servers":[{"id":"probe","extensions":[".probe"],"languageId":"probe",
                "transport":"websocket","endpoint":"ws://localhost:1234/","environment":{"PROBE":"value"}}]}
                """);
            var result = new LanguageServerConfigLoader(path,
                Substitute.For<ILogger<LanguageServerConfigLoader>>()).Load([]);
            result.Entries.Should().ContainSingle();
            result.Problems.Should().Contain(p => p.Kind == LanguageServerConfigProblemKind.IgnoredField
                && p.Message.Contains("environment"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ArgumentArraysRoundTripWithoutLosingBoundaries()
    {
        const string json = """{"arguments":[""," spaced ","a\"b","a\\b"]}""";
        var entry = JsonSerializer.Deserialize(json, LanguageServerConfigJsonContext.Default.LanguageServerEntry)!;
        var saved = JsonSerializer.Serialize(entry, LanguageServerConfigJsonContext.Default.LanguageServerEntry);
        using var document = JsonDocument.Parse(saved);
        document.RootElement.GetProperty("arguments").EnumerateArray()
            .Select(a => a.GetString()).Should().Equal("", " spaced ", "a\"b", "a\\b");
    }
}
