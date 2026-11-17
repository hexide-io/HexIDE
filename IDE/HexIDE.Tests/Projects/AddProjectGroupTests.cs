using System.IO;
using Avalonia.Headless.XUnit;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;
using HexIDE.Tools;

namespace HexIDE.Tests.Projects;

public sealed class AddProjectGroupTests : IDisposable
{
    private readonly string directory = Path.Join(Path.GetTempPath(), "hexide-add-group-" + Guid.NewGuid().ToString("N"));
    private readonly ProjectManager manager;
    private readonly ILspClient lsp = Substitute.For<ILspClient>();
    private readonly ProjectService service;
    private readonly ProjectToolViewModel explorer;
    private readonly ContributedProjects types = new();
    private readonly ILocalizationService strings = Substitute.For<ILocalizationService>();
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();

    public AddProjectGroupTests()
    {
        Directory.CreateDirectory(directory);
        var events = Substitute.For<IEventBus>();
        var editors = Substitute.For<IEditorService>();
        manager = new ProjectManager(editors, events, () => lsp);
        strings.GetString("Str.Dialog.ProjectGroup.OtherKindRefused").Returns("{0} refuses {1}");
        strings.GetString("Str.Tool.Project.TitleProject").Returns("Project - {0}");
        strings.GetString("Str.Tool.Project.TitleGroup").Returns("Project Group - {0}");
        // VB6's files are read by VB6's type, which the composition registers first (#3411 slice 10b).
        Vb6ProjectType.Register(types, new Vb6ProjectFileFormat());
        service = new ProjectService(() => throw new InvalidOperationException(),
            windows, events, manager, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
            strings, Substitute.For<IHeaderRefresher>(), new ComponentRegistry(), contributed: types);
        explorer = new ProjectToolViewModel(manager, events, service, editors, strings);
    }

    private string WriteProject(string name)
    {
        var path = Path.Join(directory, name + ".vbp");
        File.WriteAllText(path, $"Type=Exe\r\nName=\"{name}\"\r\n");
        return path;
    }

