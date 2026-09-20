using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

public class ProjectPropertiesViewIntegrationTests
{

    /// <summary>
    /// A validator that objects to nothing. These tests are about the dialog's other fields; the naming
    /// rules have their own, in <c>ProjectNameRefusalTests</c>.
    /// </summary>
    private static string? NoNameObjection(string _) => null;
    [AvaloniaFact]
    public void View_WithProjectDefinition_RendersWithoutErrors()
    {
        var projectDef = new ProjectDefinition(VBProjectType.EXE, "TestProject");
        var vm = new ProjectPropertiesViewModel(projectDef, NoNameObjection);
        var view = new ProjectPropertiesView { DataContext = vm };

        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        view.Should().NotBeNull();
        view.Should().BeAssignableTo<UserControl>();
        view.IsMeasureValid.Should().BeTrue();
        view.IsArrangeValid.Should().BeTrue();
    }

    [AvaloniaFact]
    public void View_WithProjectContainingForms_RendersWithoutErrors()
    {
        var projectDef = new ProjectDefinition(VBProjectType.EXE, "TestProject");
        var form1 = new FormDefinition(projectDef, FormComponentClass.Instance, "Form1");
        var form2 = new FormDefinition(projectDef, FormComponentClass.Instance, "Form2");
        projectDef.AddForm(form1);
        projectDef.AddForm(form2);

        var vm = new ProjectPropertiesViewModel(projectDef, NoNameObjection);
        var view = new ProjectPropertiesView { DataContext = vm };

        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        vm.ProjectName.Should().Be("TestProject");
        // Sub Main plus the two forms. The list gained a non-form entry when Sub Main became a
        // selectable startup object (#210); this test is about the view rendering, so what matters here
        // is that every entry is present — ProjectPropertiesViewModelTests and SubMainStartupObjectTests
        // own the ordering and selection rules.
        vm.StartupObjects.Should().HaveCount(3);
        vm.StartupObjects.Select(o => o.Header).Should().Equal("Sub Main", "Form1", "Form2");
    }

    [AvaloniaFact]
    public void View_ProjectNameBindsCorrectly()
    {
        var projectDef = new ProjectDefinition(VBProjectType.EXE, "OriginalName");
        var vm = new ProjectPropertiesViewModel(projectDef, NoNameObjection);
        var view = new ProjectPropertiesView { DataContext = vm };

        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        vm.ProjectName.Should().Be("OriginalName");
        vm.SelectedProjectType.Should().Be(VBProjectType.EXE);
    }
}
