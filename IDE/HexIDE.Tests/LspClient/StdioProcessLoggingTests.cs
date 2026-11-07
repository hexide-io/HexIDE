using System.Collections.Concurrent;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;

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
        var signals = new[] { "alpha", "bravo" }.ToDictionary(id => id,
            _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        var logger = Substitute.For<ILogger<StdioProcessLspTransport>>();
        logger.WhenForAnyArgs(l => l.Log(default, default, default(object)!, default, default!))
            .Do(call =>
            {
                var state = call[2];
                var properties = ((IEnumerable<KeyValuePair<string, object?>>)state).ToArray();
                var message = state.ToString()!;
                logs.Enqueue(((LogLevel)call[0], message, properties));
                if ((LogLevel)call[0] == level &&
                    (level != LogLevel.Information || message.Contains("Starting language server:", StringComparison.Ordinal)))
                {
                    if (properties.FirstOrDefault(p => p.Key == "Server").Value is string server &&
                        signals.TryGetValue(server, out var signal))
                        signal.TrySetResult();
                }
            });

        foreach (var id in new[] { "alpha", "bravo" })
        {
            var info = level == LogLevel.Error
                ? new LspServerInfo("hexide-no-such-executable-364", "", "")
                : new LspServerInfo(Path.ChangeExtension(
                    typeof(global::HexIDE.Lsp.TestServer.Program).Assembly.Location,
                    OperatingSystem.IsWindows() ? ".exe" : null), "logging", "");
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
            using var handler = (HeaderDelimitedMessageHandler?)await transport.ConnectAsync(
                new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);
            handler.Should().NotBeNull(transport.LastFailure);

            await handler!.WriteAsync(new JsonRpcRequest { RequestId = new RequestId(1), Method = "hexide/ping" },
                TestContext.Current.CancellationToken);
            var reply = await handler.ReadAsync(TestContext.Current.CancellationToken).AsTask()
                .WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);
            reply.Should().BeAssignableTo<JsonRpcResult>().Which.RequestId.Should().Be(new RequestId(1));
            transport.IsAlive.Should().BeTrue("the log fixture must stay alive until explicitly released");

            // Keep the server alive until job assignment and stderr setup finish (#2347). An immediate
            // exit can make Windows refuse job assignment before BeginErrorReadLine is reached.
            await handler!.WriteAsync(new JsonRpcRequest { Method = "exit" },
                TestContext.Current.CancellationToken);
            await ProcessNoticeWait.ForAsync(signals[id].Task, $"[{id}] {level} log",
                TestContext.Current.CancellationToken, received: () =>
                    string.Join("; ", logs.Select(l => $"{l.Level} '{l.Message}'")));
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
