using System.IO;
using System.Text.Json;
using HexIDE.Localization;
using HexIDE.Profiles;
using HexIDE.Tests.Infrastructure;
using HexIDE.Tests.Sidecar;
using HexIDE.Tools.TranslationEditor;

namespace HexIDE.Tests.Localization;

/// <summary>
/// The lines naming HexIDE's signing keys always show the shipped text. A profile (a brand's <c>defaultProfile</c> is
/// one), the user's translations and any later override tier are ignored for them, as for HexIDE's credits, so a
/// product built on HexIDE can't present HexIDE's keys as its own beside the real fingerprints (#4060).
/// </summary>
public sealed class TrustLabelProtectionTests : IDisposable
{
    private const string Relabel = "Acme's own key";

    private readonly string root = ResolvedTemp.CreateDirectory("hexide-trust-labels-");

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
    }

    public static TheoryData<string, string> TrustLabelsInEachLanguage()
    {
        var data = new TheoryData<string, string>();
        foreach (var key in BrandText.TrustLabelKeys)
            foreach (var language in new[] { "en", "de" })
                data.Add(key, language);
        return data;
    }

    [Theory]
    [MemberData(nameof(TrustLabelsInEachLanguage))]
    public void A_profile_cannot_relabel_a_trust_label(string key, string language)
    {
        var shipped = new LocalizationService().GetStringFrom(language, key);
        var relabel = RelabelFor(key);

        var service = new LocalizationService(Profile(new() { [language] = new() { [key] = relabel } }));

        service.GetStringFrom(language, key).Should().Be(shipped).And.NotBe(relabel);
    }

    [Theory]
    [MemberData(nameof(TrustLabelsInEachLanguage))]
    public void The_users_translations_cannot_relabel_a_trust_label(string key, string language)
    {
        var shipped = new LocalizationService().GetStringFrom(language, key);
        var relabel = RelabelFor(key);

        var service = new LocalizationService(Overrides(language, key, relabel));

        service.GetStringFrom(language, key).Should().Be(shipped).And.NotBe(relabel);
    }

    [Theory]
    [MemberData(nameof(TrustLabelsInEachLanguage))]
    public void The_relabel_these_tests_try_would_apply_to_an_unprotected_key(string key, string language)
    {
        // The profile tier drops an override that loses a placeholder, so a relabel without them would pass the
        // tests above whether or not the key is protected. Carrying the English placeholders keeps them honest.
        var relabel = RelabelFor(key);
        var english = new LocalizationService().GetStringFrom("en", key);

        UserTranslationsService.PreservesPlaceholders(new Dictionary<string, string> { [key] = english }, key, relabel)
            .Should().BeTrue(language);
    }

    [Fact]
    public void A_neutral_languages_override_cannot_relabel_its_region()
    {
        const string key = "Str.Dialog.About.SigningKeys.RootKey";
        var shipped = new LocalizationService().GetStringFrom("fr-CA", key);

        var service = new LocalizationService(Profile(new() { ["fr"] = new() { [key] = Relabel } }));

        service.GetStringFrom("fr-CA", key).Should().Be(shipped).And.NotBe(Relabel);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void A_profile_cannot_replace_HexIDEs_credits(string language)
    {
        var shipped = new LocalizationService().GetStringFrom(language, BrandText.UpstreamCreditsKey);

        var service = new LocalizationService(Profile(new() { [language] = new() { [BrandText.UpstreamCreditsKey] = "A brand's own thanks" } }));

        service.GetStringFrom(language, BrandText.UpstreamCreditsKey).Should().Be(shipped);
    }

    [Fact]
    public void A_profile_override_of_any_other_key_still_applies()
    {
        var service = new LocalizationService(Profile(new() { ["de"] = new() { ["Str.Dialog.TrustChain.Publisher"] = "Herausgeberin" } }));

        service.GetStringFrom("de", "Str.Dialog.TrustChain.Publisher").Should().Be("Herausgeberin");
    }

    [Fact]
    public void The_ignored_override_is_logged_with_where_it_came_from()
    {
        const string key = "Str.Dialog.TrustChain.Root";
        using var log = new CapturedLog();

        new LocalizationService(Profile(new() { ["de"] = new() { [key] = Relabel } }), Overrides("de", key, Relabel))
            .GetStringFrom("de", key);

        log.Messages.Should().Contain(m => m.Contains(key, StringComparison.Ordinal)
            && m.Contains("profile 'custom' (de)", StringComparison.Ordinal)
            && m.Contains("the user's translations (de)", StringComparison.Ordinal)
            && m.Contains("can't be relabeled", StringComparison.Ordinal));
    }

    [Fact]
    public void The_translation_editor_shows_every_trust_label_read_only_and_says_why()
    {
        foreach (var key in BrandText.TrustLabelKeys)
        {
            var row = new TranslationRowViewModel(key);
            row.IsProtected.Should().BeTrue(key);
            row.ProtectedReasonKey.Should().Be("Str.TranslationEditor.ProtectedTrust", key);
        }

        new TranslationRowViewModel(BrandText.UpstreamCreditsKey).ProtectedReasonKey.Should().Be("Str.TranslationEditor.ProtectedCredits");
        new TranslationRowViewModel("Str.Dialog.TrustChain.Publisher").IsProtected.Should().BeFalse();
    }

    [Fact]
    public void Every_protected_key_and_its_reason_is_a_shipped_key()
    {
        var english = new LocalizationService();
        foreach (var key in BrandText.ProtectedKeys.Append("Str.TranslationEditor.ProtectedTrust"))
            english.GetStringFrom("en", key).Should().NotBe(key, "{0} must exist in en.json, or protecting it guards nothing", key);
    }

    [Fact]
    public void The_reason_keeps_HexIDEs_name_in_a_brands_build()
    {
        var strings = new Dictionary<string, string>
        {
            ["Str.TranslationEditor.ProtectedTrust"] = new LocalizationService().GetStringFrom("en", "Str.TranslationEditor.ProtectedTrust"),
        };

        BrandText.Apply(strings, "Acme Studio");

        strings["Str.TranslationEditor.ProtectedTrust"].Should().Contain("HexIDE").And.NotContain("Acme");
    }

    /// <summary>A brand's label for <paramref name="key"/>, keeping every placeholder the English text has.</summary>
    private static string RelabelFor(string key)
    {
        var english = new LocalizationService().GetStringFrom("en", key);
        var placeholders = System.Text.RegularExpressions.Regex.Matches(english, @"\{\d+\}").Select(m => m.Value).Distinct();
        return string.Join(" ", [Relabel, .. placeholders]);
    }

    // ── #4121: what tells a person whether to trust an add-in ──────────

    public static TheoryData<string, string> TrustVerdictsInEachLanguage()
    {
        var data = new TheoryData<string, string>();
        foreach (var key in BrandText.TrustVerdictKeys)
            foreach (var language in new[] { "en", "de" })
                data.Add(key, language);
        return data;
    }

    [Theory]
    [MemberData(nameof(TrustVerdictsInEachLanguage))]
    public void A_profile_cannot_change_a_trust_verdict(string key, string language)
    {
        var shipped = new LocalizationService().GetStringFrom(language, key);
        var relabel = RelabelFor(key);

        var service = new LocalizationService(Profile(new() { [language] = new() { [key] = relabel } }));

        service.GetStringFrom(language, key).Should().Be(shipped).And.NotBe(relabel);
    }

    [Theory]
    [MemberData(nameof(TrustVerdictsInEachLanguage))]
    public void The_users_own_translation_of_a_trust_verdict_still_applies(string key, string language)
    {
        var mine = RelabelFor(key);

        var service = new LocalizationService(Overrides(language, key, mine));

        service.GetStringFrom(language, key).Should().Be(mine);
    }

    [Fact]
    public void The_users_translation_wins_over_a_refused_profile()
    {
        const string key = "Str.Dialog.AddinConsent.Vm.Unsigned";

        var service = new LocalizationService(Profile(new() { ["de"] = new() { [key] = "Geprüft" } }), Overrides("de", key, "MEINE ÜBERSETZUNG"));

        service.GetStringFrom("de", key).Should().Be("MEINE ÜBERSETZUNG");
    }

    [Fact]
    public void A_neutral_languages_profile_cannot_change_its_regions_verdict()
    {
        const string key = "Str.Dialog.AddinConsent.UnsignedWarning";
        var shipped = new LocalizationService().GetStringFrom("fr-CA", key);

        var service = new LocalizationService(Profile(new() { ["fr"] = new() { [key] = RelabelFor(key) } }));

        service.GetStringFrom("fr-CA", key).Should().Be(shipped);
    }

    [Fact]
    public void A_refused_profile_is_logged_by_name()
    {
        const string key = "Str.Dialog.AddinConsent.Vm.Unsigned";
        using var log = new CapturedLog();

        new LocalizationService(Profile(new() { ["de"] = new() { [key] = "Geprüft" } })).GetStringFrom("de", key);

        log.Messages.Should().Contain(m => m.Contains(key, StringComparison.Ordinal)
            && m.Contains("profile 'custom' (de)", StringComparison.Ordinal)
            && !m.Contains("the user's translations", StringComparison.Ordinal)
            && m.Contains("whether to trust an add-in", StringComparison.Ordinal));
    }

    [Fact]
    public void Nothing_is_refused_or_logged_when_the_users_translation_wins_anyway()
    {
        const string key = "Str.Dialog.AddinConsent.Vm.Unsigned";
        using var log = new CapturedLog();

        new LocalizationService(Profile(new() { ["de"] = new() { [key] = "Geprüft" } }), Overrides("de", key, "Meine"))
            .GetStringFrom("de", key).Should().Be("Meine");

        log.Messages.Should().NotContain(m => m.Contains(key, StringComparison.Ordinal));
    }

    [Fact]
    public void The_translation_editor_keeps_trust_verdicts_editable_and_notes_the_profile_rule()
    {
        foreach (var key in BrandText.TrustVerdictKeys)
        {
            var row = new TranslationRowViewModel(key);
            row.IsProtected.Should().BeFalse(key);
            row.IsProtectedFromProfiles.Should().BeTrue(key);
        }

        new TranslationRowViewModel("Str.Options.Addin.Activate").IsProtectedFromProfiles.Should().BeFalse();
    }

    [Fact]
    public void Every_trust_verdict_is_a_shipped_key_and_none_is_also_fully_protected()
    {
        var english = new LocalizationService();
        foreach (var key in BrandText.TrustVerdictKeys.Append("Str.TranslationEditor.ProtectedFromProfiles"))
            english.GetStringFrom("en", key).Should().NotBe(key, "{0} must exist in en.json, or protecting it guards nothing", key);

        BrandText.TrustVerdictKeys.Should().NotIntersectWith(BrandText.ProtectedKeys);
    }

    private static IUserTranslationsService Overrides(string packId, string key, string value)
    {
        var service = Substitute.For<IUserTranslationsService>();
        service.GetOverrides(Arg.Any<string>()).Returns(new Dictionary<string, string>());
        service.GetOverrides(packId).Returns(new Dictionary<string, string> { [key] = value });
        return service;
    }

    private ProfileSnapshot Profile(Dictionary<string, Dictionary<string, string>> locales)
    {
        var files = new Dictionary<string, string>();
        foreach (var (id, strings) in locales)
        {
            files[id] = id + ".json";
            File.WriteAllText(Path.Combine(root, files[id]), JsonSerializer.Serialize(
                new LanguagePackRecord(id, null, strings), LocalizationJsonContext.Default.LanguagePackRecord));
        }
        File.WriteAllText(Path.Combine(root, "profile.json"),
            "{\"schemaVersion\":1,\"id\":\"custom\",\"languageOverrides\":" + JsonSerializer.Serialize(files) + "}");
        return new ProfileResolver(root, root).Resolve(root)!;
    }
}
