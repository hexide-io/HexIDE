using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.Forms.Views.Options;
using HexIDE.IDE;
using NSubstitute;

namespace HexIDE.Integration.Tests.Views;

public class EnvironmentGeneralPageViewIntegrationTests
{
    [AvaloniaFact]
    public void Page_RendersBothGroups_AndLoadsReloadSetting()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.ReloadFilesChangedOutsideIde.Returns(true);

        var vm = new EnvironmentGeneralPageViewModel(settings);
        var view = new EnvironmentGeneralPageView { DataContext = vm };

        view.Measure(new Size(420, 300));
        view.Arrange(new Rect(0, 0, 420, 300));

        view.Should().BeAssignableTo<UserControl>();
        view.IsMeasureValid.Should().BeTrue();
        view.IsArrangeValid.Should().BeTrue();
        vm.ReloadFilesChangedOutsideIde.Should().BeTrue(); // loaded from settings
    }

    [AvaloniaFact]
    public void SaveToSettings_PersistsReloadToggle()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.ReloadFilesChangedOutsideIde.Returns(false);
        var vm = new EnvironmentGeneralPageViewModel(settings);

        vm.ReloadFilesChangedOutsideIde = true;
        vm.SaveToSettings();

        settings.Received().ReloadFilesChangedOutsideIde = true;
    }
}
