using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Tests;

public class SubMainStartupObjectTests
{
    private static ProjectDefinition ProjectWithForm(out FormDefinition form)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        project.AddForm(form);
        return project;
    }

    [Fact]
    public void ChoosingSubMainClearsTheStartupForm()
    {
        var project = ProjectWithForm(out var form);
        project.StartupForm.Should().Be(form, "the first form added becomes the startup");

        project.StartsAtSubMain = true;

        project.StartsAtSubMain.Should().BeTrue();
        project.StartupForm.Should().BeNull("a project starts at a form or at Sub Main, never both");
    }

    [Fact]
    public void ChoosingAFormClearsSubMain()
    {
        var project = ProjectWithForm(out var form);
        project.StartsAtSubMain = true;

        project.StartupForm = form;

        project.StartupForm.Should().Be(form);
        project.StartsAtSubMain.Should().BeFalse("the invariant holds in both directions");
    }

    [Fact]
    public void AddingAFormDoesNotStealTheStartupFromSubMain()
    {
        // AddForm sets the first form as the startup, which is right for a new project and wrong for one
        // that already starts at Sub Main: adding a form would silently move the startup object, and the
        // user would only find out at the next F5.
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.StartsAtSubMain = true;

        project.AddForm(new FormDefinition(project, FormComponentClass.Instance, "Form1"));

        project.StartsAtSubMain.Should().BeTrue();
        project.StartupForm.Should().BeNull();
    }

    [Fact]
    public void AddingTheFirstFormToAFreshProjectStillMakesItTheStartup()
    {
        // The behaviour the guard above must not break.
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");

        project.AddForm(form);

        project.StartupForm.Should().Be(form);
    }
}
