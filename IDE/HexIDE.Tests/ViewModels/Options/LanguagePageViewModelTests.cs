using System;
using System.Linq;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Tests.ViewModels.Options;

public class LanguagePageViewModelTests
{
    private static readonly LanguageInfo System = new("system", "System default", "ltr");
    private static readonly LanguageInfo En = new("en", "English", "ltr");
    private static readonly LanguageInfo Fr = new("fr", "French", "ltr");

    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly ILanguageSwitchService _switch = Substitute.For<ILanguageSwitchService>();
    private readonly ILocalizationService _loc = Substitute.For<ILocalizationService>();

    public LanguagePageViewModelTests()
    {
        _settings.ActiveLanguage.Returns("en");
        _switch.ActiveLanguage.Returns("en");
        _switch.AvailableLanguages.Returns([System, En, Fr]);
        _switch.RegionsFor("en").Returns([new RegionInfo("en-US", "United States"), new RegionInfo("en-GB", "United Kingdom")]);
        _switch.RegionsFor("fr").Returns([new RegionInfo("fr-FR", "France"), new RegionInfo("fr-BE", "Belgium")]);
        _switch.RegionsFor(Arg.Is<string>(s => s != "en" && s != "fr")).Returns([]);
        _switch.SwitchWithGateAsync(Arg.Any<string>()).Returns(true);
        _loc.GetString("Str.Options.Language.RegionStandard").Returns("(Standard)");
    }

    private LanguagePageViewModel CreateSut() => new(_settings, _switch, _loc, () => { });

    [Fact]
    public void Load_SelectsLanguage_AndStandardRegion_WithoutSwitching()
    {
        var sut = CreateSut();

        sut.SelectedLanguage.Should().Be(En);
        sut.IsRegionEnabled.Should().BeTrue();
        sut.AvailableRegions[0].Id.Should().Be("en");                                  // "(Standard)" pinned first
        sut.AvailableRegions.Select(r => r.Id).Should().Contain(["en-US", "en-GB"]);   // regions follow
        sut.SelectedRegion!.Id.Should().Be("en");                                       // (Standard)
        _switch.DidNotReceive().SwitchWithGateAsync(Arg.Any<string>());
    }

    [Fact]
    public void Load_RegionId_SelectsThatRegion()
    {
        _settings.ActiveLanguage.Returns("en-GB");

        var sut = CreateSut();

        sut.SelectedLanguage.Should().Be(En);
        sut.SelectedRegion!.Id.Should().Be("en-GB");
    }

    [Fact]
    public void ChangingLanguage_RunsGate_OnTheNeutralId_AndRebuildsRegions()
    {
        var sut = CreateSut();

        sut.SelectedLanguage = Fr;

        _switch.Received(1).SwitchWithGateAsync("fr");          // gate fires on the language (Standard)
        sut.AvailableRegions[0].Id.Should().Be("fr");          // "(Standard)" first
        sut.AvailableRegions.Select(r => r.Id).Should().Contain(["fr-FR", "fr-BE"]);
        sut.SelectedRegion!.Id.Should().Be("fr");              // reset to (Standard)
    }

    [Fact]
    public void AvailableRegions_AreAlphaSorted_AfterStandard()
    {
        // (Standard) is pinned first; the rest are A–Z by display name (mock fr = France, Belgium).
        var sut = CreateSut();
        sut.SelectedLanguage = Fr;

        sut.AvailableRegions[0].Id.Should().Be("fr");
        sut.AvailableRegions.Skip(1).Select(r => r.DisplayName)
            .Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        sut.AvailableRegions.Skip(1).Select(r => r.Id).Should().Equal("fr-BE", "fr-FR"); // Belgium < France
    }

    [Fact]
    public void ChangingRegion_AppliesSilently_NoGate()
    {
        var sut = CreateSut();

        sut.SelectedRegion = sut.AvailableRegions.First(r => r.Id == "en-GB");

        _switch.Received(1).Apply("en-GB");                     // applied live...
        _switch.DidNotReceive().SwitchWithGateAsync("en-GB");  // ...but no gate (can't strand a reader)
    }

    [Fact]
    public void RevertedLanguageSwitch_SnapsBackToActiveLanguage()
    {
        _switch.SwitchWithGateAsync("fr").Returns(false);      // reverted / timed out
        _switch.ActiveLanguage.Returns("en");
        var sut = CreateSut();

        sut.SelectedLanguage = Fr;

        sut.SelectedLanguage.Should().Be(En);                  // snapped back
        sut.SelectedRegion!.Id.Should().Be("en");
    }

    [Fact]
    public void SelectingSystem_DisablesRegionCombo()
    {
        var sut = CreateSut();

        sut.SelectedLanguage = System;

        sut.IsRegionEnabled.Should().BeFalse();
        sut.AvailableRegions.Should().BeEmpty();
        sut.SelectedRegion.Should().BeNull();
    }

    [Fact]
    public void Save_WritesEffectiveId_ComposedFromBothCombos()
    {
        var sut = CreateSut();
        sut.SelectedLanguage = Fr;
        sut.SelectedRegion = sut.AvailableRegions.First(r => r.Id == "fr-BE");

        sut.SaveToSettings();

        _settings.Received().ActiveLanguage = "fr-BE";
    }

    [Fact]
    public void Save_StandardRegion_WritesTheNeutralId()
    {
        var sut = CreateSut();           // en + (Standard)
        sut.SaveToSettings();
        _settings.Received().ActiveLanguage = "en";
    }

    [Fact]
    public void RestoreDefaults_AppliesDefaultSilently_NoGate()
    {
        var sut = CreateSut();
        sut.SelectedLanguage = Fr;

        sut.RestoreDefaults();

        sut.SelectedLanguage!.Id.Should().Be(SettingsDefaults.ActiveLanguage);
        _switch.Received().Apply(SettingsDefaults.ActiveLanguage);
        _switch.DidNotReceive().SwitchWithGateAsync(SettingsDefaults.ActiveLanguage);
    }

    [Fact]
    public void AvailableLanguages_ComeFromService()
    {
        CreateSut().AvailableLanguages.Should().Equal(System, En, Fr);
    }

    [Fact]
    public void Title_IsLanguage()
    {
        CreateSut().Title.Should().Be("Language");
    }
}
