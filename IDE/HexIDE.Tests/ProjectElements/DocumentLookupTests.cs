using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.ProjectElements;

/// <summary>
/// Finding a document by a name that came from outside the IDE — an automation client, an add-in. One
/// search, because the surfaces that do this used to disagree: some searched the startup project only, one
/// searched every open editor, and one built a key out of the caller's own spelling (hexide-io/HexIDE#467).
/// </summary>
public sealed class DocumentLookupTests
{
    private static ProjectDefinition AProject(string name, params string[] moduleNames)
    {
        var project = TestHelpers.CreateProject(name);
        foreach (var moduleName in moduleNames)
            project.AddModule(new ModuleDefinition(project, moduleName, ModuleKind.StandardModule));
        return project;
    }

    [Theory]
    [InlineData("Module1")]
    [InlineData("module1")]
    [InlineData("MODULE1")]
    public void ANameIsFoundInAnyCase(string spelling)
    {
        var project = AProject("P", "Module1");

        var found = DocumentLookup.Find([project], spelling);

        found.Should().ContainSingle();
        found[0].Name.Should().Be("Module1", "what comes back is the IDE's spelling, not the caller's");
    }

    [Fact]
    public void EveryLoadedProjectIsSearched()
    {
        var first = AProject("First", "Alpha");
        var second = AProject("Second", "Beta");

        DocumentLookup.Find([first, second], "Beta").Should().ContainSingle()
            .Which.Project.Should().BeSameAs(second);
    }

    [Fact]
    public void ANameInTwoProjectsAnswersWithBoth()
    {
        var first = AProject("First", "Module1");
        var second = AProject("Second", "Module1");

        var found = DocumentLookup.Find([first, second], "Module1");

        found.Should().HaveCount(2, "the caller decides what ambiguity means; the search does not pick");
        found.Select(d => d.Display).Should().Equal("First/Module1", "Second/Module1");
    }

    [Fact]
    public void AProjectNarrowsTheSearch()
    {
        var first = AProject("First", "Module1");
        var second = AProject("Second", "Module1");

        DocumentLookup.Find([first, second], "Module1", "SECOND").Should().ContainSingle()
            .Which.Project.Should().BeSameAs(second);
    }

    [Fact]
    public void AnUnknownNameAnswersWithNothing() =>
        DocumentLookup.Find([AProject("P", "Module1")], "Nowhere").Should().BeEmpty();

    [Fact]
    public void AUserControlIsListedOnceAsItsModule()
    {
        var project = TestHelpers.CreateProject();
        var module = new ModuleDefinition(project, "Gauge", ModuleKind.UserControl);
        module.UpdateFormPart(new FormDefinition(project, FormComponentClass.Instance, "Gauge"));
        project.AddModule(module);

        DocumentLookup.DocumentsOf(project).Should().ContainSingle()
            .Which.Module.Should().BeSameAs(module);
    }

    [Fact]
    public void FormsAndModulesAreListedTogether()
    {
        var project = AProject("P", "Helpers");
        project.AddForm(new FormDefinition(project, FormComponentClass.Instance, "Form1"));

        DocumentLookup.DocumentsOf(project).Select(d => d.Name)
            .Should().BeEquivalentTo(["Form1", "Helpers"]);
    }
}
