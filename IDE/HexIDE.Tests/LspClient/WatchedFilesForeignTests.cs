using HexIDE.Conversations;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;

namespace HexIDE.Tests.LspClient;

public sealed class WatchedFilesForeignTests
{
    [ForeignServerFact("python")]
    public async Task RuffRereadsPyprojectAfterAnExternalEdit()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "hexide-ruff-watch-" + Guid.NewGuid().ToString("N"))).FullName;
        using var logs = LoggerFactory.Create(_ => { });
        await using var capture = new ConversationLog();
        capture.Arm("ruff-watch", armed: true);
        var workspace = Substitute.For<ILspWorkspace>();
        workspace.Directory.Returns(root);
        workspace.Folders.Returns(new[] { new LspWorkspaceFolder("test", root) });
        var info = new LspServerInfo(ForeignServer.Python.Find()!, ForeignServer.Python.ServerArguments, root);
        await using var client = new VBLspClient(
            new StdioProcessLspTransport(info, logs.CreateLogger<StdioProcessLspTransport>()),
            logs.CreateLogger<VBLspClient>(), ForeignServer.Python.LanguageId,
            capture: capture, connectionId: "ruff-watch", workspace: workspace);
        try
        {
            var config = Path.Combine(root, "pyproject.toml");
            await File.WriteAllTextAsync(config, "[tool.ruff.lint]\nselect = [\"F401\"]\n", TestContext.Current.CancellationToken);
            var path = Path.Combine(root, "sample.py");
            const string source = "import os\n";
            await File.WriteAllTextAsync(path, source, TestContext.Current.CancellationToken);
            var initial = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            client.DiagnosticsPublished += (_, p) =>
            {
                if (p.Diagnostics.Any(d => d.CodeText == "F401")) initial.TrySetResult();
                if (initial.Task.IsCompleted && p.Diagnostics.Length == 0) refreshed.TrySetResult();
            };
            await client.StartAsync();
            await client.OpenDocumentAsync(new Uri(path).AbsoluteUri, source);
            await initial.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            // Diagnostics and registration are independent. An edit made before registration is
            // acknowledged has no watcher yet and is correctly not reported.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (true)
            {
                await capture.DrainAsync();
                if (capture.Snapshot("ruff-watch").Any(e => e.Method == "client/registerCapability"
                    && e.Direction == ConversationDirection.Received && e.Outcome == ConversationOutcome.Answered)) break;
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException("Ruff did not register watched files: " + string.Join(" | ",
                        capture.Snapshot("ruff-watch").Select(e => $"{e.Direction} {e.Method} {e.Outcome} {e.Detail}")));
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            await File.WriteAllTextAsync(config, "[tool.ruff.lint]\nselect = [\"F401\"]\nignore = [\"F401\"]\n", TestContext.Current.CancellationToken);
            await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            await capture.DrainAsync();
            var wire = capture.Snapshot("ruff-watch");
            wire.Should().Contain(e => e.Method == "client/registerCapability");
            wire.Should().Contain(e => e.Method == "workspace/didChangeWatchedFiles");
        }
        finally
        {
            await client.StopAsync();
            Directory.Delete(root, true);
        }
    }
}
