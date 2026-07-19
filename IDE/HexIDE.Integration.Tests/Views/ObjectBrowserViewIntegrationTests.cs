using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Tools.ObjectBrowser;
using System.Linq;

namespace HexIDE.Integration.Tests.Views;

public class ObjectBrowserViewIntegrationTests
{
    // The Object Browser tab title is now localized via ILocalizationService; configure the stub to
    // return the English title so the chrome renders as before.
    private static ILocalizationService Loc()
    {
        var loc = Substitute.For<ILocalizationService>();
        loc.GetString("Str.Tool.ObjectBrowser.Title").Returns("Object Browser");
        return loc;
    }

    private static ObjectBrowserToolViewModel CreateEmptySut()
    {
        var pm = Substitute.For<IProjectManager>();
        pm.LoadedProjects.Returns(new List<ProjectDefinition>());
        return new ObjectBrowserToolViewModel(
            pm,
            Substitute.For<ILspClient>(),
            Substitute.For<IEditorService>(),
            Substitute.For<IComponentRegistry>(),
            Substitute.For<ITypeLibraryService>(),
            Substitute.For<IFocusedProjectUtil>(),
            Loc());
    }

    private static ObjectBrowserToolViewModel CreateSutWithProject(string projectName = "TestProject")
    {
        var project = new ProjectDefinition(VBProjectType.EXE, projectName);
        project.AddForm(new FormDefinition(project, FormComponentClass.Instance, "Form1"));

        var pm = Substitute.For<IProjectManager>();
        pm.LoadedProjects.Returns(new List<ProjectDefinition> { project });

        return new ObjectBrowserToolViewModel(
            pm,
            Substitute.For<ILspClient>(),
            Substitute.For<IEditorService>(),
            Substitute.For<IComponentRegistry>(),
            Substitute.For<ITypeLibraryService>(),
            Substitute.For<IFocusedProjectUtil>(),
            Loc());
    }

