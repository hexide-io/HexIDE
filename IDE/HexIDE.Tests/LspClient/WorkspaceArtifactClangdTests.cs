using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Lsp.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace HexIDE.Tests.LspClient;

public sealed class WorkspaceArtifactClangdTests
{
    [ForeignServerFact("cpp")]
    public async Task ClangdReadsGeneratedCompilationArguments()
    {
        var root = Path.Combine(Path.GetTempPath(), "hexide-clangd-artifact-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string source = "#ifndef HEXIDE_PROBE\n#error HEXIDE_PROBE missing\n#endif\nint main(void) { return 0; }\n";
            var file = Path.Combine(root, "probe.c");
            File.WriteAllText(file, source);
            var workspace = new WorkspaceArtifactGenerationTests.Workspace(root)
            {
                Projects = [new(root, new("probe", null,
                    [new("probe.c", file, WorkspaceProjectFileKind.Carried)], []))]
            };
            var without = await Diagnostics(false);
            without.Diagnostics.Should().Contain(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("HEXIDE_PROBE"));
            var with = await Diagnostics(true);
            with.Diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

            async Task<PublishDiagnosticsParams> Diagnostics(bool generate)
            {
                var consent = Substitute.For<IWorkspaceArtifactConsent>();
                consent.AllowAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<WorkspaceArtifactSpec>()).Returns(true);
                using var service = new WorkspaceArtifactService(workspace,
                    new WorkspaceArtifactProviderRegistry([new ClangdCompilationDatabaseProvider()]), consent,
                    NullLogger<WorkspaceArtifactService>.Instance);
                using var settings = JsonDocument.Parse("""{"arguments":["clang","-DHEXIDE_PROBE"]}""");
                var registration = new LanguageServerRegistration("clangd", "clangd", [".c"], "c", () =>
                    new VBLspClient(new StdioProcessLspTransport(
                        new LspServerInfo(ForeignServer.Cpp.Find()!, ForeignServer.Cpp.ServerArguments, root),
                        NullLogger<StdioProcessLspTransport>.Instance, workspace),
                        NullLogger<VBLspClient>.Instance, "c", workspace),
                    WorkspaceArtifact: generate ? new("compile_commands.json", "clangd", settings.RootElement) : null);
                await using var registry = new LspClientRegistry([registration], NullLogger<LspClientRegistry>.Instance,
                    workspace, artifacts: service);
                var received = new TaskCompletionSource<PublishDiagnosticsParams>(TaskCreationOptions.RunContinuationsAsynchronously);
                registry.DiagnosticsPublished += (_, p) => received.TrySetResult(p);
                await registry.OpenDocumentAsync(new Uri(file).AbsoluteUri, source, TestContext.Current.CancellationToken);
                return await received.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
