using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using HexIDE.IDE;
using HexIDE.Profiles;
using HexIDE.Themes;
using HexIDE.Tests.Sidecar;

namespace HexIDE.Tests.Profiles;

public sealed class ProfileThemeTests : IDisposable
{
    private readonly string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "hexide-profile-theme-" + Guid.NewGuid());
    private string SettingsPath => Path.Combine(root, "user-settings.json");

    public ProfileThemeTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    private ProfileSnapshot Profile(string colors = "\"ReadOnlyText\":\"#123456\"", string variant = "Dark")
    {
        File.WriteAllText(Path.Combine(root, "profile.json"),
            """{"schemaVersion":1,"id":"custom","theme":"theme.json","settingsDefaults":"defaults.json"}""");
        File.WriteAllText(Path.Combine(root, "defaults.json"), """{"activeTheme":"Abyss"}""");
        File.WriteAllText(Path.Combine(root, "theme.json"),
            $$$"""{"name":"Custom","themeVariant":"{{{variant}}}","colors":{ {{{colors}}} }}""");
        return new ProfileResolver(root, root).Resolve(root)!;
    }

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void PayloadOverridesProfileSettingAndUsesSnapshot(string variant)
    {
        var profile = Profile(variant: variant);
        File.WriteAllText(Path.Combine(root, "theme.json"), "{}");
        var settings = new SettingsService(SettingsPath, profile);
        var themes = new ThemeService();
        try
        {
            themes.Apply(profile, settings);
            themes.ActiveTheme.Should().Be("Custom");
            Application.Current!.RequestedThemeVariant.Should().Be(variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);
            ((SolidColorBrush)((ResourceDictionary)Application.Current.Resources.MergedDictionaries.Last())["ReadOnlyTextBrush"]!)
                .Color.Should().Be(Color.Parse("#123456"));
            settings.Save();
            File.Exists(SettingsPath).Should().BeFalse();
            settings.ActiveTheme = "Classic";
            themes.Apply(profile, settings);
            themes.ActiveTheme.Should().Be("Classic");
            settings.Save();
            themes.Apply(profile, settings);
            themes.ActiveTheme.Should().Be("Classic", "saving must retain explicit-choice provenance");
        }
        finally { themes.Apply("Classic"); }
    }

    [AvaloniaTheory]
    [InlineData("Classic")]
    [InlineData("Dark")]
    public void ExplicitSavedChoiceWinsEvenWhenItEqualsHostDefault(string choice)
    {
        File.WriteAllText(SettingsPath, $$"""{"activeTheme":"{{choice}}"}""");
        var profile = Profile();
        var themes = new ThemeService();
        try
        {
            themes.Apply(profile, new SettingsService(SettingsPath, profile));
            themes.ActiveTheme.Should().Be(choice);
            Application.Current!.RequestedThemeVariant.Should().Be(choice == "Classic" ? ThemeVariant.Light : ThemeVariant.Dark);
        }
        finally { themes.Apply("Classic"); }
    }

    [AvaloniaFact]
    public void UnrelatedSavedSettingDoesNotSuppressPayload()
    {
        File.WriteAllText(SettingsPath, """{"tabWidth":8}""");
        var profile = Profile();
        var themes = new ThemeService();
        try
        {
            themes.Apply(profile, new SettingsService(SettingsPath, profile));
            themes.ActiveTheme.Should().Be("Custom");
        }
        finally { themes.Apply("Classic"); }
    }

    [AvaloniaFact]
    public void UnknownColourIsSkippedButKnownColourApplies()
    {
        using var log = new CapturedLog();
        var profile = Profile("\"Unknown\":\"not-a-colour\",\"ReadOnlyText\":\"#123456\"");
        var themes = new ThemeService();
        try
        {
            themes.Apply(profile, new SettingsService(SettingsPath, profile));
            themes.ActiveTheme.Should().Be("Custom");
            var dictionary = (ResourceDictionary)Application.Current!.Resources.MergedDictionaries.Last();
            dictionary.ContainsKey("Unknown").Should().BeFalse();
            log.Messages.Should().Contain(message => message.Contains("Unknown") && message.Contains(root)
                && message.Contains("theme.json"));
            ((SolidColorBrush)dictionary["ReadOnlyTextBrush"]!).Color.Should().Be(Color.Parse("#123456"));
        }
        finally { themes.Apply("Classic"); }
    }

    [AvaloniaFact]
    public void InvalidKnownColourRejectsWholeBundleBeforeApplication()
    {
        var themes = new ThemeService();
        try
        {
            themes.Apply("Dark");
            var dictionaries = Application.Current!.Resources.MergedDictionaries.ToArray();
            Action load = () => Profile("\"ReadOnlyText\":\"#123456\",\"Window\":\"not-a-colour\"");
            var error = load.Should().Throw<ProfileLoadException>().Which;
            error.File.Should().Be("theme.json");
            error.SourceId.Should().Be(root);
            error.Error.Should().Be(ProfileError.InvalidPayload);
            Application.Current.Resources.MergedDictionaries.Should().Equal(dictionaries);
            themes.ActiveTheme.Should().Be("Dark");
            Application.Current.RequestedThemeVariant.Should().Be(ThemeVariant.Dark);
        }
        finally { themes.Apply("Classic"); }
    }
}
