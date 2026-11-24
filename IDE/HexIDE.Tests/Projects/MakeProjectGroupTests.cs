using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

// Dialog and stop behaviour measured in #5190; see docs/vb6-fidelity-oracle/make-project-group.md.
public class MakeProjectGroupTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("hexide-group-").FullName;
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();
    private readonly IEventBus events = Substitute.For<IEventBus>();
    private readonly IProjectManager manager = Substitute.For<IProjectManager>();
    private readonly ProjectDefinition[] projects;
    private readonly ProjectService service;

    public MakeProjectGroupTests()
    {
        projects = Enumerable.Range(1, 2).Select(i =>
        {
            var folder = Directory.CreateDirectory(Path.Join(root, "P" + i)).FullName;
            var project = new ProjectDefinition(VBProjectType.EXE, "P" + i) { AbsolutePath = Path.Join(folder, "P" + i + ".vbp") };
            File.WriteAllText(project.AbsolutePath, "original project");
            project.UnknownPreSectionLines.Add((0, $"ExeName32=\"Made{i}.exe\""));
            var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule)
                { AbsolutePath = Path.Join(folder, "Module1.bas") };
            module.UpdateCode("Sub Main()\nEnd Sub");
            project.AddModule(module);
            File.WriteAllText(module.AbsolutePath, "original module");
            return project;
        }).ToArray();
        projects[1].UnknownPreSectionLines.Add((1, "Path32=\"out\""));
        Directory.CreateDirectory(Path.Join(root, "P2", "out"));
        manager.LoadedProjects.Returns(projects);
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns("message {0}");
        service = new ProjectService(() => throw new InvalidOperationException(), windows, events, manager,
            Substitute.For<IRecentProjectsService>(), Substitute.For<IReferenceLibraryService>(),
            Substitute.For<IUserSidecarService>(), new FileBaselineStore(), localization,
            Substitute.For<IHeaderRefresher>(), InBoxRegistry.Create());
        service.RuntimeDirectory = root;
        var standalone = Directory.CreateDirectory(Path.Join(root, "standalone")).FullName;
        File.WriteAllText(Path.Join(standalone, ProjectService.StandaloneRunnerName()!), "runner");
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(true);
    }

    [Fact]
    public async Task Default_options_build_both_at_the_shared_paths_without_a_picker_or_saving_sources()
    {
        events.When(e => e.Publish(Arg.Any<ApplyAllUnsavedChangesEvent>())).Do(_ =>
            projects[0].Modules[0].UpdateCode("Sub Main()\n' unsaved editor text\nEnd Sub"));

        await service.MakeProjectGroup();

        ProjectOutputPath.OutputExePath(projects[0]).Should().Be(Path.Join(root, "P1", "Made1.exe"));
        ProjectOutputPath.OutputExePath(projects[1]).Should().Be(Path.Join(root, "P2", "out", "Made2.exe"));
        foreach (var project in projects)
        {
            var output = ProjectOutputPath.OutputExePath(project);
            File.ReadAllText(output).Should().Be("runner");
            using var archive = ZipFile.OpenRead(Path.ChangeExtension(output, "dll"));
            archive.Entries.Should().Contain(e => e.Name.EndsWith(".vbp", StringComparison.Ordinal));
            var moduleEntry = archive.Entries.Single(e => e.Name == "Module1.bas");
            using var reader = new StreamReader(moduleEntry.Open());
            if (project == projects[0])
                reader.ReadToEnd().Should().Contain("unsaved editor text");
            File.ReadAllText(project.AbsolutePath!).Should().Be("original project");
            File.ReadAllText(project.Modules[0].AbsolutePath!).Should().Be("original module");
        }
        events.Received().Publish(Arg.Any<ApplyAllUnsavedChangesEvent>());
        await windows.DidNotReceiveWithAnyArgs().SaveFilePickerAsync(default!);
    }

    [Fact]
    public async Task Nondefault_options_ask_for_each_project_in_order_in_its_folder()
    {
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(call =>
        {
            ((MakeProjectGroupViewModel)call.Arg<IDialog>()).UseDefaultBuildOptions = false;
            return true;
        });
        var asked = 0;
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string>()).Returns(call =>
        {
            var project = projects[asked++];
            call.ArgAt<string>(1).Should().Be(Path.GetDirectoryName(project.AbsolutePath));
            call.ArgAt<FilePickerSaveOptions>(0).SuggestedFileName.Should().StartWith(project.Name);
            return Path.Join(root, project.Name + ".exe");
        });

        await service.MakeProjectGroup();

        asked.Should().Be(2);
        File.Exists(Path.Join(root, "P1.exe")).Should().BeTrue();
        File.Exists(Path.Join(root, "P2.exe")).Should().BeTrue();
    }

    [Fact]
    public async Task Cancel_on_the_first_picker_stops_without_writing_any_output()
    {
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(call =>
        {
            ((MakeProjectGroupViewModel)call.Arg<IDialog>()).UseDefaultBuildOptions = false;
            return true;
        });
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string>()).Returns((string?)null);

        await service.MakeProjectGroup();

        await windows.Received(1).SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Path.GetDirectoryName(projects[0].AbsolutePath));
        events.Received(1).Publish(Arg.Any<ApplyAllUnsavedChangesEvent>());
        Directory.GetFiles(root, "*.dll", SearchOption.AllDirectories).Should().BeEmpty();
        File.Exists(ProjectOutputPath.OutputExePath(projects[1])).Should().BeFalse();
    }

    [Fact]
    public async Task Deselecting_a_project_leaves_it_out()
    {
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(call =>
        {
            var dialog = (MakeProjectGroupViewModel)call.Arg<IDialog>();
            dialog.SelectedProjects.Remove(dialog.Projects[0]);
            return true;
        });

        await service.MakeProjectGroup();

        File.Exists(ProjectOutputPath.OutputExePath(projects[0])).Should().BeFalse();
        File.Exists(ProjectOutputPath.OutputExePath(projects[1])).Should().BeTrue();
    }

    [Fact]
    public async Task Failure_stops_the_group_and_is_reported_once()
    {
        events.When(e => e.Publish(Arg.Any<ApplyAllUnsavedChangesEvent>())).Do(_ => throw new IOException("failed"));

        await service.MakeProjectGroup();

        events.Received(1).Publish(Arg.Any<ApplyAllUnsavedChangesEvent>());
        await windows.Received(1).MessageBox("message failed", icon: MessageBoxIcon.Error);
        File.Exists(ProjectOutputPath.OutputExePath(projects[1])).Should().BeFalse();
    }

    [Fact]
    public async Task Unsaved_project_uses_the_picker_even_with_default_options()
    {
        projects[0].AbsolutePath = null;
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), null).Returns(Path.Join(root, "Unsaved.exe"));

        await service.MakeProjectGroup();

        await windows.Received(1).SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), null);
        File.Exists(Path.Join(root, "Unsaved.exe")).Should().BeTrue();
        projects[0].AbsolutePath.Should().BeNull();
        File.Exists(ProjectOutputPath.OutputExePath(projects[1])).Should().BeTrue();
    }

    [Fact]
    public async Task Default_options_keep_the_existing_archive_question_and_stop_if_declined()
    {
        var archive = Path.ChangeExtension(ProjectOutputPath.OutputExePath(projects[0]), "dll");
        File.WriteAllText(archive, "existing archive");
        windows.MessageBox(Arg.Any<string>(), buttons: MessageBoxButtons.YesNo, icon: MessageBoxIcon.Warning)
            .Returns(MessageBoxResult.No);

        await service.MakeProjectGroup();

        await windows.Received(1).MessageBox(Arg.Any<string>(), buttons: MessageBoxButtons.YesNo, icon: MessageBoxIcon.Warning);
        File.ReadAllText(archive).Should().Be("existing archive");
        File.Exists(ProjectOutputPath.OutputExePath(projects[1])).Should().BeFalse();
    }

    public void Dispose() => Directory.Delete(root, true);
}