    [AvaloniaFact]
    public async Task Startup_group_open_keeps_its_name_order_startup_and_saved_path()
    {
        WriteProject("First");
        WriteProject("Second");
        var path = Path.Join(directory, "Launch.vbg");
        var bytes = System.Text.Encoding.ASCII.GetBytes("VBGROUP 5.0\r\nProject=First.vbp\r\nStartupProject=Second.vbp\r\n");
        File.WriteAllBytes(path, bytes);
        var startup = new HexIDE.Desktop.StartupProjectOpen();
        var window = new Avalonia.Controls.Window();
        startup.Attach(window, types, () => Task.CompletedTask);
        try
        {
            await startup.Open(service, path);
            manager.CurrentGroup!.Name.Should().Be("Launch");
            manager.CurrentGroup.AbsolutePath.Should().Be(path);
            manager.LoadedProjects.Select(p => p.Name).Should().Equal("First", "Second");
            manager.StartupProject!.Name.Should().Be("Second");
            explorer.Title.Should().Be("Project Group - Launch");
            await service.SaveAllProjects(saveAs: false);
            File.ReadAllBytes(path).Should().Equal(bytes);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddingAnExistingProject_CreatesTheGroup(bool contributed)
    {
        await service.OpenProject(WriteProject("First"));
        var second = WriteProject("Second");
        using var registration = types.Register(new AddinProjectType("Stand-in projects")
        {
            FileNames = ["*.sproj"],
            OpenAsync = (_, _, _) => Task.FromResult(new AddinProjectContent("Second")),
        }, new AddinOrigin("provider", "A provider"));
        if (contributed)
        {
            second = Path.Join(directory, "Second.sproj");
            File.WriteAllText(second, "stand-in");
        }

        await service.OpenProject(second);

        manager.LoadedProjects.Should().HaveCount(2);
        manager.CurrentGroup.Should().NotBeNull();
        manager.CurrentGroup!.Name.Should().Be("Group1");
        explorer.Title.Should().Be("Project Group - Group1");
        explorer.RootItems.Should().ContainSingle().Which.Should().BeOfType<ProjectGroupViewModel>();
    }

    [Fact]
    public async Task OpeningASingleProject_DoesNotCreateAGroup()
    {
        await service.OpenProject(WriteProject("Only"));

        manager.CurrentGroup.Should().BeNull();
        explorer.Title.Should().Be("Project - Only");
    }

    [Fact]
    public async Task OpeningAGroup_UsesItsOwnNamePathAndStartupProject()
    {
        WriteProject("First");
        WriteProject("Second");
        var path = Path.Join(directory, "OuterGroup.vbg");
        File.WriteAllText(path, "VBGROUP 5.00\r\nProject=First.vbp\r\nProject=Second.vbp\r\nStartupProject=Second.vbp\r\n");

        await service.OpenProject(path);

        manager.LoadedProjects.Should().HaveCount(2);
        manager.CurrentGroup.Should().NotBeNull();
        manager.CurrentGroup!.Name.Should().Be("OuterGroup");
        manager.CurrentGroup.AbsolutePath.Should().Be(path);
        manager.StartupProject!.Name.Should().Be("Second");
        explorer.Title.Should().Be("Project Group - OuterGroup");
    }

    [Theory]
    [InlineData("StartupProject=First.vbp\r\nProject=Second.vbp\r\n", "First")]
    [InlineData("Project=First.vbp\r\nStartupProject=Second.vbp\r\n", "Second")]
    public async Task OpeningVb6Group_LoadsStartupMemberInFileOrder(string members, string startup)
    {
        WriteProject("First");
        WriteProject("Second");
        var path = Path.Join(directory, "Oracle.vbg");
        File.WriteAllText(path, "VBGROUP 5.0\r\n" + members);

        await service.OpenProject(path);

        manager.LoadedProjects.Select(p => p.Name).Should().Equal("First", "Second");
        manager.StartupProject!.Name.Should().Be(startup);
        manager.CurrentGroup!.UnknownLines.Should().BeEmpty();
    }

    // Reload, chosen when a save finds a project's file changed outside HexIDE, keeps the project's place in its group:
    // the group, the order and the startup project, so saving the group writes what it read (#4009).
    [AvaloniaTheory]
    [InlineData("StartupProject=First.vbp\r\nProject=Second.vbp\r\n", "First")]
    [InlineData("StartupProject=First.vbp\r\nProject=Second.vbp\r\n", "Second")]
    [InlineData("Project=First.vbp\r\nStartupProject=Second.vbp\r\nProject=Third.vbp\r\nKept=1\r\n", "Second")]
    public async Task Reload_after_an_outside_change_keeps_the_project_in_its_place_in_the_group(string members, string reloaded)
    {
        WriteProject("First");
        WriteProject("Second");
        WriteProject("Third");
        var path = Path.Join(directory, "G.vbg");
        var groupBytes = System.Text.Encoding.ASCII.GetBytes("VBGROUP 5.0\r\n" + members);
        File.WriteAllBytes(path, groupBytes);
        await service.OpenProject(path);
        var group = manager.CurrentGroup;
        var order = manager.LoadedProjects.Select(p => p.Name).ToList();
        var startup = manager.StartupProject!.Name;
        var project = manager.LoadedProjects.Single(p => p.Name == reloaded);
        await service.SaveProject(project, saveAs: false);
        project.Name = "Ours";
        File.AppendAllText(project.AbsolutePath!, "Title=\"Outside\"\r\n");
        windows.ShowDialog(Arg.Any<FileConflictViewModel>()).Returns(call =>
        {
            var answer = true;
            var conflict = call.Arg<FileConflictViewModel>();
            conflict.CloseRequested += chosen => answer = chosen;
            conflict.ReloadAll();
            return Task.FromResult(answer);
        });

        var save = () => service.SaveProject(project, saveAs: false);
        await save.Should().ThrowAsync<OperationCanceledException>();

        manager.CurrentGroup.Should().BeSameAs(group);
        manager.CurrentGroup!.AbsolutePath.Should().Be(path);
        manager.LoadedProjects.Select(p => p.Name).Should().Equal(order);
        manager.LoadedProjects.Should().NotContain(project);
        manager.StartupProject!.Name.Should().Be(startup);
        explorer.LoadedProjects.Select(p => p.Name).Should().Equal(order);
        explorer.Title.Should().Be("Project Group - G");
        await service.SaveAllProjects(saveAs: false);
        File.ReadAllBytes(path).Should().Equal(groupBytes);
    }

    [AvaloniaFact]
    public async Task Object_browser_selection_survives_reload_in_a_saved_two_project_group()
    {
        WriteProject("First");
        WriteProject("Second");
        var path = Path.Join(directory, "G.vbg");
        var bytes = System.Text.Encoding.ASCII.GetBytes("VBGROUP 5.0\r\nProject=First.vbp\r\nStartupProject=Second.vbp\r\n");
        File.WriteAllBytes(path, bytes);
        await service.OpenProject(path);
        var group = manager.CurrentGroup;
        var project = manager.LoadedProjects[0];
        var browser = new HexIDE.Tools.ObjectBrowser.ObjectBrowserToolViewModel(manager, lsp,
            Substitute.For<IEditorService>(), new ComponentRegistry(), Substitute.For<ITypeLibraryService>(),
            Substitute.For<IFocusedProjectUtil>(), Substitute.For<ILocalizationService>(),
            Substitute.For<ILanguageConnectionRegistry>());
        browser.SelectedLibrary = browser.Libraries.Single(l => l.Name == "First");
        await service.SaveProject(project, saveAs: false);
        project.Description = "Ours";
        File.AppendAllText(project.AbsolutePath!, "Title=\"Outside\"\r\n");
        windows.ShowDialog(Arg.Any<FileConflictViewModel>()).Returns(call =>
        {
            call.Arg<FileConflictViewModel>().ReloadAll();
            return true;
        });

        var save = () => service.SaveProject(project, saveAs: false);
        await save.Should().ThrowAsync<OperationCanceledException>();

        browser.SelectedLibrary.Should().NotBeNull();
        browser.SelectedLibrary!.Name.Should().Be("First");
        browser.Libraries.Should().Contain(browser.SelectedLibrary);
        manager.CurrentGroup.Should().BeSameAs(group);
        manager.LoadedProjects.Select(p => p.Name).Should().Equal("First", "Second");
        manager.StartupProject.Should().BeSameAs(manager.LoadedProjects[1]);
        explorer.Title.Should().Be("Project Group - G");
        await service.SaveAllProjects(saveAs: false);
        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Reload_only_moves_the_workspace_when_reopening_fails(bool group, bool failReopen)
    {
        var path = WriteProject("Startup");
        await service.OpenProject(path);
        if (group)
        {
            var otherDirectory = Path.Join(directory, "other");
            Directory.CreateDirectory(otherDirectory);
            var other = Path.Join(otherDirectory, "Other.vbp");
            File.WriteAllText(other, "Type=Exe\r\nName=\"Other\"\r\n");
            await service.OpenProject(other);
        }
        var project = manager.StartupProject!;
        await service.SaveProject(project, saveAs: false);
        project.Name = "Ours";
        File.AppendAllText(path, "Title=\"Outside\"\r\n");
        windows.ShowDialog(Arg.Any<FileConflictViewModel>()).Returns(call =>
        {
            var conflict = call.Arg<FileConflictViewModel>();
            conflict.ReloadAll();
            if (failReopen) File.Delete(path);
            return Task.FromResult(true);
        });
        var workspace = new ProjectLspWorkspace(() => manager);
        var rootedAt = workspace.Directory;
        var calls = 0;
        var restarts = 0;
        lsp.WorkspaceChangedAsync().Returns(_ =>
        {
            calls++;
            if (workspace.Directory != rootedAt)
            {
                restarts++;
                rootedAt = workspace.Directory;
            }
            return Task.CompletedTask;
        });

        var save = () => service.SaveProject(project, saveAs: false);
        if (failReopen)
            await save.Should().ThrowAsync<IOException>();
        else
            await save.Should().ThrowAsync<OperationCanceledException>();

        restarts.Should().Be(failReopen ? 1 : 0);
        calls.Should().Be(1);
        manager.StartupProject.Should().NotBeSameAs(project);
        manager.StartupProject.Should().BeSameAs(manager.LoadedProjects.First());
    }

    [AvaloniaFact]
    public async Task Standalone_contributed_project_saves_its_module_without_a_group_or_project_writer()
    {
        var code = Path.Join(directory, "Code.bas");
        File.WriteAllText(code, "Attribute VB_Name = \"Code\"\r\nSub Main()\r\nEnd Sub\r\n");
        var path = Path.Join(directory, "Other.rdproj");
        File.WriteAllText(path, "other project");
        using var registration = types.Register(new AddinProjectType("Other projects")
        {
            FileNames = ["*.rdproj"],
            OpenAsync = (_, _, _) => Task.FromResult(new AddinProjectContent("Other")
            {
                Members = [new(code, AddinMemberKind.StandardModule)]
            }),
        }, new AddinOrigin("provider", "A provider"));
        await service.OpenProject(path);
        var project = manager.LoadedProjects.Single();
        project.Modules.Single().UpdateCode("Sub Main()\r\nDebug.Print 4051\r\nEnd Sub\r\n");
        var modified = File.GetLastWriteTimeUtc(path);

        await service.SaveProject(project, saveAs: false);

        File.ReadAllText(code).Should().Contain("Debug.Print 4051");
        File.ReadAllText(path).Should().Be("other project");
        File.GetLastWriteTimeUtc(path).Should().Be(modified);
        Directory.GetFiles(directory, "*.vbg").Should().BeEmpty();
        manager.LoadedProjects.Any(p => p.Offers.MovesProjectFile).Should().BeFalse();
        windows.ReceivedCalls().Should().NotContain(call => call.GetMethodInfo().Name == nameof(IWindowManager.MessageBox));
        await windows.DidNotReceive().SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>(), Arg.Any<string?>());
    }

    // Non-.vbp lines become code modules in VB6, not projects (oracle PR #4308).
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mixed_group_refuses_before_writing_any_file(bool existingGroup)
    {
        var first = WriteProject("First");
        var groupPath = Path.Join(directory, "Outer.vbg");
        if (existingGroup)
        {
            File.WriteAllText(groupPath, "VBGROUP 5.0\r\nStartupProject=First.vbp\r\n");
            await service.OpenProject(groupPath);
        }
        else
            await service.OpenProject(first);
        using var registration = types.Register(new AddinProjectType("Other projects")
        {
            FileNames = ["*.rdproj"],
            OpenAsync = (_, _, _) => Task.FromResult(new AddinProjectContent("Other")),
        }, new AddinOrigin("provider", "A provider"));
        var other = Path.Join(directory, "Other.rdproj");
        File.WriteAllText(other, "other project");
        await service.OpenProject(other);
        var group = manager.CurrentGroup!;
        var order = manager.LoadedProjects.ToArray();
        var startup = manager.StartupProject;
        var before = Directory.GetFiles(directory).ToDictionary(path => path,
            path => (Bytes: File.ReadAllBytes(path), Modified: File.GetLastWriteTimeUtc(path)));
        windows.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>(), Arg.Any<string?>())
            .Returns(Task.FromResult<string?>(groupPath));

        var save = () => service.SaveAllProjects(saveAs: false);
        var refusal = (await save.Should().ThrowAsync<ProjectGroupKindRefusal>()).Which;

        refusal.Group.Should().Be(group.Name);
        refusal.Members.Should().Equal("Other (Other.rdproj)");
        refusal.Message.Should().Contain("VB6 would open").And.Contain("as code modules");
        foreach (var (path, snapshot) in before)
        {
            File.ReadAllBytes(path).Should().Equal(snapshot.Bytes);
            File.GetLastWriteTimeUtc(path).Should().Be(snapshot.Modified);
        }
        File.Exists(groupPath).Should().Be(existingGroup);
        manager.CurrentGroup.Should().BeSameAs(group);
        manager.LoadedProjects.Should().Equal(order);
        manager.StartupProject.Should().BeSameAs(startup);
        group.AbsolutePath.Should().Be(existingGroup ? groupPath : null);
        await windows.Received().MessageBox($"{group.Name} refuses Other (Other.rdproj)",
            Distribution.DisplayName, MessageBoxButtons.Ok, MessageBoxIcon.Warning);
    }

    [AvaloniaFact]
    public async Task Vbp_only_group_saves_byte_identically()
    {
        WriteProject("First");
        WriteProject("Second");
        var path = Path.Join(directory, "Outer.vbg");
        var bytes = System.Text.Encoding.ASCII.GetBytes("VBGROUP 5.0\r\nProject=First.vbp\r\nStartupProject=Second.vbp\r\n");
        File.WriteAllBytes(path, bytes);
        await service.OpenProject(path);

        await service.SaveAllProjects(saveAs: false);

        File.ReadAllBytes(path).Should().Equal(bytes);
        manager.CurrentGroup!.Name.Should().Be("Outer");
        manager.CurrentGroup.AbsolutePath.Should().Be(path);
        manager.LoadedProjects.Select(project => project.Name).Should().Equal("First", "Second");
        manager.StartupProject!.Name.Should().Be("Second");
    }

    [AvaloniaFact]
    public async Task Save_Project_As_checks_the_destination_and_uses_the_pseudo_pack()
    {
        WriteProject("First");
        WriteProject("Second");
        var path = Path.Join(directory, "Outer.vbg");
        File.WriteAllText(path, "VBGROUP 5.0\r\nProject=First.vbp\r\nStartupProject=Second.vbp\r\n");
        await service.OpenProject(path);
        var project = manager.LoadedProjects.First();
        var oldPath = project.AbsolutePath;
        var group = manager.CurrentGroup;
        var before = Directory.GetFiles(directory).ToDictionary(p => p,
            p => (Bytes: File.ReadAllBytes(p), Modified: File.GetLastWriteTimeUtc(p)));
        var destination = Path.Join(directory, "Renamed.rdproj");
        windows.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>(), Arg.Any<string?>())
            .Returns(Task.FromResult<string?>(destination));
        var localization = new LocalizationService();
        localization.Apply("pseudo");
        try
        {
            var template = localization.GetString("Str.Dialog.ProjectGroup.OtherKindRefused");
            template.Should().StartWith("⟦");
            strings.GetString("Str.Dialog.ProjectGroup.OtherKindRefused").Returns(template);

            var save = () => service.SaveProject(project, saveAs: true);
            (await save.Should().ThrowAsync<ProjectGroupKindRefusal>()).Which.Members.Should().Equal("First (Renamed.rdproj)");

            await windows.Received().MessageBox(string.Format(template, "Outer", "First (Renamed.rdproj)"),
                Distribution.DisplayName, MessageBoxButtons.Ok, MessageBoxIcon.Warning);
            File.Exists(destination).Should().BeFalse();
            foreach (var (file, snapshot) in before)
            {
                File.ReadAllBytes(file).Should().Equal(snapshot.Bytes);
                File.GetLastWriteTimeUtc(file).Should().Be(snapshot.Modified);
            }
            project.AbsolutePath.Should().Be(oldPath);
            manager.CurrentGroup.Should().BeSameAs(group);
            manager.LoadedProjects.Select(p => p.Name).Should().Equal("First", "Second");
            manager.StartupProject!.Name.Should().Be("Second");
        }
        finally { localization.Apply("en"); }
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
