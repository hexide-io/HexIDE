using System.Collections.Concurrent;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class StdioProcessLoggingTests
{
    [Theory]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    public async Task EachProcessLogCarriesItsServerIdentity(LogLevel level)
    {
        var logs = new ConcurrentQueue<(LogLevel Level, string Message, KeyValuePair<string, object?>[] Properties)>();
        var logger = Substitute.For<ILogger<StdioProcessLspTransport>>();
        logger.WhenForAnyArgs(l => l.Log(default, default, default(object)!, default, default!))
            .Do(call =>
            {
                var state = call[2];
                logs.Enqueue(((LogLevel)call[0], state.ToString()!,
                    ((IEnumerable<KeyValuePair<string, object?>>)state).ToArray()));
            });

        foreach (var id in new[] { "alpha", "bravo" })
        {
            var info = level == LogLevel.Error
                ? new LspServerInfo("hexide-no-such-executable-364", "", "")
                : OperatingSystem.IsWindows()
                    ? new LspServerInfo("cmd.exe", "/c \" >&2 echo stack-frame& exit 3\"", "")
                    : new LspServerInfo("/bin/sh", "-c \">&2 echo stack-frame; exit 3\"", "");
            if (level == LogLevel.Error)
            {
                var loggerFactory = Substitute.For<ILoggerFactory>();
                loggerFactory.CreateLogger(Arg.Any<string>()).Returns(_ => Substitute.For<ILogger>());
                loggerFactory.CreateLogger(typeof(StdioProcessLspTransport).FullName!).Returns(logger);
                var registration = new LanguageServerRegistrationFactory(loggerFactory).Create(
                [
                    new LanguageServerEntry { Id = id, Transport = "stdio", Command = info.FileName }
                ]).Single();
                await using var client = registration.CreateClient();
                await client.StartAsync(TestContext.Current.CancellationToken);
                continue;
            }

            await using var transport = new StdioProcessLspTransport(info, logger, serverId: id);
            var handler = await transport.ConnectAsync(
                new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (logs.Count(l => l.Level == level) < (id == "alpha" ? 1 : 2)
                   && DateTime.UtcNow < deadline)
                await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        var records = logs.Where(l => l.Level == level &&
            (level != LogLevel.Information || l.Message.Contains("Starting language server:", StringComparison.Ordinal)))
            .ToArray();
        records.Should().HaveCount(2);
        for (var i = 0; i < records.Length; i++)
        {
            var id = i == 0 ? "alpha" : "bravo";
            records[i].Properties.Should().Contain(new KeyValuePair<string, object?>("Server", id));
            records[i].Message.Should().Contain($"[{id}]").And.NotContain("VB6").And.NotContain("VB LSP");
        }
        if (level == LogLevel.Debug)
            records.Should().OnlyContain(l => l.Message.Contains("stack-frame"));
        if (level == LogLevel.Warning)
            records.Should().OnlyContain(l => l.Message.Contains("with code 3"));
    }
}
