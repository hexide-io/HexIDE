using HexIDE.Forms.ViewModels.Options;
using HexIDE.IDE;
using HexIDE.Themes;

namespace HexIDE.Tests.ViewModels.Options;

public class ThemePageViewModelTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IThemeService _theme = Substitute.For<IThemeService>();

    public ThemePageViewModelTests()
    {
        _settings.ActiveTheme.Returns("Classic");
        _theme.AvailableThemes.Returns(["Classic", "Dark", "Abyss"]);
    }

    private ThemePageViewModel CreateSut() => new(_settings, _theme);

    [Fact]
    public void Load_PullsActiveThemeWithoutApplying()
    {
        var sut = CreateSut();

        sut.SelectedTheme.Should().Be("Classic");
        _theme.DidNotReceive().Apply(Arg.Any<string>());
    }

    [Fact]
    public void ChangingTheme_AppliesLive()
    {
        var sut = CreateSut();

        sut.SelectedTheme = "Dark";

        _theme.Received(1).Apply("Dark");
    }

    [Fact]
    public void Save_WritesSelectedThemeToSettings()
    {
        var sut = CreateSut();
        sut.SelectedTheme = "Abyss";

        sut.SaveToSettings();

        _settings.Received().ActiveTheme = "Abyss";
    }

    [Fact]
    public void AvailableThemes_ComeFromService()
    {
        CreateSut().AvailableThemes.Should().Equal("Classic", "Dark", "Abyss");
    }

    [Fact]
    public void RestoreDefaults_SetsDefaultThemeAndAppliesLive()
    {
        var sut = CreateSut();
        sut.SelectedTheme = "Dark";

        sut.RestoreDefaults();

        sut.SelectedTheme.Should().Be(SettingsDefaults.ActiveTheme);
        _theme.Received().Apply(SettingsDefaults.ActiveTheme);
    }

    [Fact]
    public void Title_IsTheme()
    {
        CreateSut().Title.Should().Be("Theme");
    }
}
