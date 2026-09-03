using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.ViewModels;

public class ProjectPropertiesViewModelTests
{
    [Fact]
    public void Title_IncludesProjectName()
    {
        var project = TestHelpers.CreateProject("MyApp");
        var vm = new ProjectPropertiesViewModel(project);

        vm.Title.Should().Contain("MyApp");
    }

    [Fact]
    public void CanResize_IsFalse()
    {
        var project = TestHelpers.CreateProject();
        var vm = new ProjectPropertiesViewModel(project);

        vm.CanResize.Should().BeFalse();
    }

    [Fact]
    public void ProjectName_InitializedFromProjectDefinition()
    {
        var project = TestHelpers.CreateProject("SomeProject");
        var vm = new ProjectPropertiesViewModel(project);

        vm.ProjectName.Should().Be("SomeProject");
    }

    [Fact]
    public void ProjectDescription_InitializedFromProjectDefinition()
    {
        var project = TestHelpers.CreateProject("Proj");
        project.Description = "A test description";
        var vm = new ProjectPropertiesViewModel(project);

        vm.ProjectDescription.Should().Be("A test description");
    }

    [Fact]
    public void SelectedProjectType_InitializedFromProjectDefinition()
    {
        var project = TestHelpers.CreateProject();
        var vm = new ProjectPropertiesViewModel(project);

        vm.SelectedProjectType.Should().Be(VBProjectType.EXE);
    }

    [Fact]
    public void StartupObjects_PopulatedFromProjectForms()
    {
        var project = TestHelpers.CreateProject("Proj");
        var form1 = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        project.AddForm(form1);
        var form2 = new FormDefinition(project, FormComponentClass.Instance, "Form2");
        project.AddForm(form2);

        var vm = new ProjectPropertiesViewModel(project);

        // Sub Main leads the list and every form follows in project order — VB6's own arrangement (#210).
        // This asserted exactly two entries before Sub Main became selectable; the forms are still all
        // there and still in order, they are simply no longer first.
        vm.StartupObjects.Should().HaveCount(3);
        vm.StartupObjects[0].IsSubMain.Should().BeTrue();
        vm.StartupObjects[1].Form.Should().Be(form1);
        vm.StartupObjects[2].Form.Should().Be(form2);
    }

    [Fact]
    public void SelectedStartupObject_MatchesProjectStartupForm()
    {
        var project = TestHelpers.CreateProjectWithForm("Proj", "MainForm");
        var startupForm = project.StartupForm;

        var vm = new ProjectPropertiesViewModel(project);

        vm.SelectedStartupObject.Should().NotBeNull();
        vm.SelectedStartupObject!.Form.Should().Be(startupForm);
    }

    [Fact]
    public void OkCommand_CanExecute_WhenProjectNameIsNotEmpty()
    {
        var project = TestHelpers.CreateProject("NonEmpty");
        var vm = new ProjectPropertiesViewModel(project);

        vm.OkCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void OkCommand_CannotExecute_WhenProjectNameIsEmpty()
    {
        var project = TestHelpers.CreateProject("NonEmpty");
        var vm = new ProjectPropertiesViewModel(project);

        vm.ProjectName = "";

        vm.OkCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void OkCommand_FiresCloseRequestedTrue()
    {
        var project = TestHelpers.CreateProject();
        var vm = new ProjectPropertiesViewModel(project);
        bool? receivedValue = null;
        vm.CloseRequested += val => receivedValue = val;

        vm.OkCommand.Execute(null);

        receivedValue.Should().BeTrue();
    }

    [Fact]
    public void CancelCommand_FiresCloseRequestedFalse()
    {
        var project = TestHelpers.CreateProject();
        var vm = new ProjectPropertiesViewModel(project);
        bool? receivedValue = null;
        vm.CloseRequested += val => receivedValue = val;

        vm.CancelCommand.Execute(null);

        receivedValue.Should().BeFalse();
    }

    [Fact]
    public void Apply_WritesBackName()
    {
        var project = TestHelpers.CreateProject("Original");
        var vm = new ProjectPropertiesViewModel(project);
        vm.ProjectName = "Updated";

        var target = TestHelpers.CreateProject("Target");
        vm.Apply(target);

        target.Name.Should().Be("Updated");
    }

    [Fact]
    public void Apply_WritesBackDescription()
    {
        var project = TestHelpers.CreateProject();
        var vm = new ProjectPropertiesViewModel(project);
        vm.ProjectDescription = "New description";

        var target = TestHelpers.CreateProject();
        vm.Apply(target);

        target.Description.Should().Be("New description");
    }

    [Fact]
    public void Apply_WritesBackProjectType()
    {
        var project = TestHelpers.CreateProject();
        var vm = new ProjectPropertiesViewModel(project);
        vm.SelectedProjectType = VBProjectType.EXE;

        var target = TestHelpers.CreateProject();
        vm.Apply(target);

        target.ProjectType.Should().Be(VBProjectType.EXE);
    }

    [Fact]
    public void Apply_WritesBackStartupForm()
    {
        var project = TestHelpers.CreateProjectWithForm("Proj", "Form1");
        var vm = new ProjectPropertiesViewModel(project);

        var target = TestHelpers.CreateProject("Target");
        vm.Apply(target);

        target.StartupForm.Should().Be(vm.SelectedStartupObject?.Form);
    }
}
