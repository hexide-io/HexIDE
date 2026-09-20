using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.ProjectElements;

/// <summary>
/// A document's identity is the definition, compared by reference, carrying its project — so it survives
/// every one of the names a document answers to, all of which change while the developer works.
/// </summary>
public sealed class DocumentIdentityTests
{
    private static (ProjectDefinition Project, ModuleDefinition Module) AProjectWithAModule(
        string projectName = "Project1", string moduleName = "Module1")
    {
        var project = TestHelpers.CreateProject(projectName);
        var module = new ModuleDefinition(project, moduleName, ModuleKind.StandardModule);
        project.AddModule(module);
        return (project, module);
    }

    [Fact]
    public void TwoIdentitiesForOneModuleAreEqual()
    {
        var (_, module) = AProjectWithAModule();

        DocumentIdentity.For(module).Should().Be(DocumentIdentity.For(module));
        DocumentIdentity.For(module).GetHashCode().Should().Be(DocumentIdentity.For(module).GetHashCode());
    }

    [Fact]
    public void ARenameDoesNotChangeTheIdentity()
    {
        var (_, module) = AProjectWithAModule();
        var before = DocumentIdentity.For(module);

        module.Name = "Utilities";

        DocumentIdentity.For(module).Should().Be(before);
        before.Name.Should().Be("Utilities", "the name is read live, never captured");
    }

    [Fact]
    public void AFirstSaveDoesNotChangeTheIdentity()
    {
        var (_, module) = AProjectWithAModule();
        var before = DocumentIdentity.For(module);

        module.AbsolutePath = @"C:\somewhere\util.bas";

        DocumentIdentity.For(module).Should().Be(before);
        before.AbsolutePath.Should().Be(@"C:\somewhere\util.bas");
    }

    [Fact]
    public void AProjectRenameDoesNotChangeTheIdentity()
    {
        var (project, module) = AProjectWithAModule();
        var before = DocumentIdentity.For(module);

        project.Name = "Renamed";

        DocumentIdentity.For(module).Should().Be(before);
        before.Display.Should().Be("Renamed/Module1");
    }

    [Fact]
    public void TwoProjectsWithAModuleOfTheSameNameAreDifferentDocuments()
    {
        var (_, first) = AProjectWithAModule("Project1");
        var (_, second) = AProjectWithAModule("Project2");

        DocumentIdentity.For(first).Should().NotBe(DocumentIdentity.For(second));
        DocumentIdentity.For(first).Display.Should().Be("Project1/Module1");
        DocumentIdentity.For(second).Display.Should().Be("Project2/Module1");
    }

    [Fact]
    public void AUserControlHasOneIdentity_ItsModules()
    {
        var project = TestHelpers.CreateProject();
        var module = new ModuleDefinition(project, "Gauge", ModuleKind.UserControl);
        var formPart = new FormDefinition(project, FormComponentClass.Instance, "Gauge");
        formPart.UpdateRootTypeName("VB.UserControl");
        module.UpdateFormPart(formPart);
        project.AddModule(module);

        DocumentIdentity.For(formPart).Should().Be(DocumentIdentity.For(module));
        DocumentIdentity.For(formPart).Module.Should().BeSameAs(module);
    }

    /// <summary>
    /// The window that a scan of the project's modules would answer wrongly in: creation joins the two
    /// halves several statements before the module is added, and the load path does the same.
    /// </summary>
    [Fact]
    public void AUserControlHasOneIdentityBeforeItsModuleJoinsTheProject()
    {
        var project = TestHelpers.CreateProject();
        var module = new ModuleDefinition(project, "Gauge", ModuleKind.UserControl);
        var formPart = new FormDefinition(project, FormComponentClass.Instance, "Gauge");
        module.UpdateFormPart(formPart);
        // No project.AddModule here, deliberately.

        DocumentIdentity.For(formPart).Should().Be(DocumentIdentity.For(module));
    }

    [Fact]
    public void DetachingADesignerHalfLetsTheFormStandAlone()
    {
        var project = TestHelpers.CreateProject();
        var module = new ModuleDefinition(project, "Gauge", ModuleKind.UserControl);
        var formPart = new FormDefinition(project, FormComponentClass.Instance, "Gauge");
        module.UpdateFormPart(formPart);
        module.UpdateFormPart(null);

        DocumentIdentity.For(formPart).Should().NotBe(DocumentIdentity.For(module));
    }

    [Fact]
    public void AFormIsItsOwnDocument()
    {
        var project = TestHelpers.CreateProject();
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        project.AddForm(form);

        var identity = DocumentIdentity.For(form);
        identity.IsForm.Should().BeTrue();
        identity.Form.Should().BeSameAs(form);
        identity.Module.Should().BeNull();
    }

    [Theory]
    [InlineData("Module1")]
    [InlineData("MODULE1")]
    [InlineData("module1")]
    public void ANameIsMatchedWithoutRegardToCase(string spelling)
    {
        var (_, module) = AProjectWithAModule();

        DocumentIdentity.For(module).IsNamed(spelling).Should().BeTrue();
    }

    [Fact]
    public void ADocumentKnowsWhichProjectItIsIn()
    {
        var (project, module) = AProjectWithAModule();
        var other = TestHelpers.CreateProject("Other");

        DocumentIdentity.For(module).IsIn(project).Should().BeTrue();
        DocumentIdentity.For(module).IsIn(other).Should().BeFalse();
    }

    /// <summary>
    /// A dictionary keyed by identity is what both mark stores are, so the key has to survive a rename in a
    /// dictionary rather than merely compare equal outside one.
    /// </summary>
    [Fact]
    public void AnEntryKeyedByIdentityIsStillFoundAfterARename()
    {
        var (_, module) = AProjectWithAModule();
        var marks = new Dictionary<DocumentIdentity, int> { [DocumentIdentity.For(module)] = 7 };

        module.Name = "Utilities";

        marks[DocumentIdentity.For(module)].Should().Be(7);
    }
}
