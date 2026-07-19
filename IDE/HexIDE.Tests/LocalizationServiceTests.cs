using System.Globalization;
using Avalonia;
using Avalonia.Media;
using HexIDE.Localization;

namespace HexIDE.Tests;

public class LocalizationServiceTests
{
    public LocalizationServiceTests() => AvaloniaTestSetup.EnsureInitialized();

    private static LocalizationService CreateSut() => new();

    [Fact]
    public void Apply_English_GetString_ReturnsEnglishValue()
    {
        var sut = CreateSut();
        sut.Apply("en");

        sut.GetString("Str.Menu.File.NewProject").Should().Be("_New Project");
        sut.ActiveLanguage.Should().Be("en");
    }

    [Fact]
    public void GetString_UnknownKey_ReturnsKeyItself()
    {
        var sut = CreateSut();
        sut.Apply("en");

        sut.GetString("Str.Does.Not.Exist").Should().Be("Str.Does.Not.Exist");
    }

    [Fact]
    public void Indexer_MatchesGetString()
    {
        var sut = CreateSut();
        sut.Apply("en");

        sut["Str.Menu.File"].Should().Be("_File");
        sut["Str.Menu.File"].Should().Be(sut.GetString("Str.Menu.File"));
    }

    [Fact]
    public void Apply_PutsStringsIntoApplicationResources_ForDynamicResource()
    {
        // Proves the AXAML path: Apply injects the strings into Application.Resources where
        // {DynamicResource Str.*} resolves them (the same mechanism MenuItem.Header uses).
        var sut = CreateSut();
        sut.Apply("en");

        Application.Current!.TryGetResource("Str.Menu.File", null, out var value).Should().BeTrue();
        value.Should().Be("_File");
    }

    [Fact]
    public void Apply_RaisesLanguageChanged()
    {
        var sut = CreateSut();
        var raised = 0;
        sut.LanguageChanged += () => raised++;

        sut.Apply("en");

        raised.Should().Be(1);
    }

    [Fact]
    public void Apply_UnknownPack_FallsBackToEnglish()
    {
        var sut = CreateSut();

        sut.Apply("qq-XX");   // neither "qq-XX" nor its neutral "qq" exists → genuinely unknown

        sut.ActiveLanguage.Should().Be("en");
        sut.GetString("Str.Menu.File.Exit").Should().Be("E_xit");
    }

    [Fact]
    public void Apply_DoesNotChangeThreadCulture()
    {
        // The localization service is a pack selector — it must never mutate thread culture,
        // so the deliberate invariant-culture lock that keeps VB6 number parsing stable stands.
        var sut = CreateSut();
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;

        sut.Apply("en");

        CultureInfo.CurrentCulture.Should().BeSameAs(culture);
        CultureInfo.CurrentUICulture.Should().BeSameAs(uiCulture);
    }

