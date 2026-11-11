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
    [InlineData("Form1.frm", MessageBoxResult.Yes)]
    [InlineData("Form1.frm", MessageBoxResult.No)]
    [InlineData("Form1.frm", MessageBoxResult.Cancel)]
    [InlineData("Form1.frx", MessageBoxResult.Yes)]
    [InlineData("Form1.frx", MessageBoxResult.No)]
    [InlineData("Form1.frx", MessageBoxResult.Cancel)]
    [InlineData("Module1.bas", MessageBoxResult.Yes)]
    [InlineData("Module1.bas", MessageBoxResult.No)]
    [InlineData("Module1.bas", MessageBoxResult.Cancel)]
    [InlineData("Project1.vbp", MessageBoxResult.Yes)]
    [InlineData("Project1.vbp", MessageBoxResult.No)]
    [InlineData("Project1.vbp", MessageBoxResult.Cancel)]
    [InlineData("UserControl1.ctx", MessageBoxResult.Yes)]
    [InlineData("UserControl1.ctx", MessageBoxResult.No)]
    [InlineData("UserControl1.ctx", MessageBoxResult.Cancel)]
    [InlineData("PropertyPage1.pgx", MessageBoxResult.Yes)]
    [InlineData("PropertyPage1.pgx", MessageBoxResult.No)]
    [InlineData("PropertyPage1.pgx", MessageBoxResult.Cancel)]
    [InlineData("all", MessageBoxResult.Yes)]
    [InlineData("all", MessageBoxResult.No)]
    [InlineData("all", MessageBoxResult.Cancel)]
    [InlineData("cleared picture", MessageBoxResult.Yes)]
    [InlineData("cleared picture", MessageBoxResult.No)]
    [InlineData("cleared picture", MessageBoxResult.Cancel)]
    public async Task DirectorySaveAsksBeforeReplacingAnyOutput(string name, MessageBoxResult answer)
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Tool.ProtocolInspector.ReplaceFiles").Returns("Replace these existing files?\n{0}");
        var service = new ProjectService(new HexIDE.Runtime.Serialization.Vb6ReadOnlyRegionWriter(), () => throw new InvalidOperationException(), windows,
            Substitute.For<IEventBus>(), Substitute.For<IProjectManager>(), Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
            localization, Substitute.For<IHeaderRefresher>(), new Vb6ProjectFileFormat(), new ComponentRegistry());
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        var form = await service.AddNewForm(project, "Form1", interactive: false);
        form.Components[0].SetProperty(VBProperties.IconProperty, new byte[] { 1, 2, 3, 4 });
        var module = await service.AddNewModule(project, "Module1", ModuleKind.StandardModule, interactive: false);
        if (name is "UserControl1.ctx" or "PropertyPage1.pgx")
        {
            var designer = name == "UserControl1.ctx"
                ? await service.AddNewUserControl(project, "UserControl1", interactive: false)
                : await service.AddNewPropertyPage(project, "PropertyPage1", interactive: false);
            var image = new ComponentInstance(ImageComponentClass.Instance, "Image1");
            image.SetProperty(VBProperties.PictureProperty, new byte[] { 1, 2, 3, 4 });
            designer.FormPart!.UpdateComponents([.. designer.FormPart.Components, image]);
        }
        var deletesCompanion = name == "cleared picture";
        if (deletesCompanion)
        {
            form.RecordCitedCompanionBlobCount(1);
            form.Components[0].SetProperty(VBProperties.IconProperty, null);
        }
        string[] existing = name == "all" ? ["Form1.frm", "Form1.frx", "Module1.bas", "Project1.vbp"]
            : [deletesCompanion ? "Form1.frx" : name];
        foreach (var file in existing)
            File.WriteAllText(Target(file), "user data");
        windows.MessageBox(Arg.Any<string>(), buttons: MessageBoxButtons.YesNo, icon: MessageBoxIcon.Warning)
            .Returns(answer);

        Func<Task> save = () => service.SaveProjectToDirectory(project, root);
        if (answer == MessageBoxResult.Yes)
        {
            await save();
            foreach (var file in existing)
            {
                if (deletesCompanion)
                    File.Exists(Target(file)).Should().BeFalse();
                else
                    File.ReadAllText(Target(file)).Should().NotBe("user data");
            }
            File.Exists(Target("Form1.frm")).Should().BeTrue();
            File.Exists(Target("Module1.bas")).Should().BeTrue();
            File.Exists(Target("Project1.vbp")).Should().BeTrue();
        }
        else
        {
            await save.Should().ThrowAsync<OperationCanceledException>();
            foreach (var file in existing)
                File.ReadAllText(Target(file)).Should().Be("user data");
            Directory.GetFiles(root).Should().BeEquivalentTo(existing.Select(Target));
            form.AbsolutePath.Should().BeNull();
            module.AbsolutePath.Should().BeNull();
            project.AbsolutePath.Should().BeNull();
        }
        await windows.Received(1).MessageBox("Replace these existing files?\n" + string.Join(Environment.NewLine, existing.Select(Target)),
            buttons: MessageBoxButtons.YesNo, icon: MessageBoxIcon.Warning);
    }

    [Theory]
    [InlineData("Form1.frm")]
    [InlineData("Form1.frx")]
    [InlineData("Module1.bas")]
    [InlineData("Project1.vbp")]
    public async Task ProjectSavePreservesEachTargetOnPartialWrite(string name)
    {
        windows.MessageBox(Arg.Any<string>(), buttons: MessageBoxButtons.YesNo, icon: MessageBoxIcon.Warning)
            .Returns(MessageBoxResult.Yes);
        var service = new ProjectService(new HexIDE.Runtime.Serialization.Vb6ReadOnlyRegionWriter(), () => throw new InvalidOperationException(), windows,
            Substitute.For<IEventBus>(), Substitute.For<IProjectManager>(), Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
            Substitute.For<ILocalizationService>(), Substitute.For<IHeaderRefresher>(), new Vb6ProjectFileFormat(), new ComponentRegistry());
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        var form = await service.AddNewForm(project, "Form1", interactive: false);
        form.Components[0].SetProperty(VBProperties.IconProperty, new byte[] { 1, 2, 3, 4 });
        await service.AddNewModule(project, "Module1", ModuleKind.StandardModule, interactive: false);
        var target = Target(name);
        Fail(target);

        Func<Task> save = () => service.SaveProjectToDirectory(project, root);
        await save.Should().ThrowAsync<IOException>().WithMessage("Disk full");
        Preserved(target);

        AtomicFileWriter.OpenStream.Value = null;
        await service.SaveProjectToDirectory(project, root);
        File.ReadAllText(target).Should().NotBe("old bytes");
    }

    [Theory]
    [InlineData("Form1.frm")]
    [InlineData("Module1.bas")]
    [InlineData("Project1.vbp")]
    public async Task PackageConversionPreservesEachTargetOnPartialWrite(string name)
    {
        var service = new ProjectService(new HexIDE.Runtime.Serialization.Vb6ReadOnlyRegionWriter(), () => throw new InvalidOperationException(), windows,
            Substitute.For<IEventBus>(), Substitute.For<IProjectManager>(), Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
            Substitute.For<ILocalizationService>(), Substitute.For<IHeaderRefresher>(), new Vb6ProjectFileFormat(), new ComponentRegistry());
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        await service.AddNewForm(project, "Form1", interactive: false);
        await service.AddNewModule(project, "Module1", ModuleKind.StandardModule, interactive: false);
        await service.SaveProjectToDirectory(project, root);
        var target = Target(name);
        Fail(target);

        var convert = () => ProjectService.EncodePackageSources(project);
        convert.Should().Throw<IOException>().WithMessage("Disk full");
        Preserved(target);
    }

    [Fact]
    public async Task GroupSavePreservesTargetOnPartialWrite()
    {
        var target = Target("Group1.vbg");
        var group = new ProjectGroupDefinition("Group1") { AbsolutePath = target };
        var manager = Substitute.For<IProjectManager>();
        manager.LoadedProjects.Returns(Array.Empty<ProjectDefinition>());
        manager.CurrentGroup.Returns(group);
        var service = new ProjectService(new HexIDE.Runtime.Serialization.Vb6ReadOnlyRegionWriter(), () => throw new InvalidOperationException(), windows,
            Substitute.For<IEventBus>(), manager, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
            Substitute.For<ILocalizationService>(), Substitute.For<IHeaderRefresher>(), new Vb6ProjectFileFormat(), new ComponentRegistry());
        Fail(target);

        Func<Task> save = () => service.SaveAllProjects(saveAs: false);
        await save.Should().ThrowAsync<IOException>().WithMessage("Disk full");
        Preserved(target);
    }

    [Theory]
    [InlineData("Made.dll")]
    [InlineData("Made.exe")]
    public async Task MakePreservesEachOutputOnPartialWrite(string name)
    {
        var target = Target(name);
        Fail(target);
        windows.MessageBox(Arg.Any<string>(), buttons: MessageBoxButtons.YesNo, icon: MessageBoxIcon.Warning)
            .Returns(MessageBoxResult.Yes);
        var service = new ProjectService(new HexIDE.Runtime.Serialization.Vb6ReadOnlyRegionWriter(), () => throw new InvalidOperationException(), windows,
            Substitute.For<IEventBus>(), Substitute.For<IProjectManager>(), Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
            Substitute.For<ILocalizationService>(), Substitute.For<IHeaderRefresher>(), new Vb6ProjectFileFormat(), new ComponentRegistry());
        service.RuntimeDirectory = root;
        Directory.CreateDirectory(Target("standalone"));
        File.WriteAllText(Path.Combine(Target("standalone"), ProjectService.StandaloneRunnerName()!), "runner");
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(Target("Made.exe"));
        Func<Task> make = () => service.MakeProject(new ProjectDefinition(VBProjectType.EXE, "Project1"));
        await make.Should().ThrowAsync<IOException>();
        Preserved(target);
        await windows.ReceivedWithAnyArgs().MessageBox(default!);
    }

    [Theory]
    [InlineData(MessageBoxResult.Yes)]
    [InlineData(MessageBoxResult.No)]
    [InlineData(MessageBoxResult.Cancel)]
    public async Task MakeAsksBeforeReplacingDerivedArchive(MessageBoxResult answer)
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Tool.ProtocolInspector.ReplaceFiles").Returns("Replace these existing files?\n{0}");
        var service = new ProjectService(new HexIDE.Runtime.Serialization.Vb6ReadOnlyRegionWriter(), () => throw new InvalidOperationException(), windows,
            Substitute.For<IEventBus>(), Substitute.For<IProjectManager>(), Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
            localization, Substitute.For<IHeaderRefresher>(), new Vb6ProjectFileFormat(), new ComponentRegistry());
        service.RuntimeDirectory = root;
        Directory.CreateDirectory(Target("standalone"));
        File.WriteAllText(Path.Combine(Target("standalone"), ProjectService.StandaloneRunnerName()!), "runner");
        File.WriteAllText(Target("Made.dll"), "user archive");
        File.WriteAllText(Target("Made.exe"), "user executable");
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(Target("Made.exe"));
        windows.MessageBox(Arg.Any<string>(), buttons: MessageBoxButtons.YesNo, icon: MessageBoxIcon.Warning)
            .Returns(answer);

        Func<Task> make = () => service.MakeProject(new ProjectDefinition(VBProjectType.EXE, "Project1"));
        if (answer == MessageBoxResult.Yes)
        {
            await make();
            using var archive = System.IO.Compression.ZipFile.OpenRead(Target("Made.dll"));
            archive.Entries.Should().Contain(e => e.Name == "Made.vbp");
            File.ReadAllText(Target("Made.exe")).Should().Be("runner");
        }
        else
        {
            await make.Should().ThrowAsync<OperationCanceledException>();
            File.ReadAllText(Target("Made.dll")).Should().Be("user archive");
            File.ReadAllText(Target("Made.exe")).Should().Be("user executable");
        }
        await windows.Received(1).MessageBox("Replace these existing files?\n" + Target("Made.dll"),
            buttons: MessageBoxButtons.YesNo, icon: MessageBoxIcon.Warning);
    }

    [Fact]
    public async Task MakeWritesAReadableArchiveAndPreservesRunnerPermissions()
    {
        var service = new ProjectService(new HexIDE.Runtime.Serialization.Vb6ReadOnlyRegionWriter(), () => throw new InvalidOperationException(), windows,
            Substitute.For<IEventBus>(), Substitute.For<IProjectManager>(), Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
            Substitute.For<ILocalizationService>(), Substitute.For<IHeaderRefresher>(), new Vb6ProjectFileFormat(), new ComponentRegistry());
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
        windows.MessageBox(default!).ReturnsForAnyArgs(MessageBoxResult.Yes);
        // The framed one-byte payload accepted by the resource exporter.
        byte[] record = [9, 0, 0, 0, 0x6c, 0x74, 0, 0, 1, 0, 0, 0, 42];
        var owner = new PropertiesToolViewModel(Substitute.For<IDocumentDockService>(), Substitute.For<IEventBus>(),
            windows, Substitute.For<ILocalizationService>(), new Vb6ProjectFileFormat());
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
            null!, null!, capture, new Pseudonymiser(), null!, null!, new Vb6ProjectFileFormat());
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
