using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using AvaloniaEdit;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;
using HexIDE.Tools;

namespace HexIDE.Integration.Tests.Controls;

public sealed class ProjectFileNodeTests
{
    [AvaloniaTheory]
    [InlineData("Example.vbp")]
    [InlineData("Example.rdproj")]
    public void Chosen_project_file_is_the_first_child(string file)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Example")
        {
            AbsolutePath = Path.Join(Path.GetTempPath(), file),
            ContributedBy = file.EndsWith(".rdproj", StringComparison.Ordinal) ? "test.project-type" : null,
        };
        project.AddModule(new ModuleDefinition(project, "Module1", ModuleKind.StandardModule));
        var explorer = Explorer();
        using var node = new ProjectViewModel(explorer, project);
        node.Elements.Select(n => n.AccessibleName).Should().StartWith(file);
    }

    [AvaloniaFact]
    public void Group_file_precedes_its_projects()
    {
        var group = new ProjectGroupDefinition("Group") { AbsolutePath = Path.Join(Path.GetTempPath(), "Group.vbg") };
        using var project = new ProjectViewModel(Explorer(), new ProjectDefinition(VBProjectType.EXE, "Example"));
        using var node = new ProjectGroupViewModel(group, new ObservableCollection<ProjectViewModel> { project }, "Project Group - {0}");
        node.Children.Cast<IProjectTreeElement>().Select(n => n.AccessibleName).Should().StartWith("Group.vbg");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void No_chosen_home_has_no_file_node(bool workingCopy)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Example")
        {
            AbsolutePath = Path.Join(Path.GetTempPath(), "Example.vbp"),
        };
        using var node = new ProjectViewModel(Explorer(), project);
        node.Elements.Should().ContainSingle();
        project.AbsolutePath = null;
        if (workingCopy)
        {
            project.WorkingDirectory = Path.Join(Path.GetTempPath(), "hexide-working");
            project.AbsolutePath = Path.Join(project.WorkingDirectory, "Example.vbp");
        }
        node.Elements.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void Activating_the_file_uses_the_file_editor()
    {
        var editors = Substitute.For<IEditorService>();
        var explorer = new ProjectToolViewModel(Substitute.For<IProjectManager>(), Substitute.For<IEventBus>(),
            Substitute.For<IProjectService>(), editors, Substitute.For<ILocalizationService>());
        var project = new ProjectDefinition(VBProjectType.EXE, "Example") { AbsolutePath = "/Example.vbp" };
        using var node = new ProjectViewModel(explorer, project);
        explorer.SelectedItem = node.Elements.First();
        explorer.OpenSelected();
        editors.Received().EditProjectFile(Arg.Is<ProjectFileSource>(f => f.Owner == project));
        editors.DidNotReceiveWithAnyArgs().EditProject(default);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_project_refreshes_the_read_only_file_tab(bool groupFile)
    {
        var directory = Path.Join(Path.GetTempPath(), "hexide-project-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Join(directory, "Example.vbp");
            File.WriteAllText(path, "Type=Exe\r\nName=\"Example\"\r\n");
            var events = new EventBus();
            var editors = Substitute.For<IEditorService>();
            var manager = new ProjectManager(editors, events);
            var service = new ProjectService(new Vb6ReadOnlyRegionWriter(), () => throw new InvalidOperationException(),
                Substitute.For<IWindowManager>(), events, manager, Substitute.For<IRecentProjectsService>(),
                Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
                Substitute.For<ILocalizationService>(), Substitute.For<IHeaderRefresher>(), new Vb6ProjectFileFormat(), new ComponentRegistry());
            if (groupFile)
            {
                var groupPath = Path.Join(directory, "Group.vbg");
                File.WriteAllText(groupPath, "VBGROUP 5.00\r\nProject=Example.vbp\r\nStartupProject=Example.vbp\r\n");
                await service.OpenProject(groupPath);
            }
            else await service.OpenProject(path);
            var project = manager.LoadedProjects.Single();
            var source = groupFile ? new ProjectFileSource(manager.CurrentGroup!) : new ProjectFileSource(project);
            using var tab = new ProjectFileDocumentViewModel(source, events);
            var view = new ProjectFileDocumentView { DataContext = tab };
            var window = new Window { Content = view };
            window.Show();
            try
            {
                view.FindControl<TextEditor>("Editor")!.IsReadOnly.Should().BeTrue();
                var before = tab.Document.Text;
                project.Description = "Changed settings";
                if (groupFile) manager.CurrentGroup!.UnknownLines.Add("Note=Changed settings");
                await service.SaveAllProjects(false);
                tab.Document.Text.Should().NotBe(before).And.Contain("Changed settings");
                tab.Document.Text.ReplaceLineEndings().Should().Be(File.ReadAllText(source.Path!).ReplaceLineEndings());
                tab.CanHoldUnsavedChanges.Should().BeFalse();
                ((ISearchableDocument)tab).IsReadOnlyDocument.Should().BeTrue();
                Action edit = () => tab.Document.Insert(0, "oops");
                edit.Should().Throw<InvalidOperationException>();
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static ProjectToolViewModel Explorer() => new(Substitute.For<IProjectManager>(),
        Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), Substitute.For<IEditorService>(),
        Substitute.For<ILocalizationService>());
}
