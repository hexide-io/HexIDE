using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;

namespace HexIDE.Integration.Tests.Views;

public class AddProcedureViewIntegrationTests
{
    [AvaloniaFact]
    public void View_IsUserControl()
    {
        var view = new AddProcedureView();

        view.Should().NotBeNull();
        view.Should().BeAssignableTo<UserControl>();
    }

    [AvaloniaFact]
    public void View_WithDataContext_RendersWithoutErrors()
    {
        var vm = new AddProcedureViewModel();
        var view = new AddProcedureView { DataContext = vm };

        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        view.IsMeasureValid.Should().BeTrue();
        view.IsArrangeValid.Should().BeTrue();
    }

    [AvaloniaFact]
    public void View_SettingNameOnViewModel_DoesNotThrow()
    {
        var vm = new AddProcedureViewModel();
        var view = new AddProcedureView { DataContext = vm };

        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        vm.Name = "TestProcedure";

        view.InvalidateMeasure();
        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        vm.Name.Should().Be("TestProcedure");
    }

    [AvaloniaFact]
    public void View_DefaultViewModel_HasExpectedDefaults()
    {
        var vm = new AddProcedureViewModel();
        var view = new AddProcedureView { DataContext = vm };

        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        vm.IsSub.Should().BeTrue();
        vm.IsPublic.Should().BeTrue();
        vm.Name.Should().BeEmpty();
    }
}
