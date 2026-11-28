using HexIDE.Runtime.Serialization;
using BrowserFolder = HexIDE.Projects.ContainerFolder;
using Avalonia.Platform.Storage;
using HexIDE.Office;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;
using HexIDE.Tests.Infrastructure;

namespace HexIDE.Tests.Projects;

public sealed class OfficeProjectBrowsingTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hexide-office-browser-" + Guid.NewGuid());
    private readonly IDocumentDockService dock = Substitute.For<IDocumentDockService>();
    private readonly IRecentProjectsService recent = Substitute.For<IRecentProjectsService>();
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();
    private readonly ILocalizationService localization = Substitute.For<ILocalizationService>();
    private readonly ArchiveProjectService archives;
    private readonly List<BaseEditorWindowViewModel> tabs = [];
    public static string CorpusFile(string name) =>
        Path.Combine(RepoTree.Root(), "corpus", "office", "m365-x64", name);

    public OfficeProjectBrowsingTests()
    {
        Directory.CreateDirectory(directory);
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        localization.GetString("Str.Office.NoProject").Returns("{0} has no VBA project.");
        localization.GetString("Str.Archive.OpenErrorFormat").Returns("Cannot open {0}: {1}");
        dock.OpenDocuments.Returns(tabs);
        dock.When(d => d.OpenDocument(Arg.Any<BaseEditorWindowViewModel>())).Do(c => tabs.Add(c.Arg<BaseEditorWindowViewModel>()));
        archives = new(dock, recent, localization, windows, Substitute.For<HexIDE.Language.ILanguageEditing>());
    }

    [Fact]
    public void Corpus_file_lookup_ignores_a_nearer_corpus_directory()
    {
        var decoy = Path.Combine(AppContext.BaseDirectory, "corpus");
        var existed = Directory.Exists(decoy);
        Directory.CreateDirectory(decoy);
        try
        {
            File.Exists(CorpusFile("basic.xlsm")).Should().BeTrue();
        }
        finally
        {
            if (!existed) Directory.Delete(decoy);
        }
    }

    [AvaloniaTheory]
    [InlineData("basic.xlsm", "Document")]
    [InlineData("basic.xlsb", "Document")]
    [InlineData("basic.xls", "Document")]
    [InlineData("basic.docm", "Document")]
    [InlineData("basic.doc", "Document")]
    [InlineData("basic.pptm", "Standard")]
    [InlineData("userform.xlsm", "Document")]
    [InlineData("cp932.xlsm", "Document")]
    public async Task Corpus_opens_folders_in_kind_order_and_releases_file(string file, string firstFolder)
    {
        var path = Path.Combine(directory, file);
        File.Copy(CorpusFile(file), path);
        await archives.Open(path);
        var project = archives.Projects.Should().ContainSingle().Which;
        project.Name.Should().Be(file == "userform.xlsm" ? "FormProject" : file == "cp932.xlsm" ? "CodePageProject" : "CorpusProject");
        var folders = project.Root.Children.Cast<BrowserFolder>().ToArray();
        folders.First().Name.Should().Be(firstFolder);
        folders.Last().Name.Should().Be("References");
        var kinds = folders.Select(f => f.Name).ToArray();
        kinds.Should().Equal(new[] { "Document", "UserForm", "Standard", "Class", "References" }.Where(kinds.Contains));
        if (file == "basic.pptm") kinds.Should().NotContain("Document");
        if (file == "userform.xlsm")
        {
            archives.OpenDocument(folders.Single(f => f.Name == "UserForm").Children.Single());
            var tab = tabs.OfType<ArchiveDocumentViewModel>().Single();
            tab.Document.Text.Should().Contain("CommandButton1_Click");
            tab.Notice.Should().Be("Str.Office.FormNotice");
        }
        if (file == "cp932.xlsm")
        {
            var module = folders.Single(f => f.Name == "Standard").Children.Single();
            module.Name.Should().Be("日本語モジュール");
            archives.OpenDocument(module);
            tabs.OfType<ArchiveDocumentViewModel>().Single().Document.Text.Should().Contain("こんにちは");
        }
        folders.Last().Children.Should().OnlyContain(r => !string.IsNullOrEmpty(r.Name));
        // Windows refuses this share mode if the locator still owns the document; Linux runs the same check too.
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        File.Delete(path);
        File.Exists(path).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Unnamed_reference_uses_libid_and_unknown_host_uses_generic_objects(string? name)
    {
        using var located = VbaProjectLocator.Locate(CorpusFile("basic.xlsm"));
        var project = VbaProjectReader.Read(located.Project!);
        var adapted = new OfficeBrowserProject(Path.Combine(directory, "unknown.office"), project with
        {
            References = [new VbaReference(name, VbaReferenceKind.Registered, "reference-id")]
        });
        adapted.Root.Children.First().CaptionKey.Should().Be("Str.Office.DocumentObjects");
        adapted.Root.Children.Last().Children.Single().Name.Should().Be("reference-id");
    }

    [Fact]
    public async Task Locked_project_shows_no_modules_and_reports_the_lock()
    {
        await archives.Open(CorpusFile("locked.xlsm"));
        var project = archives.Projects.Single();
        project.Root.Children.Should().OnlyContain(f => f.Name == "References");
        await windows.Received().MessageBox("Str.Office.LockedMessage", null, MessageBoxButtons.Ok, MessageBoxIcon.Information, null);
        tabs.Should().BeEmpty();
    }

    public static IEnumerable<object[]> Extensions => new[] { "xlsm", "xltm", "xlam", "xlsb", "docm", "dotm", "pptm", "potm", "ppsm", "ppam", "xls", "xlt", "xla", "doc", "dot" }.SelectMany(s => new[] { new object[] { s, false }, new object[] { s, true } });

    [AvaloniaTheory]
    [MemberData(nameof(Extensions))]
    public async Task Open_project_routes_every_offered_extension_and_keeps_the_workspace(string extension, bool picker)
    {
        var path = Path.Combine(directory, "project." + extension);
        File.Copy(CorpusFile("basic.xlsm"), path);
        var manager = new ProjectManager(Substitute.For<IEditorService>(), Substitute.For<IEventBus>());
        foreach (var name in new[] { "First", "Second" })
            File.WriteAllText(Path.Combine(directory, name + ".vbp"), $"Type=Exe\r\nName=\"{name}\"\r\n");
        var groupPath = Path.Combine(directory, "Kept.vbg");
        var groupBytes = System.Text.Encoding.ASCII.GetBytes("VBGROUP 5.0\r\nProject=First.vbp\r\nStartupProject=Second.vbp\r\n");
        File.WriteAllBytes(groupPath, groupBytes);
        var service = Service(manager);
        await service.OpenProject(groupPath);
        var first = manager.LoadedProjects[0];
        var second = manager.LoadedProjects[1];
        var group = manager.CurrentGroup!;
        service.HasUnsavedChanges(first).Should().BeFalse();
        service.HasUnsavedChanges(second).Should().BeFalse();
        var groupChanges = 0;
        group.PropertyChanged += (_, _) => groupChanges++;
        windows.OpenFilePickerAsync(Arg.Any<FilePickerOpenOptions>()).Returns(new[] { path });
        if (picker) await service.OpenProject();
        else await service.OpenProject(path);
        archives.Projects.Should().ContainSingle().Which.ContainerPath.Should().Be(path);
        await archives.Reload(path);
        await archives.Close(path);
        manager.LoadedProjects.Should().Equal(first, second);
        manager.StartupProject.Should().BeSameAs(second);
        manager.CurrentGroup.Should().BeSameAs(group);
        group.Name.Should().Be("Kept");
        group.AbsolutePath.Should().Be(groupPath);
        groupChanges.Should().Be(0);
        service.HasUnsavedChanges(first).Should().BeFalse();
        service.HasUnsavedChanges(second).Should().BeFalse();
        File.ReadAllBytes(groupPath).Should().Equal(groupBytes);
        recent.Received(1).Add(path);
        new ContributedProjects().Reserved.Should().Contain(r => r.Covers(path));
        new ContributedProjects().Reserved.Should().NotContain(r => r.Covers("legacy.ppt"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("locator")]
    [InlineData("reader")]
    public async Task Missing_or_unreadable_project_reports_reason_without_a_recent_entry(string failure)
    {
        var path = Path.Combine(directory, "empty.xlsm");
        var noProject = failure == "missing";
        string? readerReason = null;
        if (noProject)
        {
            using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
            using var writer = new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open());
            writer.Write("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
        }
        else if (failure == "reader")
        {
            File.Copy(Path.Combine(HexIDE.Tests.Office.CompoundFileTests.Corpus, "xls-shaped.cfb"), path);
            using var located = VbaProjectLocator.Locate(path);
            located.Status.Should().Be(VbaProjectStatus.Found);
            readerReason = FluentActions.Invoking(() => VbaProjectReader.Read(located.Project!))
                .Should().Throw<OfficeFileException>().Which.Message;
        }
        else File.WriteAllText(path, "not an Office file");
        await Service(Substitute.For<IProjectManager>()).OpenProject(path);
        archives.Projects.Should().BeEmpty();
        recent.DidNotReceive().Add(Arg.Any<string>());
        await windows.Received().MessageBox(Arg.Is<string>(s => s.Contains("empty.xlsm") &&
            s.Contains(noProject ? "has no VBA project" : readerReason ?? "neither a compound file nor an Open XML")),
            null, MessageBoxButtons.Ok, noProject ? MessageBoxIcon.Information : MessageBoxIcon.Error, null);
    }

    private ProjectService Service(IProjectManager manager)
    {
        var types = new ContributedProjects();
        var format = new Vb6ProjectFileFormat();
        Vb6Types.Register(types, format);
        return new ProjectService(() => throw new InvalidOperationException(), windows, Substitute.For<IEventBus>(), manager,
            recent, Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
            localization, Substitute.For<IHeaderRefresher>(), InBoxRegistry.Create(), archives, contributed: types);
    }

    public void Dispose()
    {
        foreach (var tab in tabs) tab.Dispose();
        Directory.Delete(directory, recursive: true);
    }
}
