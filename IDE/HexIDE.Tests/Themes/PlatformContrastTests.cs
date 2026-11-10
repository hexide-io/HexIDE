using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using HexIDE.IDE;
using HexIDE.Themes;

namespace HexIDE.Tests.Themes;

public sealed class PlatformContrastTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hexide-contrast-" + Guid.NewGuid());
    public PlatformContrastTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [AvaloniaTheory]
    [InlineData(PlatformThemeVariant.Light, "HighContrastLight")]
    [InlineData(PlatformThemeVariant.Dark, "HighContrastDark")]
    public void StartupAndChangesFollowContrastWithoutSaving(PlatformThemeVariant variant, string expected)
    {
        var settings = new SettingsService(Path.Combine(root, "settings.json"));
        var platform = Substitute.For<IPlatformSettings>();
        platform.GetColorValues().Returns(Colors(variant, true));
        var themes = new ThemeService(root);
        try
        {
            themes.Apply("Classic");
            themes.FollowPlatform(platform, settings, () => themes.Apply(settings.ActiveTheme));
            themes.ActiveTheme.Should().Be(expected);
            settings.ActiveTheme.Should().Be("Classic");
            settings.HasExplicitThemeChoice.Should().BeFalse();
            settings.TabWidth = 8;
            settings.Save();
            new SettingsService(Path.Combine(root, "settings.json")).HasExplicitThemeChoice.Should().BeFalse();
            platform.ColorValuesChanged += Raise.Event<EventHandler<PlatformColorValues>>(platform, Colors(variant, false));
            themes.ActiveTheme.Should().Be("Classic");
            platform.ColorValuesChanged += Raise.Event<EventHandler<PlatformColorValues>>(platform, Colors(PlatformThemeVariant.Dark, true));
            themes.ActiveTheme.Should().Be("HighContrastDark");
            platform.ColorValuesChanged += Raise.Event<EventHandler<PlatformColorValues>>(platform, Colors(PlatformThemeVariant.Light, true));
            themes.ActiveTheme.Should().Be("HighContrastLight");
        }
        finally { themes.Apply("Classic"); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitChoiceWinsAndPersists(bool savedBeforeStartup)
    {
        var path = Path.Combine(root, "settings.json");
        if (savedBeforeStartup) File.WriteAllText(path, """{"activeTheme":"Abyss"}""");
        var settings = new SettingsService(path);
        var platform = Substitute.For<IPlatformSettings>();
        platform.GetColorValues().Returns(Colors(PlatformThemeVariant.Dark, true));
        var themes = new ThemeService(root);
        try
        {
            themes.Apply(settings.ActiveTheme);
            themes.FollowPlatform(platform, settings, () => themes.Apply(settings.ActiveTheme));
            if (!savedBeforeStartup)
            {
                themes.ActiveTheme.Should().Be("HighContrastDark");
                settings.ActiveTheme = "Abyss";
                themes.Apply("Abyss");
                settings.Save();
            }
            themes.ActiveTheme.Should().Be("Abyss");
            platform.ColorValuesChanged += Raise.Event<EventHandler<PlatformColorValues>>(platform, Colors(PlatformThemeVariant.Light, false));
            platform.ColorValuesChanged += Raise.Event<EventHandler<PlatformColorValues>>(platform, Colors(PlatformThemeVariant.Light, true));
            themes.ActiveTheme.Should().Be("Abyss");
            new SettingsService(path).ActiveTheme.Should().Be("Abyss");
        }
        finally { themes.Apply("Classic"); }
    }

    [AvaloniaFact]
    public void OrdinaryDarkPreferenceLeavesClassicLight()
    {
        var settings = new SettingsService(Path.Combine(root, "settings.json"));
        var platform = Substitute.For<IPlatformSettings>();
        platform.GetColorValues().Returns(Colors(PlatformThemeVariant.Dark, false));
        var themes = new ThemeService(root);
        themes.Apply("Classic");
        themes.FollowPlatform(platform, settings, () => themes.Apply(settings.ActiveTheme));
        themes.ActiveTheme.Should().Be("Classic");
        Avalonia.Application.Current!.RequestedThemeVariant.Should().Be(Avalonia.Styling.ThemeVariant.Light);
    }

    [AvaloniaFact]
    public void OptionsReturningToOriginalChoiceStillPersistsExplicitChoice()
    {
        var path = Path.Combine(root, "settings.json");
        var settings = new SettingsService(path);
        var themes = new ThemeService(root);
        try
        {
            var page = new HexIDE.Forms.ViewModels.Options.ThemePageViewModel(settings, themes);
            page.SelectedTheme = "Dark";
            page.SelectedTheme = "Classic";
            page.SaveToSettings();
            settings.Save();
            new SettingsService(path).HasExplicitThemeChoice.Should().BeTrue();
        }
        finally { themes.Apply("Classic"); }
    }

    [AvaloniaFact]
    public void OptionsCanExplicitlyChooseClassicDuringAutomaticContrast()
    {
        var path = Path.Combine(root, "settings.json");
        var settings = new SettingsService(path);
        var themes = new ThemeService(root);
        try
        {
            themes.Apply("HighContrastDark");
            var page = new HexIDE.Forms.ViewModels.Options.ThemePageViewModel(settings, themes);
            page.SelectedTheme.Should().Be("HighContrastDark");
            page.SaveToSettings();
            settings.HasExplicitThemeChoice.Should().BeFalse();
            page.SelectedTheme = "Classic";
            page.SaveToSettings();
            settings.Save();
            new SettingsService(path).HasExplicitThemeChoice.Should().BeTrue();
            themes.ActiveTheme.Should().Be("Classic");
        }
        finally { themes.Apply("Classic"); }
    }

    [Fact]
    public void SavingUntouchedOptionsDoesNotDisableFollowing()
    {
        var settings = new SettingsService(Path.Combine(root, "settings.json"));
        var page = new HexIDE.Forms.ViewModels.Options.ThemePageViewModel(settings, Substitute.For<IThemeService>());
        page.SaveToSettings();
        settings.HasExplicitThemeChoice.Should().BeFalse();
    }

    private static PlatformColorValues Colors(PlatformThemeVariant variant, bool high) => new()
    {
        ThemeVariant = variant,
        ContrastPreference = high ? ColorContrastPreference.High : ColorContrastPreference.NoPreference
    };
}
