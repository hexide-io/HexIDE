using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.ProjectElements;

/// <summary>
/// VB6's own naming rules, measured against the real compiler and recorded in
/// <c>docs/vb6-fidelity-oracle.md</c>: a name is unique within a project across every kind, two projects of
/// one group may not share a name, and both comparisons ignore case.
/// </summary>
public sealed class ProjectNamingTests
{
    private static ProjectDefinition WithDocuments(params string[] names)
    {
        var project = TestHelpers.CreateProject();
        foreach (var name in names)
            project.AddModule(new ModuleDefinition(project, name, ModuleKind.StandardModule));
        return project;
    }

    [Theory]
    [InlineData("Form1")]
    [InlineData("A")]
    [InlineData("Some_Name_9")]
    [InlineData("Ärger")]
    public void AVb6NameIsALetterThenLettersDigitsAndUnderscores(string name) =>
        ProjectNaming.IsValidName(name).Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1Form")]
    [InlineData("_Leading")]
    [InlineData("My Form")]
    [InlineData("My/Form")]
    [InlineData("My#Form")]
    [InlineData("My?Form")]
    [InlineData("Form-1")]
    public void AnythingElseIsNot(string? name) =>
        ProjectNaming.IsValidName(name).Should().BeFalse();

    /// <summary>
    /// Forms and modules share one namespace. VB6 refuses to build a project holding
    /// <c>Form=Thing.frm</c> beside <c>Module=Thing</c>, whatever their order and whatever the case.
    /// </summary>
    [Fact]
    public void AFormAndAModuleOfOneProjectCannotShareAName()
    {
        var project = WithDocuments("Helpers");
        project.AddForm(new FormDefinition(project, FormComponentClass.Instance, "Form1"));

        ProjectNaming.IsNameTaken(project, "helpers").Should().BeTrue();
        ProjectNaming.IsNameTaken(project, "FORM1").Should().BeTrue();
        ProjectNaming.IsNameTaken(project, "Elsewhere").Should().BeFalse();
    }

    [Fact]
    public void ADocumentDoesNotCollideWithItself()
    {
        var project = WithDocuments("Helpers");
        var itself = DocumentIdentity.For(project.Modules[0]);

        ProjectNaming.IsNameTaken(project, "HELPERS", itself).Should().BeFalse();
        ProjectNaming.IsNameTaken(project, "HELPERS").Should().BeTrue();
    }

    /// <summary>
    /// The scenario from the delta: the lowest unused index, so a deleted name comes back rather than being
    /// skipped forever.
    /// </summary>
    [Fact]
    public void AddingAFormAfterDeletingOneDoesNotSkipTheFreedName()
    {
        var project = TestHelpers.CreateProject();
        var form1 = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        var form2 = new FormDefinition(project, FormComponentClass.Instance, "Form2");
        project.AddForm(form1);
        project.AddForm(form2);
        project.DeleteForm(form1);

        ProjectNaming.NextFreeName(project, "Form").Should().Be("Form1");
    }

    [Fact]
    public void ANewNameStepsOverEveryKind()
    {
        var project = WithDocuments("Form1");   // a standard module called Form1

        ProjectNaming.NextFreeName(project, "Form").Should().Be("Form2",
            "forms and modules share one namespace, so a module called Form1 takes that name");
    }

    /// <summary>
    /// The second delta scenario: closing a project frees its name rather than pushing the counter on.
    /// </summary>
    [Fact]
    public void StartingAProjectAfterClosingOneDoesNotSkipTheFreedName()
    {
        var second = TestHelpers.CreateProject("Project2");

        ProjectNaming.NextFreeProjectName([second]).Should().Be("Project1");
    }

    [Fact]
    public void TwoLoadedProjectsCannotShareAName()
    {
        var first = TestHelpers.CreateProject("Project1");
        var second = TestHelpers.CreateProject("Project2");

        ProjectNaming.IsProjectNameTaken([first, second], "PROJECT1").Should().BeTrue();
        ProjectNaming.IsProjectNameTaken([first, second], "PROJECT1", first).Should().BeFalse();
        ProjectNaming.IsProjectNameTaken([first, second], "Project3").Should().BeFalse();
    }
}
