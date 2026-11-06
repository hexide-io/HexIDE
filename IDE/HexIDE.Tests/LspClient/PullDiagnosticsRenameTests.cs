using HexIDE.Lsp.Messages;

namespace HexIDE.Tests.LspClient;

public partial class PullDiagnosticsReportKindTests
{
    [Fact]
    public async Task RenameSuppressesOrdinaryPullClosePublication()
    {
        var sut = ClientTalkingTo(new PullingServer());
        await sut.StartAsync(TestContext.Current.CancellationToken);
        var received = new List<PublishDiagnosticsParams>();
        sut.DiagnosticsPublished += (_, p) => received.Add(p);

        await sut.CloseDocumentForRenameAsync(Uri, TestContext.Current.CancellationToken);

        received.Should().ContainSingle("only the ordered rename clear may publish for the retired URI");
    }
}