    private static ObjectBrowserToolViewModel CreateSutWithTwoForms()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "NavTest");
        project.AddForm(new FormDefinition(project, FormComponentClass.Instance, "Form1"));
        project.AddForm(new FormDefinition(project, FormComponentClass.Instance, "Form2"));

        var pm = Substitute.For<IProjectManager>();
        pm.LoadedProjects.Returns(new List<ProjectDefinition> { project });

        return new ObjectBrowserToolViewModel(
            pm,
            Substitute.For<ILspClient>(),
            Substitute.For<IEditorService>(),
            Substitute.For<IComponentRegistry>(),
            Substitute.For<ITypeLibraryService>(),
            Substitute.For<IFocusedProjectUtil>(),
            Loc());
    }

    // --- View rendering ---

    [AvaloniaFact]
    public void View_IsUserControl()
    {
        new ObjectBrowserToolView().Should().BeAssignableTo<UserControl>();
    }

    [AvaloniaFact]
    public void View_WithEmptyState_RendersWithoutErrors()
    {
        var view = new ObjectBrowserToolView { DataContext = CreateEmptySut() };
        view.Measure(new Size(800, 400));
        view.Arrange(new Rect(0, 0, 800, 400));
        view.IsMeasureValid.Should().BeTrue();
        view.IsArrangeValid.Should().BeTrue();
    }

    [AvaloniaFact]
    public void View_WithProjectData_RendersWithoutErrors()
    {
        var view = new ObjectBrowserToolView { DataContext = CreateSutWithProject() };
        view.Measure(new Size(800, 400));
        view.Arrange(new Rect(0, 0, 800, 400));
        view.IsMeasureValid.Should().BeTrue();
        view.IsArrangeValid.Should().BeTrue();
    }

    // --- Document tab chrome ---

    [AvaloniaFact]
    public void Title_IsObjectBrowser()
    {
        CreateEmptySut().Title.Should().Be("Object Browser");
    }

    [AvaloniaFact]
    public void CanClose_IsTrue_DocTabHasCloseButton()
    {
        // CanClose = true causes the Dock framework to render an X on the
        // Document tab. This is the architectural guarantee that the Object
        // Browser always has closeable chrome, regardless of MDI state.
        CreateEmptySut().CanClose.Should().BeTrue();
    }

    [AvaloniaFact]
    public void CanFloat_IsFalse_PreventsMdiChromeLoss()
    {
        // CanFloat = false prevents the VB6 bug where the OB lost its
        // title bar / close button when floated as an MDI child and maximised.
        CreateEmptySut().CanFloat.Should().BeFalse();
    }

    [AvaloniaFact]
    public void CloseCommand_IsNotNull()
    {
        CreateEmptySut().CloseCommand.Should().NotBeNull();
    }

    [AvaloniaFact]
    public void BackCommand_Initially_CannotExecute()
    {
        CreateEmptySut().BackCommand.CanExecute(null).Should().BeFalse();
    }

    [AvaloniaFact]
    public void ForwardCommand_Initially_CannotExecute()
    {
        CreateEmptySut().ForwardCommand.CanExecute(null).Should().BeFalse();
    }

    [AvaloniaFact]
    public void BackCommand_AfterSelectingTwoClasses_CanExecute()
    {
        var vm = CreateSutWithTwoForms();
        vm.SelectedClass = vm.FilteredClasses[0];
        vm.SelectedClass = vm.FilteredClasses[1];
        vm.BackCommand.CanExecute(null).Should().BeTrue();
    }

    [AvaloniaFact]
    public void ForwardCommand_AfterBackNavigation_CanExecute()
    {
        var vm = CreateSutWithTwoForms();
        vm.SelectedClass = vm.FilteredClasses[0];
        vm.SelectedClass = vm.FilteredClasses[1];
        vm.BackCommand.Execute(null);
        vm.ForwardCommand.CanExecute(null).Should().BeTrue();
    }

    [AvaloniaFact]
    public void BackCommand_Execute_RestoresPreviousClass()
    {
        var vm = CreateSutWithTwoForms();
        var first = vm.FilteredClasses[0];
        vm.SelectedClass = first;
        vm.SelectedClass = vm.FilteredClasses[1];
        vm.BackCommand.Execute(null);
        vm.SelectedClass.Should().Be(first);
    }

    [AvaloniaFact]
    public void CloseCommand_Execute_WhenNoOwner_DoesNotThrow()
    {
        var vm = CreateEmptySut();
        var act = () => vm.CloseCommand.Execute(null);
        act.Should().NotThrow();
    }

    // --- Data population ---

    [AvaloniaFact]
    public void WithProjectLoaded_FilteredClasses_ContainsProjectForms()
    {
        var vm = CreateSutWithProject("MyApp");
        vm.FilteredClasses.Should().ContainSingle(c => c.Name == "Form1" && c.Kind == OBClassKind.Form);
    }

    [AvaloniaFact]
    public void WithProjectLoaded_Libraries_ContainsProjectByName()
    {
        var vm = CreateSutWithProject("MyApp");
        vm.Libraries.Should().ContainSingle(lib => lib.Name == "MyApp");
    }

    [AvaloniaFact]
    public void WithNoProject_FilteredClasses_IsEmpty()
    {
        CreateEmptySut().FilteredClasses.Should().BeEmpty();
    }

    // --- Phase B: VB runtime library ---

    private static ObjectBrowserToolViewModel CreateSutWithVbLibrary()
    {
        var pm = Substitute.For<IProjectManager>();
        pm.LoadedProjects.Returns(new List<ProjectDefinition>());
        return new ObjectBrowserToolViewModel(
            pm,
            Substitute.For<ILspClient>(),
            Substitute.For<IEditorService>(),
            new ComponentRegistry(),
            Substitute.For<ITypeLibraryService>(),
            Substitute.For<IFocusedProjectUtil>(),
            Loc());
    }

    [AvaloniaFact]
    public void WithVbLibrary_Libraries_ContainsVbEntry()
    {
        CreateSutWithVbLibrary().Libraries.Should().ContainSingle(lib => lib.Name == "VB");
    }

    [AvaloniaFact]
    public void WithVbLibrary_FilteredClasses_ContainsFormClass()
    {
        var vm = CreateSutWithVbLibrary();
        vm.FilteredClasses.Should().Contain(c => c.Name == "Form" && c.Kind == OBClassKind.ClassModule);
    }

    [AvaloniaFact]
    public void WithVbLibrary_FormClass_HasPropertyMembers()
    {
        var vm = CreateSutWithVbLibrary();
        var form = vm.Libraries.Single(l => l.Name == "VB").Classes.First(c => c.Name == "Form");
        form.Members.Should().Contain(m => m.Kind == OBMemberKind.Property);
    }

    [AvaloniaFact]
    public void WithVbLibrary_FormClass_HasEventMembers()
    {
        var vm = CreateSutWithVbLibrary();
        var form = vm.Libraries.Single(l => l.Name == "VB").Classes.First(c => c.Name == "Form");
        form.Members.Should().Contain(m => m.Kind == OBMemberKind.Event);
    }
}
