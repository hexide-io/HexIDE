using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.Projects;

/// <summary>
/// The IDE half of <c>Sub Main</c> as a startup object (#210): the model, the Project Properties list,
/// and the <c>Startup=</c> line. The interpreter half — which procedure is chosen and why — is measured
/// against real vb6.exe and covered by <c>SubMainStartupTests</c> in the runtime suite.
///
/// <para>
/// The startup object was modelled as a nullable FORM reference, so <c>Sub Main</c> had nowhere to live
/// and a code-only Standard EXE could not run. What is guarded here is mostly the INVARIANT that came
/// with adding it: a project starts at a form or at <c>Sub Main</c>, never both, and the two properties
/// must not be able to drift into a state where the dialog says one thing and F5 does another.
/// </para>
/// </summary>
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

    [Fact]
    public void ProjectPropertiesOffersSubMainFirstAndAlwaysOffersIt()
    {
        // Listed UNCONDITIONALLY, as VB6 lists it — not gated on a Sub Main existing. Gating would make
        // the entry appear and vanish as code is edited, and would stop the user choosing it BEFORE
        // writing the procedure, which is the order a code-only project is usually built in.
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var vm = new ProjectPropertiesViewModel(project);

        vm.StartupObjects.Should().NotBeEmpty();
        vm.StartupObjects[0].IsSubMain.Should().BeTrue("Sub Main comes first, as in VB6");
        vm.StartupObjects.Should().ContainSingle(o => o.IsSubMain);
    }

    [Fact]
    public void ProjectPropertiesPreselectsSubMainWhenTheProjectStartsThere()
    {
        var project = ProjectWithForm(out _);
        project.StartsAtSubMain = true;

        var vm = new ProjectPropertiesViewModel(project);

        vm.SelectedStartupObject.Should().NotBeNull();
        vm.SelectedStartupObject!.IsSubMain.Should().BeTrue();
    }

    [Fact]
    public void ApplyingSubMainFromTheDialogSetsTheModel()
    {
        var project = ProjectWithForm(out _);
        var vm = new ProjectPropertiesViewModel(project);
        vm.SelectedStartupObject = vm.StartupObjects.First(o => o.IsSubMain);

        vm.Apply(project);

        project.StartsAtSubMain.Should().BeTrue();
        project.StartupForm.Should().BeNull();
    }

    [Fact]
    public void ApplyingAFormFromTheDialogClearsSubMain()
    {
        var project = ProjectWithForm(out var form);
        project.StartsAtSubMain = true;
        var vm = new ProjectPropertiesViewModel(project);
        vm.SelectedStartupObject = vm.StartupObjects.First(o => !o.IsSubMain);

        vm.Apply(project);

        project.StartupForm.Should().Be(form);
        project.StartsAtSubMain.Should().BeFalse();
    }

    [Fact]
    public void TheSubMainEntrysHeaderIsTheValueWrittenToTheVbp()
    {
        // The dialog and the file agree by construction rather than by two string literals that happen
        // to match — `Startup="Sub Main"` is what VB6 writes, spaces and casing included.
        ProjectStartupObjectViewModel.SubMain.Header.Should()
            .Be(HexIDE.Runtime.Serialization.SerializedProject.SubMainStartup);
    }
}
