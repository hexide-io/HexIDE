using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// A project may not be renamed to the name of another loaded project — VB6 refuses such a group at load
/// and builds none of it — and the refusal has to be readable, not merely a button that stopped working.
/// </summary>
public sealed class ProjectNameRefusalTests
{
    /// <summary>
    /// The validator the IDE supplies, built the same way <c>ProjectService</c> builds it but with the
    /// message text standing in for the localized strings.
    /// </summary>
    private static Func<string, string?> Validator(
        ProjectDefinition renaming, params ProjectDefinition[] loaded) =>
        proposed =>
            !ProjectNaming.IsValidName(proposed) ? $"not a name: {proposed}"
            : ProjectNaming.IsProjectNameTaken(loaded, proposed, renaming) ? $"already loaded: {proposed}"
            : null;

    [Fact]
    public void RenamingAProjectToAnotherLoadedProjectsNameIsRefused()
    {
        var first = TestHelpers.CreateProject("Project1");
        var second = TestHelpers.CreateProject("Project2");
        var vm = new ProjectPropertiesViewModel(second, Validator(second, first, second));

        vm.ProjectName = "PROJECT1";

        vm.NameError.Should().Be("already loaded: PROJECT1",
            "the reason has to name the other project, which is the whole of what the developer needs");
        vm.OkCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void AProjectMayKeepItsOwnName()
    {
        var first = TestHelpers.CreateProject("Project1");
        var vm = new ProjectPropertiesViewModel(first, Validator(first, first));

        vm.ProjectName = "PROJECT1";

        vm.NameError.Should().BeNull("a project does not collide with itself");
        vm.OkCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void AProjectNameThatIsNotAVb6NameIsRefused()
    {
        var project = TestHelpers.CreateProject("Project1");
        var vm = new ProjectPropertiesViewModel(project, Validator(project, project));

        vm.ProjectName = "My Project/2";

        vm.NameError.Should().Be("not a name: My Project/2");
        vm.OkCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ClearingTheObjectionReEnablesOk()
    {
        var first = TestHelpers.CreateProject("Project1");
        var second = TestHelpers.CreateProject("Project2");
        var vm = new ProjectPropertiesViewModel(second, Validator(second, first, second));

        vm.ProjectName = "Project1";
        vm.ProjectName = "Project3";

        vm.NameError.Should().BeNull();
        vm.OkCommand.CanExecute(null).Should().BeTrue();
    }

    /// <summary>
    /// A dialog opened on a project that is already in breach — one loaded from a group HexIDE did not
    /// create — says so from the start rather than only after the first keystroke.
    /// </summary>
    [Fact]
    public void ADialogOpenedOnAnAlreadyCollidingNameSaysSoImmediately()
    {
        var first = TestHelpers.CreateProject("Same");
        var second = TestHelpers.CreateProject("Same");
        var vm = new ProjectPropertiesViewModel(second, Validator(second, first, second));

        vm.NameError.Should().Be("already loaded: Same");
        vm.OkCommand.CanExecute(null).Should().BeFalse();
    }
}
