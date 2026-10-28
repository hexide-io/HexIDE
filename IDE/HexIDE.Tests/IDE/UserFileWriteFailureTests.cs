using System.IO;
using Avalonia.Platform.Storage;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Components;
using HexIDE.Sidecar;
using HexIDE.Tools.ProtocolInspector;
using HexIDE.Conversations;
using HexIDE.Redaction;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.IDE;

public sealed class UserFileWriteFailureTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("hexide-write-failure-").FullName;
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();
    private bool injected;

    private string Target(string name) => Path.Combine(root, name);

    private void Fail(string target)
    {
        File.WriteAllText(target, "old bytes");
        AtomicFileWriter.OpenStream.Value = path =>
        {
            if (!path.StartsWith(target + ".", StringComparison.Ordinal))
                return new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            injected = true;
            return new PartialFailureStream(path);
        };
    }

    private void Preserved(string target)
    {
        injected.Should().BeTrue("the caller must reach the shared writer");
        File.ReadAllBytes(target).Should().Equal("old bytes"u8.ToArray());
        Directory.GetFiles(root, Path.GetFileName(target) + ".*.tmp").Should().BeEmpty();
    }

    public void Dispose()
    {
        AtomicFileWriter.OpenStream.Value = null;
        Directory.Delete(root, true);
    }

    private sealed class PartialFailureStream(string path) : FileStream(path, FileMode.CreateNew, FileAccess.Write)
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, Math.Min(1, count));
            throw new IOException("Disk full");
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            base.Write(buffer[..Math.Min(1, buffer.Length)]);
            throw new IOException("Disk full");
        }
    }

    [Theory]
    [InlineData("Made.dll")]
    [InlineData("Made.exe")]
    public async Task MakePreservesEachOutputOnPartialWrite(string name)
    {
        var target = Target(name);
        Fail(target);
        var service = new ProjectService(() => throw new InvalidOperationException(), windows,
            Substitute.For<IEventBus>(), Substitute.For<IProjectManager>(), Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
            Substitute.For<ILocalizationService>(), Substitute.For<IHeaderRefresher>());
        service.RuntimeDirectory = root;
        Directory.CreateDirectory(Target("standalone"));
        File.WriteAllText(Path.Combine(Target("standalone"), ProjectService.StandaloneRunnerName()!), "runner");
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(Target("Made.exe"));
        Func<Task> make = () => service.MakeProject(new ProjectDefinition(VBProjectType.EXE, "Project1"));
        await make.Should().ThrowAsync<IOException>();
        Preserved(target);
        await windows.ReceivedWithAnyArgs().MessageBox(default!);
    }

    [Fact]
    public async Task MakeWritesAReadableArchiveAndPreservesRunnerPermissions()
    {
        var service = new ProjectService(() => throw new InvalidOperationException(), windows,
            Substitute.For<IEventBus>(), Substitute.For<IProjectManager>(), Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
            Substitute.For<ILocalizationService>(), Substitute.For<IHeaderRefresher>());
        service.RuntimeDirectory = root;
        Directory.CreateDirectory(Target("standalone"));
        var runner = Path.Combine(Target("standalone"), ProjectService.StandaloneRunnerName()!);
        File.WriteAllText(runner, "runner");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(runner, UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupExecute);
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(Target("Made.exe"));
        await service.MakeProject(new ProjectDefinition(VBProjectType.EXE, "Project1"));
        File.ReadAllText(Target("Made.exe")).Should().Be("runner");
        if (!OperatingSystem.IsWindows())
            File.GetUnixFileMode(Target("Made.exe")).Should().Be(File.GetUnixFileMode(runner));
        using var archive = System.IO.Compression.ZipFile.OpenRead(Target("Made.dll"));
        archive.Entries.Should().Contain(e => e.Name == "Made.vbp");
    }

    [Fact]
    public async Task ResourceExportPreservesTargetOnPartialWrite()
    {
        var target = Target("picture.ico");
        Fail(target);
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(target);
        // The framed one-byte payload accepted by the resource exporter.
        byte[] record = [9, 0, 0, 0, 0x6c, 0x74, 0, 0, 1, 0, 0, 0, 42];
        var owner = new PropertiesToolViewModel(Substitute.For<IDocumentDockService>(), Substitute.For<IEventBus>(),
            windows, Substitute.For<ILocalizationService>());
        var row = new PropertyViewModel(owner, VBProperties.PictureProperty, record);
        await row.ExportPictureCommand.ExecuteAsync(null);
        Preserved(target);
        await windows.ReceivedWithAnyArgs().MessageBox(default!);
    }

    [Theory]
    [InlineData("conversation.jsonl")]
    [InlineData("conversation.manifest.json")]
    public async Task ProtocolExportPreservesEachTargetOnPartialWrite(string name)
    {
        var target = Target(name);
        Fail(target);
        await using var capture = new ConversationLog();
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(true);
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(Target("conversation.jsonl"));
        // The export asks before replacing an existing file (#2504); this test agrees, to reach the writer.
        windows.MessageBox(default!).ReturnsForAnyArgs(MessageBoxResult.Yes);
        var vm = new ProtocolInspectorToolViewModel(capture, Substitute.For<ILocalizationService>(), new Pseudonymiser(), windows);
        await vm.ExportAsync();
        Preserved(target);
        vm.ExportStatus.Should().NotBeEmpty();
    }

    // Each automation export has a directory of its own (#2504), so a failed write leaves an earlier export as it was,
    // and no staging file behind.
    [Theory]
    [InlineData("hexide_lsp_conversation.jsonl")]
    [InlineData("hexide_lsp_conversation.manifest.json")]
    public async Task AutomationExportFailsCleanlyAndLeavesEarlierExports(string name)
    {
        var earlier = Target(name);
        File.WriteAllText(earlier, "old bytes");
        AtomicFileWriter.OpenStream.Value = path =>
        {
            if (!Path.GetFileName(path).StartsWith(name + ".", StringComparison.Ordinal))
                return new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            injected = true;
            return new PartialFailureStream(path);
        };
        await using var capture = new ConversationLog();
        using var context = new HexIDE.Desktop.Server.IdeContext(Substitute.For<IProjectManager>(),
            Substitute.For<IDocumentDockService>(), Substitute.For<HexIDE.Lsp.ILspClient>(), null!,
            Substitute.For<IProjectRunnerService>(), null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, capture, new Pseudonymiser(), null!, null!);
        var tools = new HexIDE.Desktop.Server.HexIdeTools(context) { ConversationExportDirectory = root };
        Func<Task> export = () => tools.ExportLspConversationAsync(ct: TestContext.Current.CancellationToken);
        await export.Should().ThrowAsync<IOException>();
        injected.Should().BeTrue("the export must reach the shared writer");
        File.ReadAllBytes(earlier).Should().Equal("old bytes"u8.ToArray());
        Directory.GetFiles(root, name + ".*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task SidecarPreservesTargetAndRetriesOnPartialWrite()
    {
        var project = TestHelpers.CreateProjectWithForm("P", "Form1");
        project.AbsolutePath = Target("P.vbp");
        project.Forms[0].UpdateCode("' line 1\n' line 2");
        var target = Target("P.user.hexproj");
        File.WriteAllText(target, "{\"version\":4}");
        var bookmarks = new BookmarkService();
        var service = new UserSidecarService(bookmarks, new BreakpointService(), Substitute.For<IProjectManager>());
        await service.LoadAsync(project);
        bookmarks.SetBookmarks(DocumentIdentity.For(project.Forms[0]), [1]);
        var original = File.ReadAllBytes(target);
        Fail(target);
        File.WriteAllBytes(target, original);
        await service.SaveAsync(project);
        injected.Should().BeTrue();
        File.ReadAllBytes(target).Should().Equal(original);
        Directory.GetFiles(root, "*.tmp").Should().BeEmpty();
        AtomicFileWriter.OpenStream.Value = null;
        await service.SaveAsync(project);
        File.ReadAllText(target).Should().Contain("Form1");
    }
}
