using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;

namespace HexIDE.Integration.Tests.Views;

public class FileConflictViewIntegrationTests
{
    [AvaloniaFact]
    public void View_WithConflictedFiles_RendersWithoutErrors()
    {
        var vm = new FileConflictViewModel();
        vm.Add("Form1");
        vm.Add("Module1");

        var view = new FileConflictView { DataContext = vm };
        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        view.Should().BeAssignableTo<UserControl>();
        view.IsMeasureValid.Should().BeTrue();
        view.IsArrangeValid.Should().BeTrue();
        vm.Files.Should().HaveCount(2);
    }

    [AvaloniaFact]
    public void View_WithNoFiles_RendersWithoutErrors()
    {
        var vm = new FileConflictViewModel();
        var view = new FileConflictView { DataContext = vm };

        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        view.IsMeasureValid.Should().BeTrue();
        vm.Files.Should().BeEmpty();
    }
}
