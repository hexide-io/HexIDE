using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;

namespace HexIDE.Tests.LspClient;

public class DiagnosticLifecycleFollowUpTests
{
    [Fact]
    public async Task PullReportForRetiredUriDoesNotReachSubscribers()
    {
        await using var client = new VBLspClient(Substitute.For<ILspTransport>(),
            Substitute.For<ILogger<VBLspClient>>(), "vb6");
        const string uri = "untitled:Project1/Module1.bas";
        await client.CloseDocumentForRenameAsync(uri, TestContext.Current.CancellationToken);
        var received = new List<PublishDiagnosticsParams>();
        client.DiagnosticsPublished += (_, p) => received.Add(p);
        using var report = JsonDocument.Parse("""{"kind":"full","items":[]} """);

        client.ApplyDiagnosticReport(uri, report.RootElement);

        received.Should().BeEmpty();
        await client.OpenDocumentAsync(uri, "", TestContext.Current.CancellationToken);
        client.ApplyDiagnosticReport(uri, report.RootElement);
        received.Should().ContainSingle();
    }

    [Fact]
    public async Task ThrowingDeliveryStillDrainsQueuedPublicationAndRenameClear()
    {
        var logger = Substitute.For<ILogger<VBLspClient>>();
        await using var client = new VBLspClient(Substitute.For<ILspTransport>(), logger, "vb6");
        const string uri = "untitled:Project1/Module1.bas";
        var order = new List<string>();
        var first = true;
        var renameReturned = false;
        string[]? orderAtRenameReturn = null;
        var failure = new InvalidOperationException("handler failed");
        client.DiagnosticsPublished += (_, _) =>
        {
            if (!first) { order.Add("queued"); return; }
            first = false;
            client.PublishServerDiagnostics(new PublishDiagnosticsParams("file:///other.bas", []));
            renameReturned = Task.Run(() => client.CloseDocumentForRenameAsync(uri, TestContext.Current.CancellationToken))
                .Wait(TimeSpan.FromSeconds(5));
            orderAtRenameReturn = order.ToArray();
            throw failure;
        };
        client.DocumentRetired += (_, _) => order.Add("retired");

        var publish = () => client.PublishServerDiagnostics(new PublishDiagnosticsParams(uri, []));
        publish.Should().NotThrow();

        renameReturned.Should().BeTrue("a handler may wait for a rename on another thread");
        orderAtRenameReturn.Should().BeEmpty("the clear must follow this delivery, even though rename returned");
        order.Should().Equal("queued", "queued", "retired");
        logger.ReceivedCalls().Should().Contain(call => call.GetMethodInfo().Name == "Log"
            && call.GetArguments().Contains(failure));
        client.PublishServerDiagnostics(new PublishDiagnosticsParams("file:///after.bas", []));
        order.Should().HaveCount(4, "the drainer must also accept later work");
    }
}
