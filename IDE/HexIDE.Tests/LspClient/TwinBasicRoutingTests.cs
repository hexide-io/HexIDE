using System.Text;
using System.Text.Json;
using AvaloniaEdit.Document;
using HexIDE.Conversations;
using HexIDE.Forms.ViewModels;
using HexIDE.Language;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.TwinBasic;
using Microsoft.Extensions.Logging;

namespace HexIDE.Tests.LspClient;

public class TwinBasicRoutingTests
{
    private static ILspClient Server()
    {
        var client = Substitute.For<ILspClient>();
        client.IsRunning.Returns(true);
        client.AdvertisedCapabilities.Returns(JsonDocument.Parse(
            """{"textDocumentSync":{"openClose":true,"change":1,"save":true},"hoverProvider":true}""").RootElement.Clone());
        return client;
    }

    [Theory]
    [InlineData("twinbasic", ".bas", false)]
    [InlineData("TwInBaSiC", ".bas", false)]
    [InlineData("twinbasic", ".cls", false)]
    [InlineData("twinbasic", ".cls", true)]
    public async Task LabelRoutesOpenRequestsChangesSaveAndClose(string label, string extension, bool member)
    {
        var vb = Server();
        var tb = Server();
        var wrongExtension = Server();
        await using var registry = new LspClientRegistry([
            new("vb", "VB6", [".bas", ".cls"], "vb6", () => vb),
            new("tb", "tB", [".bas", ".cls"], "twinbasic", () => tb),
            new("other", "Other", [".twin"], "twinbasic", () => wrongExtension)
        ], Substitute.For<ILogger<LspClientRegistry>>());
        var uri = "archive-entry:/Module1" + extension + "?container=file%3A%2F%2F%2Fproject.twinproj";
        await registry.OpenDocumentAsync(uri, "Function F()\nReturn 1\nEnd Function", member,
            TestContext.Current.CancellationToken, languageId: label);
        await registry.RequestHoverAsync(uri, new Position(0, 0), TestContext.Current.CancellationToken);
        await registry.ChangeDocumentAsync(uri, 2, "changed", TestContext.Current.CancellationToken);
        await registry.SaveDocumentAsync(uri, TestContext.Current.CancellationToken);
        await registry.CloseDocumentAsync(uri, TestContext.Current.CancellationToken);

        await tb.Received(1).OpenDocumentAsync(uri, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await tb.Received(1).RequestHoverAsync(uri, Arg.Any<Position>(), Arg.Any<CancellationToken>());
        await tb.Received(1).ChangeDocumentAsync(uri, 2, "changed", Arg.Any<CancellationToken>());
        await tb.Received(1).SaveDocumentAsync(uri, Arg.Any<CancellationToken>());
        await tb.Received(1).CloseDocumentAsync(uri, Arg.Any<CancellationToken>());
        vb.ReceivedCalls().Should().BeEmpty();
        wrongExtension.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task LabelWithoutAClaimingServerRoutesNowhere()
    {
        var vb = Server();
        await using var registry = new LspClientRegistry([
            new("vb", "VB6", [".bas", ".cls"], "vb6", () => vb)
        ], Substitute.For<ILogger<LspClientRegistry>>());
        const string uri = "archive-entry:/Module1.bas?container=file%3A%2F%2F%2Fp.twinproj";
        await registry.OpenDocumentAsync(uri, "code", false, TestContext.Current.CancellationToken, languageId: "twinbasic");
        await registry.RequestHoverAsync(uri, new Position(0, 0), TestContext.Current.CancellationToken);
        await registry.CloseDocumentAsync(uri, TestContext.Current.CancellationToken);
        vb.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task WorkspaceReplayRetainsTheLabel()
    {
        var workspace = Substitute.For<ILspWorkspace>();
        workspace.Directory.Returns("/projects/a");
        workspace.Folders.Returns(Array.Empty<LspWorkspaceFolder>());
        var vb = Server();
        var created = new List<ILspClient>();
        await using var registry = new LspClientRegistry([
            new("vb", "VB6", [".bas"], "vb6", () => vb),
            new("tb", "tB", [".bas", ".twin"], "twinbasic", () =>
            {
                var client = Server();
                created.Add(client);
                return client;
            })
        ], Substitute.For<ILogger<LspClientRegistry>>(), workspace);
        const string uri = "archive-entry:/Module1.bas?container=file%3A%2F%2F%2Fp.twinproj";
        await registry.OpenDocumentAsync(uri, "code", false, TestContext.Current.CancellationToken, languageId: "twinbasic");
        workspace.Directory.Returns("/projects/b");
        await registry.OpenDocumentAsync("file:///projects/b/Other.twin", "other", false,
            TestContext.Current.CancellationToken, languageId: "twinbasic");
        created.Should().HaveCount(2);
        await created[1].Received(1).OpenDocumentAsync(uri, "code", Arg.Any<CancellationToken>());
        vb.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("Module1.bas", false, true)]
    [InlineData("Class1.cls", true, false)]
    public async Task UnlabelledDocumentsKeepTheirRoutes(string name, bool member, bool reachesOther)
    {
        var vb = Server();
        var other = Server();
        await using var registry = new LspClientRegistry([
            new("vb", "VB6", [".bas", ".cls"], "vb6", () => vb),
            new("other", "Other", member ? [".cls"] : [".bas", ".cls"], "twinbasic", () => other)
        ], Substitute.For<ILogger<LspClientRegistry>>());
        var uri = "archive-entry:/" + name + "?container=file%3A%2F%2F%2Fp";
        await registry.OpenDocumentAsync(uri, "Option Explicit", member, TestContext.Current.CancellationToken);
        await vb.Received(1).OpenDocumentAsync(uri, "Option Explicit", Arg.Any<CancellationToken>());
        if (reachesOther) await other.Received(1).OpenDocumentAsync(uri, "Option Explicit", Arg.Any<CancellationToken>());
        else other.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("Module.TWIN", "twinbasic")]
    [InlineData("Module.BAS", "twinbasic")]
    [InlineData("Class.CLS", "twinbasic")]
    [InlineData("Form.frm", "twinbasic")]
    [InlineData("Control.ctl", "twinbasic")]
    [InlineData("Page.pag", "twinbasic")]
    [InlineData("Object.dob", "twinbasic")]
    [InlineData("Designer.dsr", "twinbasic")]
    [InlineData("CHANGELOG.md", null)]
    [InlineData("#1.xml", null)]
    [InlineData("Form.tbform", null)]
    public void ProjectLabelsOnlySourceExtensions(string name, string? expected)
    {
        using var input = new MemoryStream();
        using (var writer = new BinaryWriter(input, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0xEA0BA51Cu);
            writer.Write((short)1);
            var bytes = Encoding.UTF8.GetBytes("Project");
            writer.Write((uint)bytes.Length);
            writer.Write(bytes);
            writer.Write(1ul);
            writer.Write(0u);
            writer.Write((byte)0);
            writer.Write(0u);
        }
        input.Position = 0;
        var path = Path.Combine(Path.GetTempPath(), "project.twinproj");
        var project = new TwinBasicBrowserProject(ArchiveProjectSnapshot.Read(input, path, cancellationToken: TestContext.Current.CancellationToken));
        var entry = new ContainerDocument(new ArchiveEntryIdentity(path, [name]), name);
        project.LanguageOf(entry).Should().Be(expected);
    }

    [AvaloniaFact]
    public async Task RenamedProjectHolderKeepsTheSessionLabel()
    {
        var client = Server();
        const string uri = "untitled:Archive/Module1.bas";
        const string renamed = "untitled:Archive/Renamed.bas";
        using var session = new LspDocumentSession(client, new TextDocument("code"), uri, languageId: "twinbasic");
        session.Start();
        using var holder = session.CreateProjectHolder();
        session.Dispose();
        await holder.RenameAsync(renamed, TestContext.Current.CancellationToken);
        await client.Received(1).OpenDocumentAsync(uri, "code", false, languageId: "twinbasic");
        await client.Received(1).OpenDocumentAsync(renamed, "code", false,
            TestContext.Current.CancellationToken, languageId: "twinbasic");
    }

    [AvaloniaFact]
    public async Task PublishedTwinPackMarkdownKeepsExtensionRouting()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.Parent is not null && !Directory.Exists(Path.Combine(root.FullName, "openspec"))) root = root.Parent;
        var path = Path.Combine(root.FullName, "IDE", "HexIDE.TwinBasic.Tests", "Fixtures", "sample.twinpack");
        using var input = File.OpenRead(path);
        var project = new TwinBasicBrowserProject(ArchiveProjectSnapshot.Read(input, path, cancellationToken: TestContext.Current.CancellationToken));
        IEnumerable<ContainerEntry> Entries(ContainerEntry entry) => new[] { entry }.Concat(entry.Children.SelectMany(Entries));
        var entry = Entries(project.Root).Single(e => e.Name.Equals("CHANGELOG.md", StringComparison.OrdinalIgnoreCase));
        project.LanguageOf(entry).Should().BeNull();
        var markdown = Server();
        await using var registry = new LspClientRegistry([
            new("md", "Markdown", [".md"], "markdown", () => markdown)
        ], Substitute.For<ILogger<LspClientRegistry>>());
        using var tab = new ArchiveDocumentViewModel(entry, Substitute.For<ILocalizationService>(),
            Substitute.For<IConditionalCodeReader>(), lspClient: registry, languageId: project.LanguageOf(entry));
        await markdown.Received(1).OpenDocumentAsync(tab.DocumentUri, tab.Document.Text, Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task ArchiveTabsSendTwinBasicFramesOnlyAndRetainLabelsOnReopen()
    {
        var path = Path.Combine(Path.GetTempPath(), "hexide-1122-" + Guid.NewGuid().ToString("N") + ".twinproj");
        Write(path);
        using var logs = LoggerFactory.Create(_ => { });
        await using var capture = new ConversationLog();
        capture.Arm("vb6", true);
        capture.Arm("tb", true);
        VBLspClient Client(string id, string language) => new(
            new StdioProcessLspTransport(new LspServerInfo(BundledServer.Find()!, "", Path.GetTempPath()),
                logs.CreateLogger<StdioProcessLspTransport>()), logs.CreateLogger<VBLspClient>(), language,
            capture: capture, connectionId: id);
        BundledServer.Find().Should().NotBeNull();
        await using var registry = new LspClientRegistry([
            new("vb6", "VB6", [".bas", ".cls", ".frm"], "vb6", () => Client("vb6", "vb6")),
            new("tb", "tB", [".bas", ".cls", ".twin"], "twinbasic", () => Client("tb", "twinbasic"))
        ], logs.CreateLogger<LspClientRegistry>());
        try
        {
            using var input = File.OpenRead(path);
            var project = new TwinBasicBrowserProject(ArchiveProjectSnapshot.Read(input, path, cancellationToken: TestContext.Current.CancellationToken));
            foreach (var entry in project.Root.Children)
            {
                using (var tab = new ArchiveDocumentViewModel(entry, Substitute.For<ILocalizationService>(),
                           Substitute.For<IConditionalCodeReader>(), lspClient: registry, languageId: project.LanguageOf(entry)))
                {
                    await WaitForFrames(capture, "tb", "textDocument/didOpen", entry.DocumentUri, 1);
                    Frames(capture, "vb6", "textDocument/didOpen").Should().BeEmpty();
                    var frame = Frames(capture, "tb", "textDocument/didOpen").Single(d => d.GetProperty("uri").GetString() == entry.DocumentUri);
                    frame.GetProperty("languageId").GetString().Should().Be("twinbasic");
                    frame.GetProperty("text").GetString().Should().Be(entry.ReadText()!.Text);
                    tab.Dispose();
                    await WaitForFrames(capture, "tb", "textDocument/didClose", entry.DocumentUri, 1);
                    using var reopened = tab.Reopen();
                    await WaitForFrames(capture, "tb", "textDocument/didOpen", entry.DocumentUri, 2);
                    Frames(capture, "vb6", "textDocument/didOpen").Should().BeEmpty();
                }
                await WaitForFrames(capture, "tb", "textDocument/didClose", entry.DocumentUri, 2);
            }
            Frames(capture, "tb", "textDocument/didChange").Should().BeEmpty();
            Frames(capture, "vb6", "textDocument/didOpen").Should().BeEmpty();
        }
        finally { File.Delete(path); }
    }

    private static JsonElement[] Frames(ConversationLog capture, string id, string method) => capture.Snapshot(id)
        .Where(e => e.Method == method && e.Direction == ConversationDirection.Sent)
        .Select(e =>
        {
            using var frame = JsonDocument.Parse(capture.Body(id, e.Sequence)!.Head);
            return frame.RootElement.GetProperty("params").GetProperty("textDocument").Clone();
        }).ToArray();

    private static async Task WaitForFrames(ConversationLog capture, string id, string method, string uri, int count)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        do
        {
            await capture.DrainAsync();
            if (Frames(capture, id, method).Count(d => d.GetProperty("uri").GetString() == uri) == count) return;
            await Task.Delay(20, timeout.Token);
        } while (true);
    }

    private static void Write(string path)
    {
        using var output = File.Create(path);
        using var writer = new BinaryWriter(output, Encoding.UTF8);
        writer.Write(0xEA0BA51Cu);
        Header("Project", 0);
        writer.Write(2u);
        foreach (var (name, text) in new[] { ("Module1.bas", "Function F()\nReturn 1\nEnd Function"), ("Class1.cls", "VERSION 1.0 CLASS\nOption Explicit") })
        {
            // Synthetic category 4 forces the existing text-preview path; no claim about imported modules.
            Header(name, 4);
            var bytes = Encoding.UTF8.GetBytes(text);
            writer.Write((uint)bytes.Length);
            writer.Write(bytes);
            writer.Write(0u);
        }
        void Header(string name, byte category)
        {
            writer.Write((short)1);
            var bytes = Encoding.UTF8.GetBytes(name);
            writer.Write((uint)bytes.Length);
            writer.Write(bytes);
            writer.Write(1ul);
            writer.Write(0u);
            writer.Write(category);
        }
    }
}
