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
            var pipeName = "hexide-" + Guid.NewGuid().ToString("N")[..8];
            var output = Path.Combine(directory, "received.txt");
            // The probe is the test host's launch-probe mode, the same .NET program on every platform. It was a
            // PowerShell script on Windows, the slowest thing this test started: with every core busy, Windows
            // PowerShell 5.1 took 4.0 to 6.6 s to start and exit where the test host took 1.5 to 2.1 s. Its slow
            // starts failed this test before (#1913, #1941), and a pipe case wrote nothing within the transport's
            // 30 s on CI (#2543).
            string[] values = array ? ["", " spaced ", "a\"b", @"a\b", "{pipe}"] : ["legacy"];
            string[] prefix = [Path.Join(AppContext.BaseDirectory, "LspTestHost", "HexIDE.Lsp.TestHost.dll"), "launch-probe"];
            object arguments = array ? prefix.Concat(values).ToArray()
                : string.Join(' ', prefix.Select(a => $"\"{a}\"")) + " legacy";
            var json = JsonSerializer.Serialize(new
            {
                id = "probe", extensions = new[] { ".probe" }, languageId = "probe", transport,
                command = "dotnet", arguments, pipeName,
                environment = new Dictionary<string, string>
                {
                    ["HEXIDE_LAUNCH_PROBE"] = "configured", ["HEXIDE_LAUNCH_OUTPUT"] = output,
                },
            });
            var entry = JsonSerializer.Deserialize(json, LanguageServerConfigJsonContext.Default.LanguageServerEntry)!;
            var factory = new LanguageServerRegistrationFactory(
                Substitute.For<ILoggerFactory>().WithNullLoggers());
            await using var client = factory.Create([entry]).Single().CreateClient();
            // The probe never connects, so a pipe start runs until the transport's own connect timeout of 30 s, and the
            // transport then kills the child; the minute here only bounds the watch. So the start is left pending while
            // the file is watched, and is cancelled once the child has written everything. A stdio start ends when the
            // probe exits, or when initialize goes unanswered for 30 s with the child still running, so the watch does
            // not stop when the start does.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var starting = client.StartAsync(timeout.Token);
            var expectedLines = 2 + values.Length;
            string[] received = [];
            while (!timeout.IsCancellationRequested)
            {
                try
                {
                    if (File.Exists(output) && (received = File.ReadAllLines(output)).Length >= expectedLines)
                        break;
                }
                catch (IOException) { /* held open by something else for a moment */ }
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }
            var ended = starting.IsCompleted;
            await timeout.CancelAsync();
            try { await starting; }
            catch (OperationCanceledException) { /* the start is abandoned once the child has answered */ }
            received.Should().HaveCount(expectedLines,
                "the configured child must have written its received values; after {0:0.0} s the start {1}",
                watch.Elapsed.TotalSeconds, ended ? "had already ended" : "was still running");
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
