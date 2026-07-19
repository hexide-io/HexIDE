using System.Collections.Generic;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Tools;

namespace HexIDE.Tests.ViewModels;

public class ProjectToolViewModelTests
{
    private readonly IProjectManager projectManager;
    private readonly IEventBus eventBus;
    private readonly IProjectService projectService;
    private readonly IEditorService editorService;

    public ProjectToolViewModelTests()
    {
        projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(new List<ProjectDefinition>());
        eventBus = Substitute.For<IEventBus>();
        projectService = Substitute.For<IProjectService>();
        editorService = Substitute.For<IEditorService>();
    }

    private ProjectToolViewModel CreateVm()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Tool.Project.Title").Returns("Project Group - Group1");
        return new(projectManager, eventBus, projectService, editorService, localization);
    }

    private ProjectToolViewModel CreateVmWithLoadedProject(
        ProjectDefinition project,
        out ProjectViewModel projectVm)
    {
        var vm = CreateVm();
        projectManager.ProjectLoaded += Raise.Event<Action<ProjectDefinition>>(project);
        projectVm = vm.LoadedProjects[0];
        return vm;
    }

    // TestHelpers projects are unsaved (null AbsolutePath), so every member renders at the
    // project root of the filesystem tree.
    private static FormViewModel FirstForm(ProjectViewModel projVm) =>
        projVm.Elements.OfType<FormViewModel>().First();

    private static ModuleViewModel FirstModule(ProjectViewModel projVm) =>
        projVm.Elements.OfType<ModuleViewModel>().First();

    // ── Construction ──────────────────────────────────────────────

    [Fact]
    public void Constructor_SetsTitle()
    {
        var vm = CreateVm();

        vm.Title.Should().Be("Project Group - Group1");
    }

    [Fact]
    public void Constructor_LoadedProjectsIsEmpty_WhenNoProjectsLoaded()
    {
        var vm = CreateVm();

        vm.LoadedProjects.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_LoadsExistingProjects()
    {
        var proj = TestHelpers.CreateProject("Existing");
        projectManager.LoadedProjects.Returns(new List<ProjectDefinition> { proj });

        var vm = CreateVm();

        vm.LoadedProjects.Should().HaveCount(1);
        vm.LoadedProjects[0].Definition.Should().Be(proj);
    }

    // ── ProjectLoaded / Unloaded events ───────────────────────────

    [Fact]
    public void ProjectLoadedEvent_AddsProjectViewModel()
    {
        var vm = CreateVm();
        var proj = TestHelpers.CreateProject("NewProj");

        projectManager.ProjectLoaded += Raise.Event<Action<ProjectDefinition>>(proj);

        vm.LoadedProjects.Should().HaveCount(1);
        vm.LoadedProjects[0].Definition.Should().Be(proj);
    }

    [Fact]
    public void ProjectUnloadedEvent_RemovesProjectViewModel()
    {
        var proj = TestHelpers.CreateProject("ToRemove");
        var vm = CreateVmWithLoadedProject(proj, out _);

        projectManager.ProjectUnloaded += Raise.Event<Action<ProjectDefinition>>(proj);

        vm.LoadedProjects.Should().BeEmpty();
    }

    // ── Selection cascade ─────────────────────────────────────────

    [Fact]
    public void SelectedItem_ProjectViewModel_SetsSelectedProject()
    {
        var proj = TestHelpers.CreateProject();
        var vm = CreateVmWithLoadedProject(proj, out var projVm);

        vm.SelectedItem = projVm;

        vm.SelectedProject.Should().Be(projVm);
    }

    [Fact]
    public void SelectedItem_FormViewModel_SetsSelectedForm()
    {
        var proj = TestHelpers.CreateProjectWithForm("P", "Form1");
        var vm = CreateVmWithLoadedProject(proj, out var projVm);
        var formVm = FirstForm(projVm);

        vm.SelectedItem = formVm;

        vm.SelectedForm.Should().Be(formVm);
    }

    [Fact]
    public void SelectedItem_ModuleViewModel_SetsSelectedModule()
    {
        var proj = TestHelpers.CreateProjectWithModule("P", "Mod1");
        var vm = CreateVmWithLoadedProject(proj, out var projVm);
        var moduleVm = FirstModule(projVm);

        vm.SelectedItem = moduleVm;

        vm.SelectedModule.Should().Be(moduleVm);
    }

    [Fact]
    public void SelectedItem_Null_ClearsAllSelections()
    {
        var proj = TestHelpers.CreateProject();
        var vm = CreateVmWithLoadedProject(proj, out var projVm);
        vm.SelectedItem = projVm;

        vm.SelectedItem = null;

        vm.SelectedProject.Should().BeNull();
        vm.SelectedForm.Should().BeNull();
        vm.SelectedModule.Should().BeNull();
    }

    [Fact]
    public void SelectedFormOrFormPart_TrueWhenFormSelected()
    {
        var proj = TestHelpers.CreateProjectWithForm("P", "F1");
        var vm = CreateVmWithLoadedProject(proj, out var projVm);
        var formVm = FirstForm(projVm);

        vm.SelectedItem = formVm;

        vm.SelectedFormOrFormPart.Should().BeTrue();
    }

    [Fact]
    public void SelectedFormOrFormPart_FalseWhenProjectSelected()
    {
        var proj = TestHelpers.CreateProject();
        var vm = CreateVmWithLoadedProject(proj, out var projVm);

        vm.SelectedItem = projVm;

        vm.SelectedFormOrFormPart.Should().BeFalse();
    }

    // ── Filesystem tree shape ─────────────────────────────────────

    [Fact]
    public void Elements_UnsavedProject_RendersMembersAtRoot()
    {
        var proj = TestHelpers.CreateProjectWithForm("P", "F1");
        var module = new ModuleDefinition(proj, "M1", ModuleKind.StandardModule);
        proj.AddModule(module);

        CreateVmWithLoadedProject(proj, out var projVm);

        projVm.Elements.Should().HaveCount(2);
        projVm.Elements.OfType<DirectoryViewModel>().Should().BeEmpty();
    }

    [Fact]
    public void Elements_SubdirectoryMember_RendersUnderDirectoryNode()
    {
        var root = Path.Combine(Path.GetTempPath(), "hexpe-vm-tests", "App");
        var proj = TestHelpers.CreateProjectWithForm("P", "Main");
        proj.AbsolutePath = Path.Combine(root, "App.vbp");
        proj.Forms[0].AbsolutePath = Path.Combine(root, "Forms", "Main.frm");

        CreateVmWithLoadedProject(proj, out var projVm);

        var dir = projVm.Elements.Should().ContainSingle().Subject
            .Should().BeOfType<DirectoryViewModel>().Subject;
        dir.Name.Should().Be("Forms");
        dir.Children.Should().ContainSingle().Which.Should().BeOfType<FormViewModel>();
    }

    [Fact]
    public void Elements_MemberAbsolutePathChange_Rehomes()
    {
        var root = Path.Combine(Path.GetTempPath(), "hexpe-vm-tests", "App");
        var proj = TestHelpers.CreateProjectWithForm("P", "Main");
        proj.AbsolutePath = Path.Combine(root, "App.vbp");
        proj.Forms[0].AbsolutePath = Path.Combine(root, "Main.frm");
        CreateVmWithLoadedProject(proj, out var projVm);
        projVm.Elements.Should().ContainSingle().Which.Should().BeOfType<FormViewModel>();

        proj.Forms[0].AbsolutePath = Path.Combine(root, "Forms", "Main.frm");

        projVm.Elements.Should().ContainSingle().Which.Should().BeOfType<DirectoryViewModel>();
    }

    // ── Command can-execute ───────────────────────────────────────

    [Fact]
    public void ViewObjectCommand_CanExecute_TrueWhenFormSelected()
    {
        var proj = TestHelpers.CreateProjectWithForm("P", "F1");
        var vm = CreateVmWithLoadedProject(proj, out var projVm);

        vm.SelectedItem = FirstForm(projVm);

        vm.ViewObjectCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ViewObjectCommand_CanExecute_FalseWhenNoFormSelected()
    {
        var vm = CreateVm();

        vm.ViewObjectCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ViewCodeCommand_CanExecute_TrueWhenFormSelected()
    {
        var proj = TestHelpers.CreateProjectWithForm("P", "F1");
        var vm = CreateVmWithLoadedProject(proj, out var projVm);

        vm.SelectedItem = FirstForm(projVm);

        vm.ViewCodeCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ViewCodeCommand_CanExecute_TrueWhenModuleSelected()
    {
        var proj = TestHelpers.CreateProjectWithModule("P", "M1");
        var vm = CreateVmWithLoadedProject(proj, out var projVm);

        vm.SelectedItem = FirstModule(projVm);

        vm.ViewCodeCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ViewCodeCommand_CanExecute_FalseWhenNothingSelected()
    {
        var vm = CreateVm();

        vm.ViewCodeCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void AddFormCommand_CanExecute_TrueWhenProjectSelected()
    {
        var proj = TestHelpers.CreateProject();
        var vm = CreateVmWithLoadedProject(proj, out var projVm);

        vm.SelectedItem = projVm;

        vm.AddFormCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void AddFormCommand_CanExecute_TrueWhenFormSelected()
    {
        var proj = TestHelpers.CreateProjectWithForm("P", "F1");
        var vm = CreateVmWithLoadedProject(proj, out var projVm);

        vm.SelectedItem = FirstForm(projVm);

        vm.AddFormCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void AddFormCommand_CanExecute_TrueWhenDirectorySelected()
    {
        var root = Path.Combine(Path.GetTempPath(), "hexpe-vm-tests", "App");
        var proj = TestHelpers.CreateProjectWithForm("P", "Main");
        proj.AbsolutePath = Path.Combine(root, "App.vbp");
        proj.Forms[0].AbsolutePath = Path.Combine(root, "Forms", "Main.frm");
        var vm = CreateVmWithLoadedProject(proj, out var projVm);

        vm.SelectedItem = projVm.Elements.OfType<DirectoryViewModel>().First();

        vm.AddFormCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void AddFormCommand_CanExecute_FalseWhenNothingSelected()
    {
        var vm = CreateVm();

        vm.AddFormCommand.CanExecute(null).Should().BeFalse();
    }

    // ── Command execution ─────────────────────────────────────────

    [Fact]
    public void ViewObjectCommand_Execute_CallsEditFormOnEditorService()
    {
        var proj = TestHelpers.CreateProjectWithForm("P", "F1");
        var vm = CreateVmWithLoadedProject(proj, out var projVm);
        var formVm = FirstForm(projVm);
        vm.SelectedItem = formVm;

        vm.ViewObjectCommand.Execute(null);

        editorService.Received(1).EditForm(formVm.FormDefinition);
    }

    [Fact]
    public void ViewCodeCommand_Execute_ForForm_CallsEditCodeWithFormDefinition()
    {
        var proj = TestHelpers.CreateProjectWithForm("P", "F1");
        var vm = CreateVmWithLoadedProject(proj, out var projVm);
        var formVm = FirstForm(projVm);
        vm.SelectedItem = formVm;

        vm.ViewCodeCommand.Execute(null);

        editorService.Received(1).EditCode(formVm.FormDefinition);
    }

    [Fact]
    public void ViewCodeCommand_Execute_ForModule_CallsEditCodeWithModuleDefinition()
    {
        var proj = TestHelpers.CreateProjectWithModule("P", "M1");
        var vm = CreateVmWithLoadedProject(proj, out var projVm);
        var moduleVm = FirstModule(projVm);
        vm.SelectedItem = moduleVm;

        vm.ViewCodeCommand.Execute(null);

        editorService.Received(1).EditCode(moduleVm.ModuleDefinition);
    }

    // ── SetAsStartUp ──────────────────────────────────────────────

    [Fact]
    public void SetAsStartUp_SetsStartupProjectOnProjectManager()
    {
        var proj = TestHelpers.CreateProject("StartMe");
        var vm = CreateVmWithLoadedProject(proj, out var projVm);
        vm.SelectedItem = projVm;

        vm.SetAsStartUp();

        projectManager.StartupProject.Should().Be(proj);
    }

    [Fact]
    public void SetAsStartUp_DoesNothingWhenNoProjectSelected()
    {
        var vm = CreateVm();

        vm.SetAsStartUp();

        // StartupProject should not have been set
        projectManager.DidNotReceive().StartupProject = Arg.Any<ProjectDefinition>();
    }

    // ── DeleteForm ────────────────────────────────────────────────

    [Fact]
    public void DeleteForm_PublishesFormUnloadedEvent()
    {
        var proj = TestHelpers.CreateProjectWithForm("P", "F1");
        var form = proj.Forms[0];
        var vm = CreateVmWithLoadedProject(proj, out var projVm);
        vm.SelectedItem = FirstForm(projVm);

        vm.DeleteForm();

        eventBus.Received(1).Publish(Arg.Is<FormUnloadedEvent>(e => e.Form == form));
    }

    [Fact]
    public void DeleteForm_RemovesFormFromProjectDefinition()
    {
        var proj = TestHelpers.CreateProjectWithForm("P", "F1");
        var vm = CreateVmWithLoadedProject(proj, out var projVm);
        vm.SelectedItem = FirstForm(projVm);

        vm.DeleteForm();

        proj.Forms.Should().BeEmpty();
    }

    [Fact]
    public void DeleteForm_DoesNothingWhenNoFormSelected()
    {
        var proj = TestHelpers.CreateProjectWithForm("P", "F1");
        var vm = CreateVmWithLoadedProject(proj, out _);

        vm.DeleteForm();

        proj.Forms.Should().HaveCount(1);
        eventBus.DidNotReceive().Publish(Arg.Any<FormUnloadedEvent>());
    }
}
