using HexIDE.IDE;
using HexIDE.Forms.ViewModels.Options;

namespace HexIDE.Tests;

public class BracketDepthTests
{
    [Fact]
    public void OptionLoadsSavesAndRestoresDefaults()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.ColorBracketsByDepth.Returns(true);
        var page = new EditorGeneralPageViewModel(settings);
        page.ColorBracketsByDepth.Should().BeTrue();
        page.ColorBracketsByDepth = false;
        page.SaveToSettings();
        settings.Received().ColorBracketsByDepth = false;
        page.RestoreDefaults();
        page.ColorBracketsByDepth.Should().BeTrue();
    }
}
