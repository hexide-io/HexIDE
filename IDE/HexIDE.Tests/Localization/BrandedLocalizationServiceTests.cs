using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia.Platform;
using HexIDE.Localization;

namespace HexIDE.Tests.Localization;

public class BrandedLocalizationServiceTests
{
    private static LocalizationService Branded() =>
        new(identity: new DistributionIdentity("acme", "Acme Studio"));

    [AvaloniaTheory]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("ja")]
    [InlineData("en-GB")]
    [InlineData("fr-CA")]
    public void BrandTemplates_ResolveThroughTheLanguageChain(string language)
    {
        var stock = new LocalizationService();
        var branded = Branded();
        branded.Apply(language);

        // Use every shipped key so the upstream exceptions and assembly-name exclusions are covered too.
        using var stream = AssetLoader.Open(new Uri("avares://HexIDE/Localization/Packs/en.json"));
        var keys = JsonSerializer.Deserialize(stream, LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!.Keys;
        var expected = keys.ToDictionary(k => k, k => stock.GetStringFrom(language, k));
        BrandText.Apply(expected, "Acme Studio");

        expected.Values.Should().Contain(v => v.Contains("Acme Studio", StringComparison.Ordinal));
        foreach (var (key, value) in expected)
        {
            branded.GetString(key).Should().Be(value, key);
            branded.GetStringFrom(language, key).Should().Be(value, key);
        }
        branded.GetString(BrandText.UpstreamCreditsKey).Should().Contain("HexIDE");
    }

    [AvaloniaFact]
    public void BrandTemplates_SurviveALiveLanguageSwitch()
    {
        var service = Branded();
        var observed = new List<string>();
        service.LanguageChanged += () => observed.Add(service.GetString("Str.Menu.Help.About"));

        foreach (var language in new[] { "de", "ja", "en-GB", "fr" })
        {
            service.Apply(language);
            service.ActiveLanguage.Should().Be(language);
            service.GetString(BrandText.UpstreamCreditsKey).Should().Contain("HexIDE").And.NotContain("Acme Studio");
        }

        observed.Should().HaveCount(4).And.OnlyContain(s => s.Contains("Acme Studio"));
        observed.Distinct().Count().Should().BeGreaterThan(1);
    }

    [AvaloniaFact]
    public void BrandTemplates_AreAppliedBeforePseudoLocalization()
    {
        var stock = new LocalizationService();
        var expected = new Dictionary<string, string> { ["Str.Menu.Help.About"] = stock.GetStringFrom("en", "Str.Menu.Help.About") };
        BrandText.Apply(expected, "Acme Studio");
        var service = Branded();

        service.Apply("pseudo");

        service.GetString("Str.Menu.Help.About").Should().Be(
            PseudoLocalizer.Transform(expected, false)["Str.Menu.Help.About"]);
    }
}
