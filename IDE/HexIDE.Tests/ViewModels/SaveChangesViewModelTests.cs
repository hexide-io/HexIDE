using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.ViewModels;

public class SaveChangesViewModelTests
{
    private readonly SaveChangesViewModel _sut = new();

    [Fact]
    public void Title_ShouldBeHexIDE()
    {
        _sut.Title.Should().Be("HexIDE");
    }

    [Fact]
    public void CanResize_ShouldBeFalse()
    {
        _sut.CanResize.Should().BeFalse();
    }

    [Fact]
    public void SaveChanges_DefaultsToFalse()
    {
        _sut.SaveChanges.Should().BeFalse();
    }

    [Fact]
    public void Yes_SetsSaveChangesToTrue_AndFiresCloseRequestedTrue()
    {
        bool? received = null;
        _sut.CloseRequested += value => received = value;

        _sut.Yes();

        _sut.SaveChanges.Should().BeTrue();
        received.Should().BeTrue();
    }

    [Fact]
    public void No_SetsSaveChangesToFalse_AndFiresCloseRequestedTrue()
    {
        bool? received = null;
        _sut.CloseRequested += value => received = value;

        _sut.No();

        _sut.SaveChanges.Should().BeFalse();
        received.Should().BeTrue();
    }

    [Fact]
    public void Cancel_FiresCloseRequestedFalse()
    {
        bool? received = null;
        _sut.CloseRequested += value => received = value;

        _sut.Cancel();

        received.Should().BeFalse();
    }

    [Fact]
    public void AddProject_AddsToChangedFiles_WithCorrectNameAndNoIndent()
    {
        var project = TestHelpers.CreateProject("MyProject");

        _sut.Add(project);

        _sut.ChangedFiles.Should().HaveCount(1);
        var item = _sut.ChangedFiles[0];
        item.Name.Should().Be("MyProject");
        item.Indent.Should().BeFalse();
        item.Project.Should().BeSameAs(project);
        item.Form.Should().BeNull();
    }

    [Fact]
    public void AddForm_AddsToChangedFiles_WithCorrectNameAndIndent()
    {
        var project = TestHelpers.CreateProject();
        var form = TestHelpers.CreateForm(project, "MyForm");

        _sut.Add(form);

        _sut.ChangedFiles.Should().HaveCount(1);
        var item = _sut.ChangedFiles[0];
        item.Name.Should().Be("MyForm");
        item.Indent.Should().BeTrue();
        item.Form.Should().BeSameAs(form);
        item.Project.Should().BeNull();
    }

    [Fact]
    public void ChangedFileViewModel_ToString_ReturnsIndentedNameForForms()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var vm = new ChangedFileViewModel(form);

        vm.ToString().Should().Be("    Form1");
    }

    [Fact]
    public void ChangedFileViewModel_ToString_ReturnsPlainNameForProjects()
    {
        var project = TestHelpers.CreateProject("Project1");
        var vm = new ChangedFileViewModel(project);

        vm.ToString().Should().Be("Project1");
    }

    [Fact]
    public void MultipleAdds_AccumulateInChangedFiles()
    {
        var project = TestHelpers.CreateProject("Proj");
        var form1 = TestHelpers.CreateForm(project, "FormA");
        var form2 = TestHelpers.CreateForm(project, "FormB");

        _sut.Add(project);
        _sut.Add(form1);
        _sut.Add(form2);

        _sut.ChangedFiles.Should().HaveCount(3);
        _sut.ChangedFiles[0].Name.Should().Be("Proj");
        _sut.ChangedFiles[1].Name.Should().Be("FormA");
        _sut.ChangedFiles[2].Name.Should().Be("FormB");
    }
}
