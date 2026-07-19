using HexIDE.Forms.ViewModels.Options;
using HexIDE.IDE;

namespace HexIDE.Tests.ViewModels.Options;

public class EditorFormattingPageViewModelTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    [Fact]
    public void Load_PullsTabWidth()
    {
        _settings.TabWidth.Returns(3);

        new EditorFormattingPageViewModel(_settings).TabWidth.Should().Be(3);
    }

    [Fact]
    public void Save_WritesTabWidth()
    {
        var sut = new EditorFormattingPageViewModel(_settings) { TabWidth = 8 };

        sut.SaveToSettings();

        _settings.Received().TabWidth = 8;
    }

    [Fact]
    public void Title_IsFormatting()
    {
        new EditorFormattingPageViewModel(_settings).Title.Should().Be("Formatting");
    }
}
