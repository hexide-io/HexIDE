using HexIDE.Forms.ViewModels.Options;
using HexIDE.IDE;

namespace HexIDE.Tests.ViewModels.Options;

public class FormDesignerGridPageViewModelTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    public FormDesignerGridPageViewModelTests()
    {
        _settings.ShowGrid.Returns(true);
        _settings.GridWidth.Returns(8);
        _settings.GridHeight.Returns(8);
        _settings.AlignToGrid.Returns(true);
    }

    [Fact]
    public void Load_PullsAllGridSettings()
    {
        _settings.GridWidth.Returns(12);
        _settings.GridHeight.Returns(16);
        _settings.AlignToGrid.Returns(false);

        var sut = new FormDesignerGridPageViewModel(_settings);

        sut.ShowGrid.Should().BeTrue();
        sut.GridWidth.Should().Be(12);
        sut.GridHeight.Should().Be(16);
        sut.AlignToGrid.Should().BeFalse();
    }

    [Fact]
    public void Save_WritesAllGridSettings()
    {
        var sut = new FormDesignerGridPageViewModel(_settings)
        {
            ShowGrid = false,
            GridWidth = 10,
            GridHeight = 20,
            AlignToGrid = false,
        };

        sut.SaveToSettings();

        _settings.Received().ShowGrid = false;
        _settings.Received().GridWidth = 10;
        _settings.Received().GridHeight = 20;
        _settings.Received().AlignToGrid = false;
    }

    [Fact]
    public void RestoreDefaults_ResetsToDefaults_WithoutWritingSettings()
    {
        var sut = new FormDesignerGridPageViewModel(_settings)
        {
            ShowGrid = false, GridWidth = 99, GridHeight = 99, AlignToGrid = false,
        };

        sut.RestoreDefaults();

        sut.ShowGrid.Should().Be(SettingsDefaults.ShowGrid);
        sut.GridWidth.Should().Be(SettingsDefaults.GridWidth);
        sut.GridHeight.Should().Be(SettingsDefaults.GridHeight);
        sut.AlignToGrid.Should().Be(SettingsDefaults.AlignToGrid);
        _settings.DidNotReceive().GridWidth = Arg.Any<int>();   // preview-only
    }

    [Fact]
    public void Title_IsGrid()
    {
        new FormDesignerGridPageViewModel(_settings).Title.Should().Be("Grid");
    }
}
