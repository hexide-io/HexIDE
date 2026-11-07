using System.IO;
using HexIDE.Conversations;
using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Redaction;

namespace HexIDE.Tests.Infrastructure;

public class ConversationExportSafetyTests
{
    [Fact]
    public async Task LaterExportsLeaveEarlierFilesIntact()
    {
        await using var capture = new ConversationLog();
        using var context = new IdeContext(Substitute.For<IProjectManager>(), Substitute.For<IDocumentDockService>(),
            Substitute.For<ILspClient>(), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, capture, new Pseudonymiser(), null!, null!);
        var tools = new HexIdeTools(context);
        LspExportResult? first = null;
        LspExportResult? second = null;
        try
        {
            first = await tools.ExportLspConversationAsync(ct: TestContext.Current.CancellationToken);
            var originalMessages = File.ReadAllBytes(first.MessagesPath);
            var originalManifest = File.ReadAllBytes(first.ManifestPath);
            capture.Record("probe", ConversationDirection.Sent, ConversationEntryKind.Notification, "changed", null, 0);
            second = await tools.ExportLspConversationAsync(ct: TestContext.Current.CancellationToken);

            second.MessagesPath.Should().NotBe(first.MessagesPath);
            second.ManifestPath.Should().NotBe(first.ManifestPath);
            File.ReadAllBytes(first.MessagesPath).Should().Equal(originalMessages);
            File.ReadAllBytes(first.ManifestPath).Should().Equal(originalManifest);
            File.Exists(second.MessagesPath).Should().BeTrue();
            File.ReadAllText(second.ManifestPath).Should().Contain("changed");
        }
        finally
        {
            foreach (var result in new[] { first, second }.OfType<LspExportResult>())
            {
                File.Delete(result.MessagesPath);
                File.Delete(result.ManifestPath);
                // The new export owns a directory; the old implementation wrote in the temp root.
                var directory = Path.GetDirectoryName(result.MessagesPath)!;
                if (Path.GetFileName(directory).StartsWith("hexide-lsp-export-", StringComparison.Ordinal))
                    Directory.Delete(directory);
            }
        }
    }
}
