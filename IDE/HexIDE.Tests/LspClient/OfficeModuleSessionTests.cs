using System.Text.Json;
using HexIDE.Conversations;
using HexIDE.Forms.ViewModels;
using HexIDE.Language;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Office;
using HexIDE.Projects;
using HexIDE.Tests.Projects;
using HexIDE.TwinBasic;
using Microsoft.Extensions.Logging;

namespace HexIDE.Tests.LspClient;

public class OfficeModuleSessionTests
{
    [AvaloniaTheory]
    [InlineData("basic.xlsm", "Standard", "Module1", ".bas")]
    [InlineData("basic.xlsm", "Class", "Class1", ".cls")]
    [InlineData("basic.xlsm", "Document", "ThisWorkbook", ".cls")]
    [InlineData("userform.xlsm", "UserForm", "UserForm1", ".frm")]
    public void OfficeWireNamesUseExportExtensions(string file, string kind, string name, string extension)
    {
        var project = OfficeBrowserProject.Read(OfficeProjectBrowsingTests.CorpusFile(file));
        var entry = project.Root.Children.Single(f => f.Name == kind).Children.Single(e => e.Name == name);
        using var tab = new ArchiveDocumentViewModel(entry, Substitute.For<ILocalizationService>(), Substitute.For<ILanguageEditing>());
        tab.DocumentUri.Should().Be(ArchiveEntryUri.ForEntry(project.ContainerPath, [kind, name + extension]));
        entry.Identity.Segments.Should().Equal(kind, name);
        project.SupportsLanguageServers.Should().BeTrue();
    }

    [AvaloniaFact]
    public void TwinBasicWireNameIsUnchanged()
    {
        var path = Path.Combine(Path.GetTempPath(), "Demo.twinproj");
        var entry = new ContainerDocument(new ArchiveEntryIdentity(path, ["Sources", "Module1.twin"]), "Module1.twin");
        using var tab = new ArchiveDocumentViewModel(entry, Substitute.For<ILocalizationService>(), Substitute.For<ILanguageEditing>());
        tab.DocumentUri.Should().Be(ArchiveEntryUri.ForEntry(path, ["Sources", "Module1.twin"]));
    }

    [AvaloniaFact]
    public async Task OfficeTabsReachTheBundledServerAndCloseWithoutChanges()
    {
        BundledServer.Find().Should().NotBeNull("build the bundled server before this wire test");
        using var logs = LoggerFactory.Create(_ => { });
        await using var capture = new ConversationLog();
        capture.Arm("office", true);
        var transport = new StdioProcessLspTransport(new LspServerInfo(BundledServer.Find()!, "", Path.GetTempPath()),
            logs.CreateLogger<StdioProcessLspTransport>());
        var client = new VBLspClient(transport, logs.CreateLogger<VBLspClient>(), "vb6", capture: capture, connectionId: "office");
        await using var registry = new LspClientRegistry([
            new LanguageServerRegistration("office", "VB", [".bas", ".cls", ".frm"], "vb6", () => client)
        ], logs.CreateLogger<LspClientRegistry>());
        var path = OfficeProjectBrowsingTests.CorpusFile("userform.xlsm");
        using var located = VbaProjectLocator.Locate(path);
        var sourceProject = VbaProjectReader.Read(located.Project!);
        var project = new OfficeBrowserProject(path, sourceProject with
        {
            Modules = sourceProject.Modules.Select(m => m.Kind == VbaModuleKind.Standard ? m with
            {
                Source = "Attribute VB_Name = \"Module1\"\r\nPrivate Declare PtrSafe Function GetTickCount64 Lib \"kernel32\" () As LongLong\r\n"
            } : m).ToArray()
        });
        foreach (var entry in project.Root.Children.Where(f => f.Name != "References").SelectMany(f => f.Children))
        {
            var extension = entry.Identity.Segments[0] switch { "Standard" => ".bas", "UserForm" => ".frm", _ => ".cls" };
            var expected = ArchiveEntryUri.ForEntry(project.ContainerPath, [entry.Identity.Segments[0], entry.Name + extension]);
            var published = new TaskCompletionSource<PublishDiagnosticsParams>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnDiagnostics(object? sender, PublishDiagnosticsParams p)
            {
                if (p.Uri == expected) published.TrySetResult(p);
            }
            registry.DiagnosticsPublished += OnDiagnostics;
            try
            {
                using (var tab = new ArchiveDocumentViewModel(entry, Substitute.For<ILocalizationService>(),
                           Substitute.For<ILanguageEditing>(), lspClient: registry))
                {
                    (await published.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken))
                        .Diagnostics.Should().BeEmpty();
                    FluentActions.Invoking(() => tab.Document.Insert(0, "changed")).Should().Throw<InvalidOperationException>();
                }
                await WaitForClose(capture, expected);
                var opened = Frames(capture, "textDocument/didOpen").Single(d => d.GetProperty("uri").GetString() == expected);
                opened.GetProperty("languageId").GetString().Should().Be("vb6");
                opened.GetProperty("text").GetString().Should().Be(entry.Source);
            }
            finally { registry.DiagnosticsPublished -= OnDiagnostics; }
        }
        Frames(capture, "textDocument/didChange").Should().BeEmpty();
    }

    private static JsonElement[] Frames(ConversationLog capture, string method) => capture.Snapshot("office")
        .Where(e => e.Method == method && e.Direction == ConversationDirection.Sent)
        .Select(e =>
        {
            using var frame = JsonDocument.Parse(capture.Body("office", e.Sequence)!.Head);
            return frame.RootElement.GetProperty("params").GetProperty("textDocument").Clone();
        }).ToArray();

    private static async Task WaitForClose(ConversationLog capture, string uri)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        do
        {
            await capture.DrainAsync();
            if (Frames(capture, "textDocument/didClose").Count(d => d.GetProperty("uri").GetString() == uri) == 1) return;
            await Task.Delay(20, timeout.Token);
        } while (true);
    }
}
