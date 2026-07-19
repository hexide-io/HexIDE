using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

public class SaveChangesViewIntegrationTests
{
    [AvaloniaFact]
    public void View_WithEmptyChangedFiles_RendersWithoutErrors()
    {
        var vm = new SaveChangesViewModel();
        var view = new SaveChangesView { DataContext = vm };

        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        view.Should().NotBeNull();
        view.Should().BeAssignableTo<UserControl>();
        vm.ChangedFiles.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void View_WithPopulatedChangedFiles_RendersWithoutErrors()
    {
        var vm = new SaveChangesViewModel();
        var projectDef = new ProjectDefinition(VBProjectType.EXE, "TestProject");
        var formDef = new FormDefinition(projectDef, FormComponentClass.Instance, "Form1");
        projectDef.AddForm(formDef);

        vm.Add(projectDef);
        vm.Add(formDef);

        var view = new SaveChangesView { DataContext = vm };

        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        vm.ChangedFiles.Should().HaveCount(2);
        view.IsMeasureValid.Should().BeTrue();
        view.IsArrangeValid.Should().BeTrue();
    }

    [AvaloniaFact]
    public void View_WithMultipleForms_RendersWithoutErrors()
    {
        var vm = new SaveChangesViewModel();
        var projectDef = new ProjectDefinition(VBProjectType.EXE, "TestProject");
        var form1 = new FormDefinition(projectDef, FormComponentClass.Instance, "Form1");
        var form2 = new FormDefinition(projectDef, FormComponentClass.Instance, "Form2");
        projectDef.AddForm(form1);
        projectDef.AddForm(form2);

        vm.Add(projectDef);
        vm.Add(form1);
        vm.Add(form2);

        var view = new SaveChangesView { DataContext = vm };

        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        vm.ChangedFiles.Should().HaveCount(3);
    }
}
