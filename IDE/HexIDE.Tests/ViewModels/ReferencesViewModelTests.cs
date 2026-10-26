using System.Collections.Generic;
using System.Linq;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// The References dialog (#1026). OK with nothing changed must leave the project's references as they were: its fixed
/// first row used to carry OLE Automation's GUID, which hid the project's own OLE Automation reference, and OK then
/// dropped it, so the next save wrote the project without it.
/// </summary>
/// <remarks>
/// What VB6's dialog shows is measured (docs/vb6-fidelity-oracle/the-references-dialogs-fixed-rows.md, "The References dialog's fixed rows"): three rows
/// no project lists, Visual Basic For Applications, Visual Basic runtime objects and procedures and Visual Basic objects
/// and procedures, checked and fixed; then the project's own references, OLE Automation among them, in its order, the
/// first of which can't be moved above them; the selection on the last checked row.
/// </remarks>
public class ReferencesViewModelTests
{
    private static readonly VbReference OleAutomation =
        new("{00020430-0000-0000-C000-000000000046}", "2.0", 0, @"..\Windows\SysWOW64\stdole2.tlb", "OLE Automation");

    private static readonly VbReference ScriptingRuntime =
        new("{420B2830-E718-11CF-893D-00A0C9054228}", "1.0", 0, @"..\Windows\SysWOW64\scrrun.dll", "Microsoft Scripting Runtime");

    private static ProjectDefinition Project(params VbReference[] references)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        foreach (var reference in references)
            project.AddReference(reference);
        return project;
    }

    private static ReferencesViewModel Dialog(ProjectDefinition project, params AvailableReference[] available)
    {
        var libraries = Substitute.For<IReferenceLibraryService>();
        libraries.GetAvailableReferences().Returns(available);
        return new ReferencesViewModel(project, libraries, Substitute.For<IWindowManager>());
    }

    [Fact]
    public void OK_with_nothing_changed_leaves_the_references_as_they_were()
    {
        var project = Project(OleAutomation, ScriptingRuntime);

        Dialog(project).Apply(project);

        project.References.Should().Equal(OleAutomation, ScriptingRuntime);
    }

    [Fact]
    public void OK_keeps_the_references_in_their_order_whichever_comes_first()
    {
        var project = Project(ScriptingRuntime, OleAutomation);

        Dialog(project).Apply(project);

        project.References.Should().Equal(ScriptingRuntime, OleAutomation);
    }

    [Fact]
    public void Three_fixed_rows_head_the_list_and_none_is_a_reference_of_the_project()
    {
        var vm = Dialog(Project(OleAutomation));

        vm.References.Take(3).Select(r => r.Name).Should().Equal(
            "Visual Basic For Applications",
            "Visual Basic runtime objects and procedures",
            "Visual Basic objects and procedures");
        vm.References.Take(3).Should().AllSatisfy(r =>
        {
            r.IsBuiltIn.Should().BeTrue();
            r.IsChecked.Should().BeTrue();
            r.Guid.Should().NotBe(OleAutomation.Guid, "a fixed row standing in for OLE Automation is what hid it");
        });
    }

    [Fact]
    public void The_projects_OLE_Automation_reference_is_an_ordinary_checked_row()
    {
        var vm = Dialog(Project(OleAutomation, ScriptingRuntime));

        vm.References.Skip(3).Take(2).Select(r => (r.Name, r.IsChecked, r.IsBuiltIn))
            .Should().Equal(("OLE Automation", true, false), ("Microsoft Scripting Runtime", true, false));
    }

    [Fact]
    public void Unchecking_OLE_Automation_takes_it_out_on_OK()
    {
        var project = Project(OleAutomation, ScriptingRuntime);
        var vm = Dialog(project);

        vm.References.Single(r => r.Name == "OLE Automation").IsChecked = false;
        vm.Apply(project);

        project.References.Should().Equal(ScriptingRuntime);
    }

    [Fact]
    public void A_library_the_project_already_references_is_listed_once()
    {
        var vm = Dialog(Project(OleAutomation),
            new AvailableReference(OleAutomation.Guid, "2.0", "OLE Automation", @"C:\Windows\SysWOW64\stdole2.tlb"),
            new AvailableReference(ScriptingRuntime.Guid, "1.0", "Microsoft Scripting Runtime", null));

        vm.References.Count(r => r.Name == "OLE Automation").Should().Be(1);
        vm.References.Single(r => r.Name == "Microsoft Scripting Runtime").IsChecked.Should().BeFalse();
    }

    [Fact]
    public void Nothing_moves_above_the_fixed_rows()
    {
        var project = Project(OleAutomation, ScriptingRuntime);
        var vm = Dialog(project);

        vm.SelectedReference = vm.References.Single(r => r.Name == "OLE Automation");
        vm.MoveUpCommand.CanExecute(null).Should().BeFalse("VB6 disables Up on the first row below the fixed ones");
        vm.MoveDownCommand.CanExecute(null).Should().BeTrue();

        vm.SelectedReference = vm.References.Single(r => r.Name == "Microsoft Scripting Runtime");
        vm.MoveUpCommand.CanExecute(null).Should().BeTrue();
        vm.MoveUpCommand.Execute(null);
        vm.Apply(project);

        project.References.Should().Equal(ScriptingRuntime, OleAutomation);
    }

    [Fact]
    public void A_fixed_row_cannot_be_moved()
    {
        var vm = Dialog(Project(OleAutomation));

        vm.SelectedReference = vm.References[1];
        vm.MoveUpCommand.CanExecute(null).Should().BeFalse();
        vm.MoveDownCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void The_selection_starts_on_the_last_checked_row()
    {
        var vm = Dialog(Project(OleAutomation, ScriptingRuntime),
            new AvailableReference("{11111111-2222-3333-4444-555555555555}", "1.0", "Something Unchecked", null));

        vm.SelectedReference!.Name.Should().Be("Microsoft Scripting Runtime");
    }
}
