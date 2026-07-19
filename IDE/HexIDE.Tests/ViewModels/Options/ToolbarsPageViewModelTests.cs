using HexIDE.Forms.ViewModels.Options;
using HexIDE.IDE;

namespace HexIDE.Tests.ViewModels.Options;

public class ToolbarsPageViewModelTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    [Fact]
    public void Load_PullsAllToolbarFlags()
    {
        _settings.IsStandardToolbarVisible.Returns(true);
        _settings.IsEditToolbarVisible.Returns(false);
        _settings.IsDebugToolbarVisible.Returns(true);
        _settings.IsFormEditorToolbarVisible.Returns(false);

        var sut = new ToolbarsPageViewModel(_settings);

        sut.StandardToolbarVisible.Should().BeTrue();
        sut.EditToolbarVisible.Should().BeFalse();
        sut.DebugToolbarVisible.Should().BeTrue();
        sut.FormEditorToolbarVisible.Should().BeFalse();
    }

    [Fact]
    public void Save_WritesAllToolbarFlags()
    {
        var sut = new ToolbarsPageViewModel(_settings)
        {
            StandardToolbarVisible = false,
            EditToolbarVisible = true,
            DebugToolbarVisible = true,
            FormEditorToolbarVisible = true,
        };

        sut.SaveToSettings();

        _settings.Received().IsStandardToolbarVisible = false;
        _settings.Received().IsEditToolbarVisible = true;
        _settings.Received().IsDebugToolbarVisible = true;
        _settings.Received().IsFormEditorToolbarVisible = true;
    }

    [Fact]
    public void RestoreDefaults_ResetsAllFlags()
    {
        var sut = new ToolbarsPageViewModel(_settings)
        {
            StandardToolbarVisible = false,
            EditToolbarVisible = true,
            DebugToolbarVisible = true,
            FormEditorToolbarVisible = true,
        };

        sut.RestoreDefaults();

        sut.StandardToolbarVisible.Should().Be(SettingsDefaults.IsStandardToolbarVisible);
        sut.EditToolbarVisible.Should().Be(SettingsDefaults.IsEditToolbarVisible);
        sut.DebugToolbarVisible.Should().Be(SettingsDefaults.IsDebugToolbarVisible);
        sut.FormEditorToolbarVisible.Should().Be(SettingsDefaults.IsFormEditorToolbarVisible);
    }

    [Fact]
    public void Title_IsToolbars()
    {
        new ToolbarsPageViewModel(_settings).Title.Should().Be("Toolbars");
    }
}
