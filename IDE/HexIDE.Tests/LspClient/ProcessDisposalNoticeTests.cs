using System.Collections.Concurrent;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class ProcessDisposalNoticeTests
{
    [Theory]
    [InlineData("stdio", "clean")]
    [InlineData("pipe", "clean")]
    [InlineData("stdio", "hang")]
    [InlineData("pipe", "hang")]
    public async Task Disposal_reports_the_exit_once_and_logs_the_server_and_outcome(string channel, string mode)
    {
        var logs = new ConcurrentQueue<(LogLevel Level, string Message)>();
        var logger = Substitute.For<ILogger>();
        logger.WhenForAnyArgs(l => l.Log(default, default, default(object)!, default, default!))
            .Do(call => logs.Enqueue(((LogLevel)call[0], call[2].ToString()!)));
        var factory = Substitute.For<ILoggerFactory>();
        factory.CreateLogger(Arg.Any<string>()).Returns(logger);
        var host = Path.Join(AppContext.BaseDirectory, "LspTestHost", "HexIDE.Lsp.TestHost.dll");
        var pipeName = $"h491-{Guid.NewGuid():N}"[..20];
        var arguments = new[] { host, "dispose-server", mode };
        ILspTransport transport = channel == "stdio"
            ? new StdioProcessLspTransport(new LspServerInfo("dotnet", "", "") { ArgumentList = arguments },
                factory.CreateLogger<StdioProcessLspTransport>(), serverId: "disposal-server")
            : new NamedPipeLspTransport(pipeName, NamedPipeRole.Listen,
                factory.CreateLogger<NamedPipeLspTransport>(),
                new NamedPipeLaunch("dotnet", "", ArgumentList: [.. arguments, pipeName]));
        await using var owned = transport;
        var notices = new ConcurrentQueue<TransportNotice>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.Notice += (_, notice) =>
        {
            notices.Enqueue(notice);
            if (notice.Text == "dispose-ready") ready.TrySetResult();
        };
        var closed = 0;
        transport.Closed += (_, _) => Interlocked.Increment(ref closed);
        await using var handler = (HeaderDelimitedMessageHandler?)await transport.ConnectAsync(
            new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);
        handler.Should().NotBeNull();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var disposing = transport.DisposeAsync();
        await handler!.DisposeAsync();
        await disposing;

        var exits = notices.Where(n => n.Text.StartsWith("process exited", StringComparison.Ordinal)).ToArray();
        exits.Should().ContainSingle();
        if (mode == "clean") exits.Single().Text.Should().Be("process exited with code 37");
        var identity = channel == "stdio" ? "disposal-server" : "dotnet";
        logs.Should().Contain(l => l.Level == LogLevel.Information && l.Message.Contains(identity)
            && l.Message.Contains(mode == "clean" ? "with code 37" : "killed after"));
        closed.Should().Be(0, "deliberate disposal must not announce an unexpected disconnect");
    }
}
