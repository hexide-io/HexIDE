using HexIDE.Forms.ViewModels.Options;
using HexIDE.IDE;

namespace HexIDE.Tests.ViewModels.Options;

public class AdvancedLspPageViewModelTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    [Fact]
    public void Load_PullsUrl()
    {
        _settings.LspWebSocketUrl.Returns("ws://localhost:8123/");
        new AdvancedLspPageViewModel(_settings).LspWebSocketUrl.Should().Be("ws://localhost:8123/");
    }

    [Fact]
    public void Save_TrimsUrl()
    {
        var sut = new AdvancedLspPageViewModel(_settings) { LspWebSocketUrl = "  ws://x/  " };
        sut.SaveToSettings();
        _settings.Received().LspWebSocketUrl = "ws://x/";
    }

    [Fact]
    public void Save_BlankBecomesNull()
    {
        var sut = new AdvancedLspPageViewModel(_settings) { LspWebSocketUrl = "" };
        sut.SaveToSettings();
        _settings.Received().LspWebSocketUrl = null;
    }

    [Fact]
    public void RestoreDefaults_ResetsToNull()
    {
        var sut = new AdvancedLspPageViewModel(_settings) { LspWebSocketUrl = "ws://x/" };

        sut.RestoreDefaults();

        sut.LspWebSocketUrl.Should().Be(SettingsDefaults.LspWebSocketUrl);   // null
    }

    [Fact]
    public void Title_IsLsp()
    {
        new AdvancedLspPageViewModel(_settings).Title.Should().Be("LSP");
    }
}