    [Fact]
    public void AvailableLanguages_IncludesEnglish()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id).Should().Contain("en");
    }

    [Fact]
    public void AvailableLanguages_AreLanguageNeutralsOnly_NotRegions()
    {
        // The Language combo lists one entry per translation; regions live in RegionsFor, not here.
        var ids = CreateSut().AvailableLanguages.Select(l => l.Id).ToList();
        ids.Should().Contain(["system", "en", "fr", "ar", "zh-Hans", "zh-Hant", "de", "es", "ja", "he",
            "cs", "it", "ko", "ru", "uk", "pl", "pt", "tr", "hi", "ur", "id", "vi", "fa", "nl", "sv",
            "el", "nb", "da", "fi"]);
        ids.Should().NotContain(["en-US", "en-GB", "fr-FR", "zh-CN", "ja-JP", "de-AT"]);
    }

    [Fact]
    public void RegionsFor_ReturnsRegionsUnderALanguage()
    {
        var sut = CreateSut();
        sut.RegionsFor("en").Select(r => r.Id).Should().Contain(["en-US", "en-GB", "en-CA"]);
        sut.RegionsFor("fr").Select(r => r.Id).Should().Contain(["fr-FR", "fr-BE", "fr-CA"]);
        sut.RegionsFor("zh-Hans").Select(r => r.Id).Should().Contain(["zh-CN", "zh-SG"]);
        sut.RegionsFor("zh-Hant").Select(r => r.Id).Should().Contain(["zh-TW", "zh-HK", "zh-MO"]);
        sut.RegionsFor("ur").Select(r => r.Id).Should().Contain(["ur-PK", "ur-IN"]);
        sut.RegionsFor("ja").Select(r => r.Id).Should().Equal(["ja-JP"]);
        // Official-language regions added when filling gaps (Swedish in Finland, Greek in Cyprus, etc.).
        sut.RegionsFor("sv").Select(r => r.Id).Should().Contain("sv-FI");
        sut.RegionsFor("el").Select(r => r.Id).Should().Contain("el-CY");
        sut.RegionsFor("es").Select(r => r.Id).Should().Contain(["es-BO", "es-UY", "es-PA"]);
        // system / pseudo / unknown have no regions (Region combo disables).
        sut.RegionsFor("system").Should().BeEmpty();
        sut.RegionsFor("xx-not-a-lang").Should().BeEmpty();
    }

    [Fact]
    public void GetStringFrom_English_ReturnsEnglishValue()
    {
        var sut = CreateSut();

        sut.GetStringFrom("en", "Str.Menu.File.Print").Should().Be("_Print...");
    }

    [Fact]
    public void Apply_EnGb_OverridesUsSpellings_AndInheritsCanonical()
    {
        var sut = CreateSut();
        sut.Apply("en-GB");

        sut.ActiveLanguage.Should().Be("en-GB");
        // GB overrides from en-GB.json
        sut.GetString("Str.Toolbar.Customize").Should().Be("Customise...");
        sut.GetString("Str.PropCategory.Behavior").Should().Be("Behaviour");
        // A key en-GB does not override inherits the canonical en-US.
        sut.GetString("Str.Menu.File").Should().Be("_File");
    }

    [Fact]
    public void RegionsFor_English_IncludesUsAndGb()
    {
        // en-GB is the one region that ships a real pack; en-US is a fileless region (≡ canonical en).
        CreateSut().RegionsFor("en").Select(r => r.Id).Should().Contain(["en-US", "en-GB"]);
    }

    [Fact]
    public void Apply_RegionOverNeutralOverCanonical_ResolvesThreeTier()
    {
        // zz / zz-ZZ are synthetic test packs (not in the manifest, so invisible in the dropdown):
        // neutral zz sets File + Exit; region zz-ZZ overrides only File. Proves region > neutral > canonical.
        var sut = CreateSut();
        sut.Apply("zz-ZZ");

        sut.GetString("Str.Menu.File").Should().Be("zzZZ_FILE");                 // region overrides neutral
        sut.GetString("Str.Menu.File.Exit").Should().Be("zzEXIT");               // neutral applies (region silent)
        sut.GetString("Str.Menu.File.NewProject").Should().Be("_New Project");   // canonical en-US fallback
    }

    [Fact]
    public void Apply_FrenchRegion_InheritsNeutralFrenchPack()
    {
        var sut = CreateSut();
        sut.Apply("fr");
        var frFile = sut.GetString("Str.Menu.File");
        frFile.Should().NotBe("_File");   // the neutral fr pack actually translates it

        sut.Apply("fr-FR");
        sut.ActiveLanguage.Should().Be("fr-FR");
        sut.GetString("Str.Menu.File").Should().Be(frFile);   // the empty fr-FR pack inherits neutral fr
    }

    [Fact]
    public void AvailableLanguages_IncludeFrenchRegions()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id)
            .Should().Contain("fr");
    }

    [Fact]
    public void Apply_Arabic_IsRightToLeft_AndTranslated_WithoutMnemonics()
    {
        var sut = CreateSut();
        sut.Apply("ar");

        sut.ActiveLanguage.Should().Be("ar");
        sut.FlowDirection.Should().Be(FlowDirection.RightToLeft);   // neutral ar declares direction:"rtl"
        var file = sut.GetString("Str.Menu.File");
        file.Should().NotBe("_File");        // actually translated to Arabic
        file.Should().NotContain("_");       // mnemonics omitted for Arabic
    }

    [Fact]
    public void Apply_ArabicRegion_InheritsNeutralArabic_AndRtl()
    {
        var sut = CreateSut();
        sut.Apply("ar");
        var arFile = sut.GetString("Str.Menu.File");

        sut.Apply("ar-EG");   // empty regional pack
        sut.ActiveLanguage.Should().Be("ar-EG");
        sut.FlowDirection.Should().Be(FlowDirection.RightToLeft);   // inherits rtl from neutral ar
        sut.GetString("Str.Menu.File").Should().Be(arFile);          // inherits neutral ar translation
    }

    [Fact]
    public void AvailableLanguages_IncludeArabic()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id)
            .Should().Contain("ar");
    }

    [Fact]
    public void Apply_ChineseRegions_InheritTheirScriptNeutral()
    {
        // Script-aware 3-tier: zh-CN/zh-SG inherit zh-Hans (Simplified); zh-TW/zh-HK/zh-MO inherit
        // zh-Hant (Traditional) — NOT the bare "zh".
        var sut = CreateSut();
        sut.Apply("zh-Hans");
        var hans = sut.GetString("Str.Menu.File");
        sut.Apply("zh-Hant");
        var hant = sut.GetString("Str.Menu.File");

        sut.Apply("zh-CN");
        sut.GetString("Str.Menu.File").Should().Be(hans);
        sut.Apply("zh-TW");
        sut.GetString("Str.Menu.File").Should().Be(hant);
    }

    [Fact]
    public void ChineseSimplifiedAndTraditional_AreDifferentNeutrals()
    {
        var sut = CreateSut();
        string[] keys = ["Str.Menu.File", "Str.Menu.Edit", "Str.Menu.View", "Str.Menu.Format"];
        sut.Apply("zh-Hans");
        var hans = keys.Select(sut.GetString).ToArray();
        sut.Apply("zh-Hant");
        var hant = keys.Select(sut.GetString).ToArray();
        hans.Should().NotEqual(hant);   // the two scripts produce different text
    }

    [Fact]
    public void AvailableLanguages_IncludeChinese()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id)
            .Should().Contain(["zh-Hans", "zh-Hant"]);
    }

    [Fact]
    public void Apply_GermanRegion_InheritsNeutralGerman_WithMnemonics()
    {
        var sut = CreateSut();
        sut.Apply("de");
        var deFile = sut.GetString("Str.Menu.File");
        deFile.Should().NotBe("_File");   // translated to German
        deFile.Should().Contain("_");     // mnemonic kept (Latin script)

        sut.Apply("de-AT");   // empty region
        sut.GetString("Str.Menu.File").Should().Be(deFile);
    }

    [Fact]
    public void AvailableLanguages_IncludeGerman()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id).Should().Contain("de");
    }

    [Fact]
    public void Apply_SpanishRegion_InheritsNeutralSpanish()
    {
        var sut = CreateSut();
        sut.Apply("es");
        var esFile = sut.GetString("Str.Menu.File");
        esFile.Should().NotBe("_File");

        sut.Apply("es-MX");   // empty region
        sut.GetString("Str.Menu.File").Should().Be(esFile);
    }

    [Fact]
    public void AvailableLanguages_IncludeSpanish()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id)
            .Should().Contain("es");
    }

    [Fact]
    public void Apply_JapaneseRegion_InheritsNeutralJapanese_WithoutMnemonics()
    {
        var sut = CreateSut();
        sut.Apply("ja");
        var jaFile = sut.GetString("Str.Menu.File");
        jaFile.Should().NotBe("_File");
        jaFile.Should().NotContain("_");   // mnemonics omitted for CJK

        sut.Apply("ja-JP");   // empty region
        sut.GetString("Str.Menu.File").Should().Be(jaFile);
    }

    [Fact]
    public void AvailableLanguages_IncludeJapanese()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id).Should().Contain("ja");
    }

    [Fact]
    public void Apply_HebrewRegion_InheritsNeutralHebrew_AndRtl()
    {
        var sut = CreateSut();
        sut.Apply("he");
        sut.FlowDirection.Should().Be(FlowDirection.RightToLeft);   // neutral he declares direction:"rtl"
        var heFile = sut.GetString("Str.Menu.File");
        heFile.Should().NotBe("_File");
        heFile.Should().NotContain("_");   // mnemonics omitted

        sut.Apply("he-IL");   // empty region
        sut.FlowDirection.Should().Be(FlowDirection.RightToLeft);   // inherits rtl from neutral he
        sut.GetString("Str.Menu.File").Should().Be(heFile);
    }

    [Fact]
    public void AvailableLanguages_IncludeHebrew()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id).Should().Contain("he");
    }

    [Fact]
    public void Apply_KoreanRegion_InheritsNeutralKorean_WithoutMnemonics()
    {
        var sut = CreateSut();
        sut.Apply("ko");
        var koFile = sut.GetString("Str.Menu.File");
        koFile.Should().NotBe("_File");
        koFile.Should().NotContain("_");   // mnemonics omitted

        sut.Apply("ko-KR");
        sut.GetString("Str.Menu.File").Should().Be(koFile);
    }

    [Fact]
    public void AvailableLanguages_IncludeKorean()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id).Should().Contain("ko");
    }

    [Fact]
    public void Apply_RussianRegion_InheritsNeutralRussian_WithoutMnemonics()
    {
        var sut = CreateSut();
        sut.Apply("ru");
        var ruFile = sut.GetString("Str.Menu.File");
        ruFile.Should().NotBe("_File");
        ruFile.Should().NotContain("_");

        sut.Apply("ru-RU");
        sut.GetString("Str.Menu.File").Should().Be(ruFile);
    }

    [Fact]
    public void Apply_UkrainianRegion_InheritsNeutralUkrainian_WithoutMnemonics()
    {
        var sut = CreateSut();
        sut.Apply("uk");
        var ukFile = sut.GetString("Str.Menu.File");
        ukFile.Should().NotBe("_File");
        ukFile.Should().NotContain("_");

        sut.Apply("uk-UA");
        sut.GetString("Str.Menu.File").Should().Be(ukFile);
    }

    [Fact]
    public void AvailableLanguages_IncludeRussianAndUkrainian()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id).Should().Contain(["ru", "uk"]);
    }

    [Theory]
    [InlineData("cs", "cs-CZ")]
    [InlineData("it", "it-IT")]
    [InlineData("pl", "pl-PL")]
    [InlineData("pt", "pt-BR")]
    [InlineData("tr", "tr-TR")]
    public void Apply_LatinVsLanguage_TranslatesWithMnemonics_AndRegionInherits(string neutral, string region)
    {
        // Str.Menu.Edit differs from English in all five (unlike "File", which is identical in Italian).
        var sut = CreateSut();
        sut.Apply(neutral);
        var edit = sut.GetString("Str.Menu.Edit");
        edit.Should().NotBe("_Edit");   // translated
        edit.Should().Contain("_");     // mnemonic kept (Latin script)

        sut.Apply(region);              // empty region
        sut.GetString("Str.Menu.Edit").Should().Be(edit);
    }

    [Fact]
    public void AvailableLanguages_IncludeRemainingVsLanguages()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id)
            .Should().Contain(["cs", "it", "pl", "pt", "tr"]);
    }

    [Theory]
    [InlineData("en-AU", "en")]      // English region → canonical neutral `en`
    [InlineData("fr-SN", "fr")]
    [InlineData("pt-AO", "pt")]
    [InlineData("es-PE", "es")]
    public void Apply_AddedMajorRegion_InheritsItsNeutral(string region, string parent)
    {
        var sut = CreateSut();
        sut.Apply(parent);
        var expected = sut.GetString("Str.Menu.File");
        sut.Apply(region);                                      // fileless region → resolves through neutral
        sut.GetString("Str.Menu.File").Should().Be(expected);
    }

    [Fact]
    public void Apply_AddedArabicRegion_InheritsRtlFromNeutral()
    {
        var sut = CreateSut();
        sut.Apply("ar-KW");                                     // empty pack, no direction → inherits ar's rtl
        sut.FlowDirection.Should().Be(FlowDirection.RightToLeft);
    }

    [Theory]
    [InlineData("id", "id-ID")]
    [InlineData("vi", "vi-VN")]
    [InlineData("nl", "nl-NL")]
    [InlineData("nl", "nl-BE")]
    [InlineData("sv", "sv-SE")]
    [InlineData("nb", "nb-NO")]
    [InlineData("da", "da-DK")]
    [InlineData("fi", "fi-FI")]
    public void Apply_Batch2Latin_TranslatesWithMnemonics_AndRegionInherits(string neutral, string region)
    {
        // "Ready" reliably differs in every target; "_File" proves the Latin mnemonic is kept even
        // where the word itself is loaned unchanged (e.g. "File").
        var sut = CreateSut();
        sut.Apply(neutral);
        var ready = sut.GetString("Str.StatusBar.Ready");
        ready.Should().NotBe("Ready");                          // really translated
        sut.GetString("Str.Menu.File").Should().Contain("_");  // mnemonic kept (Latin script)

        sut.Apply(region);                                      // empty region
        sut.GetString("Str.StatusBar.Ready").Should().Be(ready);
    }

    [Theory]
    [InlineData("hi", "hi-IN")]
    [InlineData("el", "el-GR")]
    public void Apply_Batch2NonLatin_TranslatesWithoutMnemonics_AndRegionInherits(string neutral, string region)
    {
        var sut = CreateSut();
        sut.Apply(neutral);
        var ready = sut.GetString("Str.StatusBar.Ready");
        ready.Should().NotBe("Ready");
        sut.GetString("Str.Menu.File").Should().NotContain("_");   // mnemonics omitted (non-Latin)

        sut.Apply(region);
        sut.GetString("Str.StatusBar.Ready").Should().Be(ready);
    }

    [Theory]
    [InlineData("ur", "ur-PK")]
    [InlineData("ur", "ur-IN")]
    [InlineData("fa", "fa-IR")]
    public void Apply_Batch2Rtl_IsRightToLeft_TranslatedWithoutMnemonics_AndRegionInherits(string neutral, string region)
    {
        var sut = CreateSut();
        sut.Apply(neutral);
        sut.FlowDirection.Should().Be(FlowDirection.RightToLeft);   // neutral declares direction:"rtl"
        var ready = sut.GetString("Str.StatusBar.Ready");
        ready.Should().NotBe("Ready");
        sut.GetString("Str.Menu.File").Should().NotContain("_");

        sut.Apply(region);                                          // empty region
        sut.FlowDirection.Should().Be(FlowDirection.RightToLeft);   // inherits rtl from neutral
        sut.GetString("Str.StatusBar.Ready").Should().Be(ready);
    }

    [Fact]
    public void AvailableLanguages_IncludeBatch2Languages()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id)
            .Should().Contain(["hi", "ur", "id", "vi", "fa", "nl", "sv", "el", "nb", "da", "fi"]);
    }

    [Fact]
    public void GetPropertyDescription_ReturnsLocalizedText()
    {
        var sut = CreateSut();
        sut.Apply("en");

        sut.GetPropertyDescription("Caption").Should()
            .Be("The text shown in the object's title bar, or beside its icon.");
        // Name is sanitized: "(Name)" -> "Name"
        sut.GetPropertyDescription("(Name)").Should()
            .Be("The identifier used to refer to this object in code.");
    }

    [Fact]
    public void GetPropertyDescription_WhenNoKeyExists_ReturnsNull()
    {
        // A property with no Str.PropDesc.* key falls back (caller uses the Core English description).
        var sut = CreateSut();
        sut.Apply("en");

        sut.GetPropertyDescription("NoSuchPropertyXyz").Should().BeNull();
    }

    [Fact]
    public void AvailableLanguages_IncludeSystemDefaultFirst()
    {
        var langs = CreateSut().AvailableLanguages;
        langs[0].Id.Should().Be("system");
        langs.Select(l => l.Id).Should().Contain("system");
    }

    [Fact]
    public void AvailableLanguages_AfterSystemDefault_AreAlphaSortedByDisplayName()
    {
        var langs = CreateSut().AvailableLanguages;
        langs[0].Id.Should().Be("system");   // sentinel pinned to top, exempt from the sort
        langs.Skip(1).Select(l => l.DisplayName)
            .Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_System_ResolvesToARealPack()
    {
        var sut = CreateSut();

        sut.Apply("system");

        // The selected id stays the "system" sentinel...
        sut.ActiveLanguage.Should().Be("system");
        // ...but the effective strings come from the OS-resolved pack (English here, the only file pack),
        // so a known key still resolves rather than echoing back.
        sut.GetString("Str.Menu.File").Should().Be("_File");
    }

    [Fact]
    public void Apply_Pseudo_TransformsStrings()
    {
        var sut = CreateSut();

        sut.Apply("pseudo");

        sut.ActiveLanguage.Should().Be("pseudo");
        var value = sut.GetString("Str.Menu.File");
        value.Should().StartWith("⟦");
        value.Should().NotBe("_File");
    }

    [Fact]
    public void Apply_PseudoLtr_SetsLeftToRight()
    {
        var sut = CreateSut();
        sut.Apply("pseudo");
        sut.FlowDirection.Should().Be(FlowDirection.LeftToRight);
    }

    [Fact]
    public void Apply_PseudoRtl_SetsRightToLeft()
    {
        var sut = CreateSut();
        sut.Apply("pseudo-rtl");
        sut.FlowDirection.Should().Be(FlowDirection.RightToLeft);
    }

    [Fact]
    public void AvailableLanguages_HidePseudo_ForNormalUsers()
    {
        // Pseudo locales are developer-only — never shown in the dropdown to end users
        // (default = not developer mode). The Apply("pseudo") path above still works regardless.
        CreateSut().AvailableLanguages.Select(l => l.Id)
            .Should().NotContain(["pseudo", "pseudo-rtl"]);
    }

    [Fact]
    public void AvailableLanguages_IncludePseudo_InDeveloperMode()
    {
        var dev = Substitute.For<IDeveloperModeService>();
        dev.IsEnabled.Returns(true);
        new LocalizationService(developerMode: dev).AvailableLanguages.Select(l => l.Id)
            .Should().Contain(["pseudo", "pseudo-rtl"]);
    }
}
