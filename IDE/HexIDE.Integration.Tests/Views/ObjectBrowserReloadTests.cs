using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;
using HexIDE.Tools.ObjectBrowser;

namespace HexIDE.Integration.Tests.Views;

public class ObjectBrowserReloadTests
{
    private static ProjectDefinition Project(bool reference = true)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1") { AbsolutePath = "/projects/Project1.vbp" };
        project.AddModule(new ModuleDefinition(project, "Module1", ModuleKind.StandardModule));
        if (reference) project.AddReference(new VbReference("{00000000-0000-0000-0000-000000000000}", "1.0", 0, null, "Reference1"));
        return project;
    }

    private static (ProjectManager Projects, ObjectBrowserToolViewModel Vm, Window Window, ComboBox Box) Create(ProjectDefinition project)
    {
        var editor = Substitute.For<IEditorService>();
        var projects = new ProjectManager(editor, Substitute.For<IEventBus>());
        projects.AddProject(project);
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Tool.ObjectBrowser.AllLibraries").Returns("<All Libraries>");
        var vm = new ObjectBrowserToolViewModel(projects, Substitute.For<ILspClient>(), editor,
            Substitute.For<IComponentRegistry>(), Substitute.For<ITypeLibraryService>(),
            Substitute.For<IFocusedProjectUtil>(), localization, Substitute.For<ILanguageConnectionRegistry>());
        var view = new ObjectBrowserToolView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        var box = view.GetVisualDescendants().OfType<ComboBox>().Single();
        return (projects, vm, window, box);
    }

    private static void Consistent(ObjectBrowserToolViewModel vm, ComboBox box, string name)
    {
        vm.SelectedLibrary.Should().NotBeNull();
        vm.SelectedLibrary!.Name.Should().Be(name);
        box.SelectedItem.Should().BeSameAs(vm.SelectedLibrary);
        var source = name == "<All Libraries>"
            ? vm.Libraries.Skip(1).SelectMany(l => l.Classes).Where(c => !c.IsStatus)
            : vm.SelectedLibrary.Classes;
        vm.FilteredClasses.Should().BeEquivalentTo(source);
        vm.SelectedClass.Should().BeNull();
        vm.SelectedMember.Should().BeNull();
        vm.FilteredMembers.Should().BeEmpty();
        vm.BackCommand.CanExecute(null).Should().BeFalse();
        vm.ForwardCommand.CanExecute(null).Should().BeFalse();
    }

    [AvaloniaFact]
    public void RemovingSelectedReferenceSelectsAllLibraries()
    {
        var project = Project();
        var (_, vm, window, box) = Create(project);
        try
        {
            box.SelectedItem = vm.Libraries.Single(l => l.Name == project.Name);
            vm.SelectedClass = vm.FilteredClasses.Single();
            box.SelectedItem = vm.Libraries.Single(l => l.Reference != null);
            var type = vm.FilteredClasses.Single();
            type.Members.Add(new OBMemberViewModel("Value", OBMemberKind.Property, "Property Value"));
            vm.SelectedClass = type;
            vm.SelectedMember = vm.FilteredMembers.Single();
            vm.BackCommand.CanExecute(null).Should().BeTrue();
            project.RemoveReference(project.References.Single());
            Consistent(vm, box, "<All Libraries>");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task ReopenRestoresOnlyLibrariesStillPresent(bool reference, bool retained)
    {
        var project = Project();
        var (projects, vm, window, box) = Create(project);
        try
        {
            box.SelectedItem = vm.Libraries.Single(l => reference ? l.Reference != null : l.Name == project.Name);
            vm.SelectedClass = vm.FilteredClasses.Single();
            var directory = Path.Join(Path.GetTempPath(), "hexide-browser-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var path = Path.Join(directory, "Project1.vbp");
                project.AbsolutePath = path;
                File.WriteAllText(path, "Type=Exe\r\nName=\"Project1\"\r\nModule=Module1; Module1.bas\r\n"
                    + (retained ? "Reference=*\\G{00000000-0000-0000-0000-000000000000}#1.0#0##Reference1\r\n" : ""));
                File.WriteAllText(Path.Join(directory, "Module1.bas"), "Attribute VB_Name = \"Module1\"\r\n");
                var windows = Substitute.For<IWindowManager>();
                windows.OpenFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerOpenOptions>()).Returns([path]);
                windows.ShowDialog(Arg.Any<SaveChangesViewModel>()).Returns(call =>
                {
                    call.Arg<SaveChangesViewModel>().No();
                    return true;
                });
                var service = new ProjectService(() => throw new InvalidOperationException(),
                    windows, new EventBus(), projects, Substitute.For<IRecentProjectsService>(),
                    Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
                    Substitute.For<ILocalizationService>(), Substitute.For<IHeaderRefresher>(),                     new ComponentRegistry());
                await service.OpenProject();
            }
            finally { Directory.Delete(directory, true); }
            Consistent(vm, box, reference ? retained ? "Reference1" : "<All Libraries>" : "Project1");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReloadMemoryEndsOnCompletionOrAnotherSelection(bool chooseAnother)
    {
        var project = Project();
        var (projects, vm, window, box) = Create(project);
        try
        {
            box.SelectedItem = vm.Libraries.Single(l => l.Reference != null);
            using (projects.BeginReload())
            {
                projects.UnloadProject(project);
                project = Project();
                projects.AddProject(project);
            }
            vm.SelectedLibrary.Should().NotBeNull();
            vm.SelectedLibrary!.Name.Should().Be("Reference1");
            using (projects.BeginReload())
            {
                projects.UnloadProject(project);
                if (chooseAnother) box.SelectedItem = vm.Libraries.Single(l => l.Name == "VB");
                projects.AddProject(Project(false));
                if (chooseAnother) projects.LoadedProjects.Single().AddReference(project.References.Single());
            }
            if (!chooseAnother) projects.LoadedProjects.Single().AddReference(project.References.Single());
            vm.SelectedLibrary!.Name.Should().Be(chooseAnother ? "VB" : "<All Libraries>");
            box.SelectedItem.Should().BeSameAs(vm.SelectedLibrary);
        }
        finally { window.Close(); }
    }
}
