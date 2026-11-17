using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Office;
using HexIDE.Tests.Office;
using Microsoft.Extensions.Logging;

namespace HexIDE.Tests.LspClient;

public class OfficeModuleCorpusTests
{
    [BundledServerFact]
    public async Task EveryOfficeCorpusModulePublishesNoDiagnostics()
    {
        using var logs = LoggerFactory.Create(_ => { });
        var transport = new StdioProcessLspTransport(
            new LspServerInfo(BundledServer.Find()!, "", Path.GetTempPath()),
            logs.CreateLogger<StdioProcessLspTransport>());
        await using var client = new VBLspClient(transport, logs.CreateLogger<VBLspClient>(), "vb6");
        await client.StartAsync(TestContext.Current.CancellationToken);
        var failures = new List<string>();
        foreach (var file in new[] { "basic.xlsm", "basic.xlsb", "basic.docm", "basic.pptm", "basic.xls", "basic.doc", "userform.xlsm", "cp932.xlsm" })
        {
            var path = Path.GetFullPath(Path.Combine(CompoundFileTests.Corpus, "..", "m365-x64", file));
            using var located = VbaProjectLocator.Locate(path);
            var project = VbaProjectReader.Read(located.Project!);
            project.Modules.Should().NotBeEmpty(file);
            foreach (var module in project.Modules)
            {
                var extension = module.Kind switch { VbaModuleKind.Standard => ".bas", VbaModuleKind.UserForm => ".frm", _ => ".cls" };
                var uri = ArchiveEntryUri.ForEntry(path, [module.Kind.ToString(), module.Name + extension]);
                var published = new TaskCompletionSource<PublishDiagnosticsParams>(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnDiagnostics(object? sender, PublishDiagnosticsParams p)
                {
                    if (p.Uri == uri) published.TrySetResult(p);
                }
                client.DiagnosticsPublished += OnDiagnostics;
                try
                {
                    await client.OpenDocumentAsync(uri, module.Source!, TestContext.Current.CancellationToken);
                    var result = await published.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
                    failures.AddRange(result.Diagnostics.Select(d => $"{file}, {module.Name}, line {d.Range.Start.Line + 1}: {d.Message}"));
                    await client.CloseDocumentAsync(uri, TestContext.Current.CancellationToken);
                }
                finally { client.DiagnosticsPublished -= OnDiagnostics; }
            }
        }
        await client.StopAsync();
        failures.Should().BeEmpty();
    }
}
