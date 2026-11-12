using System.Globalization;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Tests;

public class LocalizationServiceTests
{

    private static LocalizationService CreateSut() => new();

    [AvaloniaTheory]
    [InlineData("zz")]
    [InlineData("zz-ZZ")]
    public void TestPack_IsNotEmbeddedInProduct(string id)
    {
        AssetLoader.Exists(new Uri($"avares://HexIDE/Localization/Packs/{id}.json"))
            .Should().BeFalse();
        AssetLoader.GetAssets(new Uri("avares://HexIDE/Localization/Packs/"), null)
            .Should().NotContain(uri => uri.AbsolutePath.EndsWith($"/{id}.json", StringComparison.Ordinal));
    }

    [AvaloniaTheory]
    [InlineData("Str.Tool.Properties.Tab.Categorized", "Według kategorii")]
    [InlineData("Str.Tool.Project.Toolbar.ViewCode", "Pokaż kod")]
    [InlineData("Str.Tool.Project.Toolbar.ViewObject", "Pokaż obiekt")]
    [InlineData("Str.Tool.Project.Menu.ViewObject", "Pokaż obiekt")]
    [InlineData("Str.Tool.Project.Menu.ViewCode", "Pokaż kod")]
    [InlineData("Str.Tool.Project.Menu.Properties", "Właściwości")]
    [InlineData("Str.Tool.ColorPalette.Button.Default", "Domyślny")]
    [InlineData("Str.Tool.Locals.Column.Expression", "Wyrażenie")]
    [InlineData("Str.Tool.Locals.Column.Value", "Wartość")]
    [InlineData("Str.Tool.Watches.Column.Expression", "Wyrażenie")]
    [InlineData("Str.Tool.Watches.Column.Value", "Wartość")]
    [InlineData("Str.Tool.ObjectBrowser.Chrome.CloseTip", "Zamknij Przeglądarkę obiektów")]
    [InlineData("Str.Tool.ObjectBrowser.Chrome.Title", "Przeglądarka obiektów")]
    [InlineData("Str.Tool.ObjectBrowser.Toolbar.GoToDefinition", "Przejdź do definicji")]
    [InlineData("Str.Tool.ObjectBrowser.Toolbar.CopySignature", "Kopiuj sygnaturę do schowka")]
    [InlineData("Str.Tool.ObjectBrowser.Toolbar.ClearSearch", "Wyczyść wyszukiwanie")]
    [InlineData("Str.Tool.Properties.Title", "Właściwości")]
    [InlineData("Str.Tool.Project.TitleGroup", "Grupa projektów - {0}")]
    [InlineData("Str.Tool.FormLayout.Title", "Układ formularza")]
    [InlineData("Str.Tool.ObjectBrowser.Title", "Przeglądarka obiektów")]
    [InlineData("Str.Tool.Project.PropertiesFormat", "Właściwości {0}")]
    [InlineData("Str.Tool.Project.RemoveFormFormat", "Usuń {0}")]
    public void Apply_Polish_PreservesDiacriticsInEarlyEntries(string key, string expected)
    {
        var sut = CreateSut();
        sut.Apply("pl");

        sut.GetString(key).Should().Be(expected);
    }

    [AvaloniaFact]
    public void Apply_English_GetString_ReturnsEnglishValue()
    {
        var sut = CreateSut();
        sut.Apply("en");

        sut.GetString("Str.Menu.File.NewProject").Should().Be("_New Project");
        sut.ActiveLanguage.Should().Be("en");
    }

    [AvaloniaFact]
    public void ResetPrompt_UsesMeasuredVB6Wording()
    {
        var sut = CreateSut();
        sut.Apply("en");

        sut.GetString("Str.ProjectRunner.EditWhileRunningConfirm").Should()
            .Be("This action will reset your project, proceed anyway?");
    }

    [AvaloniaFact]
    public void GetString_UnknownKey_ReturnsKeyItself()
    {
        var sut = CreateSut();
        sut.Apply("en");

        sut.GetString("Str.Does.Not.Exist").Should().Be("Str.Does.Not.Exist");
    }

    [AvaloniaFact]
    public void Indexer_MatchesGetString()
    {
        var sut = CreateSut();
        sut.Apply("en");

        sut["Str.Menu.File"].Should().Be("_File");
        sut["Str.Menu.File"].Should().Be(sut.GetString("Str.Menu.File"));
    }

    [AvaloniaFact]
    public void Apply_PutsStringsIntoApplicationResources_ForDynamicResource()
    {
        // Proves the AXAML path: Apply injects the strings into Application.Resources where
        // {DynamicResource Str.*} resolves them (the same mechanism MenuItem.Header uses).
        var sut = CreateSut();
        sut.Apply("en");

        Application.Current!.TryGetResource("Str.Menu.File", null, out var value).Should().BeTrue();
        value.Should().Be("_File");
    }

    [AvaloniaFact]
    public void Apply_RaisesLanguageChanged()
    {
        var sut = CreateSut();
        var raised = 0;
        sut.LanguageChanged += () => raised++;

        sut.Apply("en");

        raised.Should().Be(1);
    }

    [AvaloniaFact]
    public void Apply_UnknownPack_FallsBackToEnglish()
    {
        var sut = CreateSut();

        sut.Apply("qq-XX");   // neither "qq-XX" nor its neutral "qq" exists → genuinely unknown

        sut.ActiveLanguage.Should().Be("en");
        sut.GetString("Str.Menu.File.Exit").Should().Be("E_xit");
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void AvailableLanguages_IncludesEnglish()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id).Should().Contain("en");
    }

    [AvaloniaFact]
    public void AvailableLanguages_AreLanguageNeutralsOnly_NotRegions()
    {
        // The Language combo lists one entry per translation; regions live in RegionsFor, not here.
        var ids = CreateSut().AvailableLanguages.Select(l => l.Id).ToList();
        ids.Should().Contain(["system", "en", "fr", "ar", "zh-Hans", "zh-Hant", "de", "es", "ja", "he",
            "cs", "it", "ko", "ru", "uk", "pl", "pt", "tr", "id", "vi", "nl", "sv",
            "el", "nb", "da", "fi"]);
        ids.Should().NotContain(["en-US", "en-GB", "fr-FR", "zh-CN", "ja-JP", "de-AT"]);
    }

    [AvaloniaFact]
    public void RegionsFor_ReturnsRegionsUnderALanguage()
    {
        var sut = CreateSut();
        sut.RegionsFor("en").Select(r => r.Id).Should().Contain(["en-US", "en-GB", "en-CA"]);
        sut.RegionsFor("fr").Select(r => r.Id).Should().Contain(["fr-FR", "fr-BE", "fr-CA"]);
        sut.RegionsFor("zh-Hans").Select(r => r.Id).Should().Contain(["zh-CN", "zh-SG"]);
        sut.RegionsFor("zh-Hant").Select(r => r.Id).Should().Contain(["zh-TW", "zh-HK", "zh-MO"]);
        sut.RegionsFor("ja").Select(r => r.Id).Should().Equal(["ja-JP"]);
        // Official-language regions added when filling gaps (Swedish in Finland, Greek in Cyprus, etc.).
        sut.RegionsFor("sv").Select(r => r.Id).Should().Contain("sv-FI");
        sut.RegionsFor("el").Select(r => r.Id).Should().Contain("el-CY");
        sut.RegionsFor("es").Select(r => r.Id).Should().Contain(["es-BO", "es-UY", "es-PA"]);
        // system / pseudo / unknown have no regions (Region combo disables).
        sut.RegionsFor("system").Should().BeEmpty();
        sut.RegionsFor("xx-not-a-lang").Should().BeEmpty();
    }

    [AvaloniaFact]
    public void GetStringFrom_English_ReturnsEnglishValue()
    {
        var sut = CreateSut();

        sut.GetStringFrom("en", "Str.Menu.File.Print").Should().Be("_Print...");
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void RegionsFor_English_IncludesUsAndGb()
    {
        // en-GB ships a real pack; en-US is a fileless region (≡ canonical en).
        CreateSut().RegionsFor("en").Select(r => r.Id).Should().Contain(["en-US", "en-GB"]);
    }

    [AvaloniaFact]
    public void Apply_RegionOverNeutralOverCanonical_ResolvesThreeTier()
    {
        // zz / zz-ZZ are synthetic packs loaded only within this test:
        // neutral zz sets File + Exit; region zz-ZZ overrides only File. Proves region > neutral > canonical.
        var sut = LocalizationService.CreateWithPackUris(id => new Uri(
            $"avares://{(id is "zz" or "zz-ZZ" ? "HexIDE.Tests" : "HexIDE")}/Localization/Packs/{id}.json"));
        sut.Apply("zz-ZZ");

        sut.GetString("Str.Menu.File").Should().Be("zzZZ_FILE");                 // region overrides neutral
        sut.GetString("Str.Menu.File.Exit").Should().Be("zzEXIT");               // neutral applies (region silent)
        sut.GetString("Str.Menu.File.NewProject").Should().Be("_New Project");   // canonical en-US fallback
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void AvailableLanguages_IncludeFrenchRegions()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id)
            .Should().Contain("fr");
    }

    [AvaloniaFact]
    public void Apply_Arabic_IsRightToLeft_AndTranslated_WithoutMnemonics()
    {
        var sut = CreateSut();
        sut.Apply("ar");

        sut.ActiveLanguage.Should().Be("ar");
        sut.FlowDirection.Should().Be(FlowDirection.RightToLeft);   // neutral ar declares direction:"rtl"
        Application.Current!.Resources.TryGetResource(LocalizationService.FlowDirectionKey, null, out var direction).Should().BeTrue();
        direction.Should().Be(FlowDirection.RightToLeft, "a menu opened from code reads the language's direction from here (#1944)");
        var file = sut.GetString("Str.Menu.File");
        file.Should().NotBe("_File");        // actually translated to Arabic
        file.Should().NotContain("_");       // mnemonics omitted for Arabic
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void AvailableLanguages_IncludeArabic()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id)
            .Should().Contain("ar");
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void AvailableLanguages_IncludeChinese()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id)
            .Should().Contain(["zh-Hans", "zh-Hant"]);
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void AvailableLanguages_IncludeGerman()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id).Should().Contain("de");
    }

    [AvaloniaFact]
    public void Apply_SpanishRegion_InheritsNeutralSpanish()
    {
        var sut = CreateSut();
        sut.Apply("es");
        var esFile = sut.GetString("Str.Menu.File");
        esFile.Should().NotBe("_File");

        sut.Apply("es-MX");   // regional overrides inherit the remaining Spanish strings
        sut.GetString("Str.Menu.File").Should().Be(esFile);
    }

    [AvaloniaFact]
    public void AvailableLanguages_IncludeSpanish()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id)
            .Should().Contain("es");
    }

    [AvaloniaFact]
    public void Apply_JapaneseRegion_InheritsNeutralJapanese_WithMnemonics()
    {
        var sut = CreateSut();
        sut.Apply("ja");
        var jaFile = sut.GetString("Str.Menu.File");
        jaFile.Should().NotBe("_File");
        jaFile.Should().Be("ファイル(_F)");

        sut.Apply("ja-JP");   // empty region
        sut.GetString("Str.Menu.File").Should().Be(jaFile);
    }

    [AvaloniaTheory]
    [InlineData("ja")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    public void Apply_CjkPack_PreservesEnglishAccessKeysExceptDock(string language)
    {
        var sut = CreateSut();
        sut.Apply("en");
        using var stream = AssetLoader.Open(new Uri("avares://HexIDE/Localization/Packs/en.json"));
        var canonical = JsonSerializer.Deserialize(stream, LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!;
        sut.Apply(language);
        foreach (var (key, value) in canonical)
        {
            // Dock labels omit access keys in non-Latin scripts (#3776).
            if (key.StartsWith("Str.Dock.", StringComparison.Ordinal))
            {
                sut.GetString(key).Should().NotContain("_", key);
                continue;
            }
            // These underscores belong to identifiers, not access keys (#41).
            if (key is "Str.Vb6Toolchain.Vb6NotFound" or "Str.Dialog.ConflictingNames")
                continue;
            var marker = value.IndexOf('_');
            if (marker < 0) continue;
            var translated = sut.GetString(key);
            translated.Should().Contain($"(_{char.ToUpperInvariant(value[marker + 1])})", key);
            translated.Count(c => c == '_').Should().Be(1, key);
        }
    }

    [AvaloniaFact]
    public void AvailableLanguages_IncludeJapanese()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id).Should().Contain("ja");
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void AvailableLanguages_IncludeHebrew()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id).Should().Contain("he");
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void AvailableLanguages_IncludeKorean()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id).Should().Contain("ko");
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void AvailableLanguages_IncludeRussianAndUkrainian()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id).Should().Contain(["ru", "uk"]);
    }

    [AvaloniaTheory]
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

    [AvaloniaFact]
    public void AvailableLanguages_IncludeRemainingVsLanguages()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id)
            .Should().Contain(["cs", "it", "pl", "pt", "tr"]);
    }

    [AvaloniaTheory]
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

    [AvaloniaFact]
    public void Apply_AddedArabicRegion_InheritsRtlFromNeutral()
    {
        var sut = CreateSut();
        sut.Apply("ar-KW");                                     // empty pack, no direction → inherits ar's rtl
        sut.FlowDirection.Should().Be(FlowDirection.RightToLeft);
    }

    [AvaloniaTheory]
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

    [AvaloniaTheory]
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

    [AvaloniaFact]
    public void AvailableLanguages_IncludeBatch2Languages()
    {
        CreateSut().AvailableLanguages.Select(l => l.Id)
            .Should().Contain(["id", "vi", "nl", "sv", "el", "nb", "da", "fi"]);
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
    public void GetPropertyDescription_WhenNoKeyExists_ReturnsNull()
    {
        // A property with no Str.PropDesc.* key falls back (caller uses the Core English description).
        var sut = CreateSut();
        sut.Apply("en");

        sut.GetPropertyDescription("NoSuchPropertyXyz").Should().BeNull();
    }

    [AvaloniaFact]
    public void AvailableLanguages_IncludeSystemDefaultFirst()
    {
        var langs = CreateSut().AvailableLanguages;
        langs[0].Id.Should().Be("system");
        langs.Select(l => l.Id).Should().Contain("system");
    }

    [AvaloniaFact]
    public void AvailableLanguages_AfterSystemDefault_AreAlphaSortedByDisplayName()
    {
        var langs = CreateSut().AvailableLanguages;
        langs[0].Id.Should().Be("system");   // sentinel pinned to top, exempt from the sort
        langs.Skip(1).Select(l => l.DisplayName)
            .Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void Apply_System_ResolvesToARealPack()
    {
        var sut = CreateSut();

        sut.Apply("system");

        // The selected id stays the "system" sentinel...
        sut.ActiveLanguage.Should().Be("system");
        // ...but a known key resolves through a real pack rather than echoing back.
        sut.GetString("Str.Menu.File").Should().NotBe("Str.Menu.File");
    }

    [Theory]
    [InlineData("LocalizationService.OsUICulture = CultureInfo.CurrentUICulture;")]
    [InlineData("UserRegion.Culture = CultureInfo.CurrentCulture;")]
    public void Initialize_CapturesCulturesBeforeInvariantPin(string assignment)
    {
        var root = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !System.IO.File.Exists(System.IO.Path.Combine(root.FullName, "IDE", "HexIDE", "App.axaml.cs")))
            root = root.Parent;
        root.Should().NotBeNull();
        var source = System.IO.File.ReadAllText(System.IO.Path.Combine(root!.FullName, "IDE", "HexIDE", "App.axaml.cs"))
            .ReplaceLineEndings("\n");
        var capture = source.IndexOf(assignment, StringComparison.Ordinal);
        capture.Should().BeGreaterThanOrEqualTo(0);
        capture.Should().BeLessThan(source.IndexOf("CultureInfo.DefaultThreadCurrentCulture =", StringComparison.Ordinal));
        capture.Should().BeLessThan(source.IndexOf("CultureInfo.DefaultThreadCurrentUICulture =", StringComparison.Ordinal));
    }

    [AvaloniaTheory]
    [InlineData("de-DE", "de")]
    [InlineData("fr-CD", "fr")]
    [InlineData("en-GB", "en-GB")]
    public void Apply_System_UsesCapturedUiCulture(string culture, string expectedPack)
    {
        var previous = LocalizationService.OsUICulture;
        var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            LocalizationService.OsUICulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            var sut = CreateSut();
            sut.Apply("system");

            sut.ActiveLanguage.Should().Be("system");
            sut.GetString("Str.Menu.Format.CenterInForm").Should()
                .Be(sut.GetStringFrom(expectedPack, "Str.Menu.Format.CenterInForm"));
        }
        finally
        {
            LocalizationService.OsUICulture = previous;
            CultureInfo.DefaultThreadCurrentUICulture = previousDefault;
        }
    }

    [AvaloniaFact]
    public void Apply_Pseudo_TransformsStrings()
    {
        var sut = CreateSut();

        sut.Apply("pseudo");

        sut.ActiveLanguage.Should().Be("pseudo");
        var value = sut.GetString("Str.Menu.File");
        value.Should().StartWith("⟦");
        value.Should().NotBe("_File");
    }

    [AvaloniaFact]
    public void Apply_PseudoLtr_SetsLeftToRight()
    {
        var sut = CreateSut();
        sut.Apply("pseudo");
        sut.FlowDirection.Should().Be(FlowDirection.LeftToRight);
    }

    [AvaloniaFact]
    public void Apply_PseudoRtl_SetsRightToLeft()
    {
        var sut = CreateSut();
        sut.Apply("pseudo-rtl");
        sut.FlowDirection.Should().Be(FlowDirection.RightToLeft);
    }

    [AvaloniaFact]
    public void AvailableLanguages_HidePseudo_ForNormalUsers()
    {
        // Pseudo locales are developer-only — never shown in the dropdown to end users
        // (default = not developer mode). The Apply("pseudo") path above still works regardless.
        CreateSut().AvailableLanguages.Select(l => l.Id)
            .Should().NotContain(["pseudo", "pseudo-rtl"]);
    }

    [AvaloniaFact]
    public void AvailableLanguages_IncludePseudo_InDeveloperMode()
    {
        var dev = Substitute.For<IDeveloperModeService>();
        dev.IsEnabled.Returns(true);
        new LocalizationService(developerMode: dev).AvailableLanguages.Select(l => l.Id)
            .Should().Contain(["pseudo", "pseudo-rtl"]);
    }

    [AvaloniaTheory]
    [InlineData("hi")]
    [InlineData("hi-IN")]
    [InlineData("fa")]
    [InlineData("fa-IR")]
    [InlineData("fa-AF")]
    [InlineData("ur")]
    [InlineData("ur-PK")]
    [InlineData("ur-IN")]
    [InlineData("la")]
    [InlineData("la-VA")]
    [InlineData("eo")]
    [InlineData("eo-001")]
    public void Apply_RetiredShippedLanguage_FallsBackToEnglish(string savedLanguage)
    {
        var sut = CreateSut();

        var directory = Path.Combine(Path.GetTempPath(), "HexIDE_RetiredLanguage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var settings = new SettingsService(path) { ActiveLanguage = savedLanguage };
            settings.Save();

            // App.Initialize applies the persisted selection through this same service call.
            sut.Apply(new SettingsService(path).ActiveLanguage);
        }
        finally
        {
            Directory.Delete(directory, true);
        }

        sut.ActiveLanguage.Should().Be("en");
        sut.GetString("Str.Menu.File.Exit").Should().Be("E_xit");
        sut.FlowDirection.Should().Be(FlowDirection.LeftToRight);
    }

    [AvaloniaTheory]
    [InlineData("hi")]
    [InlineData("fa")]
    [InlineData("ur")]
    [InlineData("la")]
    [InlineData("eo")]
    public void RetiredShippedLanguage_IsNotOfferedOrEmbedded(string id)
    {
        var sut = CreateSut();

        sut.AvailableLanguages.Select(l => l.Id).Should().NotContain(id);
        sut.RegionsFor(id).Should().BeEmpty();
        AssetLoader.Exists(new Uri($"avares://HexIDE/Localization/Packs/{id}.json"))
            .Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData("ro")]
    [InlineData("ro-RO")]
    [InlineData("ro-MD")]
    public void Apply_Romanian_TranslatesChromeAndPreservesLatinMnemonics(string id)
    {
        var sut = CreateSut();
        sut.Apply(id);

        sut.GetString("Str.Menu.File").Should().Be("_Fișier");
        sut.GetString("Str.StatusBar.Ready").Should().Be("Pregătit");
        sut.GetString("Str.Dialog.MessageBox.Button.Cancel").Should().Be("Anulare");
        sut.GetPropertyDescription("Width").Should().Be("Lățimea obiectului.");
        sut.FlowDirection.Should().Be(FlowDirection.LeftToRight);
        Application.Current!.TryGetResource("Str.Menu.File", null, out var value).Should().BeTrue();
        value.Should().Be("_Fișier");
    }

    [AvaloniaFact]
    public void AvailableLanguages_IncludeRomanianWithRomaniaAndMoldovaRegions()
    {
        var sut = CreateSut();
        sut.AvailableLanguages.Should().Contain(l => l.Id == "ro" && l.DisplayName == "Romanian");
        sut.RegionsFor("ro").Select(r => r.Id).Should().Equal(["ro-RO", "ro-MD"]);
    }

    [AvaloniaFact]
    public void Hungarian_IsSelectableWithHungaryRegion()
    {
        LanguageManifest.Packs.Should().Contain(new LanguageInfo("hu", "Hungarian", "ltr"));
        LanguageManifest.Regions.Should().ContainKey("hu");
        LanguageManifest.Regions["hu"].Should().Contain(new HexIDE.Localization.RegionInfo("hu-HU", "Hungary"));
    }

    [AvaloniaTheory]
    [InlineData("hu")]
    [InlineData("hu-HU")]
    public void Apply_Hungarian_LoadsTranslatedResources(string language)
    {
        var sut = CreateSut();
        sut.Apply(language);

        sut.GetString("Str.Menu.File").Should().Be("_Fájl");
        sut.GetString("Str.RevertGate.Prompt").Should().Be("Megtartja ezt a nyelvet?");
        Application.Current!.TryGetResource("Str.Menu.File", null, out var value).Should().BeTrue();
        value.Should().Be("_Fájl");
        sut.FlowDirection.Should().Be(FlowDirection.LeftToRight);
    }

    [AvaloniaTheory]
    [InlineData("bg")]
    [InlineData("bg-BG")]
    public void Apply_Bulgarian_LoadsTranslatedChrome(string id)
    {
        var sut = CreateSut();
        sut.Apply(id);

        sut.ActiveLanguage.Should().Be(id);
        sut.GetString("Str.Menu.File").Should().Be("Файл");
        sut.GetString("Str.Menu.File.NewProject").Should().Be("Нов проект");
        Application.Current!.TryGetResource("Str.Menu.File", null, out var value).Should().BeTrue();
        value.Should().Be("Файл");
    }

    [AvaloniaFact]
    public void AvailableLanguages_IncludeCroatianAndItsRegion()
    {
        var sut = CreateSut();
        sut.AvailableLanguages.Should().Contain(l => l.Id == "hr" && l.DisplayName == "Croatian");
        sut.RegionsFor("hr").Select(r => r.Id).Should().Equal(["hr-HR"]);
    }

    [AvaloniaTheory]
    [InlineData("hr")]
    [InlineData("hr-HR")]
    public void Apply_Croatian_TranslatesWithMnemonics(string id)
    {
        var sut = CreateSut();
        sut.Apply(id);

        sut.ActiveLanguage.Should().Be(id);
        sut.FlowDirection.Should().Be(FlowDirection.LeftToRight);
        sut.GetString("Str.Menu.File").Should().Be("_Datoteka");
        sut.GetString("Str.StatusBar.Ready").Should().Be("Spremno");
        string.Format(sut.GetString("Str.RevertGate.Countdown"), 5).Should().Be("Vraćanje za 5 s…");
    }

    [AvaloniaFact]
    public void CroatianPack_PreservesEveryKeyPlaceholderAndAccessKey()
    {
        using var english = AssetLoader.Open(new Uri("avares://HexIDE/Localization/Packs/en.json"));
        using var croatian = AssetLoader.Open(new Uri("avares://HexIDE/Localization/Packs/hr.json"));
        var canonical = System.Text.Json.JsonSerializer.Deserialize(english,
            LocalizationJsonContext.Default.LanguagePackRecord)!;
        var pack = System.Text.Json.JsonSerializer.Deserialize(croatian,
            LocalizationJsonContext.Default.LanguagePackRecord)!;
        pack.Name.Should().Be("Croatian");
        pack.Direction.Should().Be("ltr");
        pack.Strings!.Keys.Should().BeEquivalentTo(canonical.Strings!.Keys);
        foreach (var (key, original) in canonical.Strings)
        {
            var translated = pack.Strings[key];
            translated.Should().NotBeNullOrWhiteSpace(key);
            System.Text.RegularExpressions.Regex.Matches(translated, @"\{\d+\}")
                .Select(m => m.Value).Should().BeEquivalentTo(
                    System.Text.RegularExpressions.Regex.Matches(original, @"\{\d+\}")
                        .Select(m => m.Value), key);
            if (!NavigationAccessKeyTests.CollisionPolicyKeys.Contains(key))
                translated.Count(c => c == '_').Should().Be(original.Count(c => c == '_'), key);
        }
    }

    [AvaloniaTheory]
    [InlineData("sk")]
    [InlineData("sk-SK")]
    public void Apply_Slovak_LoadsEmbeddedTranslationAndResources(string id)
    {
        var sut = CreateSut();
        sut.AvailableLanguages.Should().Contain(language => language.Id == "sk");
        sut.Apply(id);

        sut.ActiveLanguage.Should().Be(id);
        sut.FlowDirection.Should().Be(FlowDirection.LeftToRight);
        sut.GetString("Str.Menu.File.NewProject").Should().Be("_Nový projekt");
        sut.GetString("Str.StatusBar.LineColumn").Should().Be("Riadok {0}, stĺpec {1}");
        Application.Current!.TryGetResource("Str.Menu.File", null, out var value).Should().BeTrue();
        value.Should().Be("_Súbor");
    }

    [AvaloniaFact]
    public void AvailableLanguages_IncludeSlovenianAndItsRegion()
    {
        var sut = CreateSut();
        sut.AvailableLanguages.Should().Contain(l => l.Id == "sl" && l.DisplayName == "Slovenian");
        sut.RegionsFor("sl").Select(r => r.Id).Should().Contain("sl-SI");
    }

    [AvaloniaTheory]
    [InlineData("sl")]
    [InlineData("sl-SI")]
    public void Apply_Slovenian_TranslatesWithMnemonicsAndPlaceholders(string language)
    {
        var sut = CreateSut();
        sut.Apply(language);

        sut.FlowDirection.Should().Be(FlowDirection.LeftToRight);
        sut.GetString("Str.Menu.File").Should().Be("_Datoteka");
        sut.GetString("Str.StatusBar.Ready").Should().Be("Pripravljeno");
        string.Format(sut.GetString("Str.Tool.CallStack.RowName"), "Main", 12)
            .Should().Be("Main, vrstica 12");
    }

    [AvaloniaTheory]
    [InlineData("lt")]
    [InlineData("lt-LT")]
    public void Apply_Lithuanian_LoadsEmbeddedTranslation(string id)
    {
        var sut = CreateSut();
        sut.Apply(id);

        sut.ActiveLanguage.Should().Be(id);
        sut.GetString("Str.Menu.File").Should().Be("_Failas");
        sut.GetString("Str.Dialog.MessageBox.Button.Yes").Should().Be("Taip");
        Application.Current!.TryGetResource("Str.Menu.File", null, out var value).Should().BeTrue();
        value.Should().Be("_Failas");
    }

    [AvaloniaFact]
    public void AvailableLanguages_IncludesLithuanianAndItsRegion()
    {
        var sut = CreateSut();

        sut.AvailableLanguages.Should().Contain(new LanguageInfo("lt", "Lithuanian", "ltr"));
        sut.RegionsFor("lt").Should().Contain(new HexIDE.Localization.RegionInfo("lt-LT", "Lithuania"));
    }

    [AvaloniaFact]
    public void AvailableLanguages_IncludeLatvianAndLatvia()
    {
        var sut = CreateSut();
        sut.AvailableLanguages.Should().Contain(l => l.Id == "lv" && l.Direction == "ltr");
        LanguageManifest.Regions["lv"].Should().Contain(r => r.Id == "lv-LV");
    }

    [AvaloniaTheory]
    [InlineData("lv")]
    [InlineData("lv-LV")]
    public void Apply_Latvian_TranslatesResourcesAndPreservesPlaceholders(string id)
    {
        var sut = CreateSut();
        sut.Apply(id);

        sut.GetString("Str.Menu.File").Should().Be("_Fails");
        sut.GetString("Str.StatusBar.Ready").Should().Be("Gatavs");
        string.Format(sut.GetString("Str.StatusBar.LineColumn"), 12, 3)
            .Should().Be("R. 12, kol. 3");
        sut.FlowDirection.Should().Be(FlowDirection.LeftToRight);
        Application.Current!.TryGetResource("Str.Menu.File", null, out var value).Should().BeTrue();
        value.Should().Be("_Fails");
    }

    [AvaloniaTheory]
    [InlineData("et")]
    [InlineData("et-EE")]
    public void Apply_Estonian_UsesEmbeddedTranslationWithMnemonics(string id)
    {
        var sut = CreateSut();
        sut.Apply(id);

        sut.GetString("Str.Menu.File.NewProject").Should().Be("_Uus projekt");
        sut.GetString("Str.StatusBar.Ready").Should().Be("Valmis");
        sut.GetString("Str.StatusBar.LineColumn").Should().Be("Rida {0}, veerg {1}");
        sut.FlowDirection.Should().Be(FlowDirection.LeftToRight);
    }

    [AvaloniaFact]
    public void AvailableLanguages_IncludeEstonianAndEstoniaRegion()
    {
        var sut = CreateSut();
        sut.AvailableLanguages.Should().Contain(new LanguageInfo("et", "Estonian", "ltr"));
        LanguageManifest.Regions["et"].Should().Contain(new HexIDE.Localization.RegionInfo("et-EE", "Estonia"));
    }

    [AvaloniaFact]
    public void Apply_Basque_LoadsShippedLeftToRightTranslation()
    {
        var sut = CreateSut();
        sut.AvailableLanguages.Should().Contain(l => l.Id == "eu" && l.Direction == "ltr");

        sut.Apply("eu");

        sut.ActiveLanguage.Should().Be("eu");
        sut.GetString("Str.Menu.File").Should().Be("_Fitxategia");
        sut.GetString("Str.StatusBar.Ready").Should().Be("Prest");
        Application.Current!.TryGetResource("Str.Menu.File", null, out var value).Should().BeTrue();
        value.Should().Be("_Fitxategia");
    }

    [AvaloniaFact]
    public void Apply_Catalan_OffersAndLoadsTranslatedChrome()
    {
        var sut = CreateSut();
        sut.AvailableLanguages.Should().Contain(p => p.Id == "ca" && p.Direction == "ltr");
        sut.Apply("ca");

        sut.ActiveLanguage.Should().Be("ca");
        sut.GetString("Str.Menu.File").Should().Be("_Fitxer");
        sut.GetString("Str.Dialog.MessageBox.Button.Cancel").Should().Be("Cancel·la");
    }

    [AvaloniaFact]
    public void AvailableLanguages_IncludesGalician()
    {
        CreateSut().AvailableLanguages.Should().Contain(
            language => language.Id == "gl" && language.DisplayName == "Galician"
                && language.Direction == "ltr");
    }

    [AvaloniaTheory]
    [InlineData("gl")]
    [InlineData("gl-ES")]
    public void Apply_Galician_LoadsTranslatedResources(string id)
    {
        var sut = CreateSut();
        sut.Apply(id);

        sut.ActiveLanguage.Should().Be(id);
        sut.FlowDirection.Should().Be(FlowDirection.LeftToRight);
        sut.GetString("Str.Menu.File").Should().Be("_Ficheiro");
        Application.Current!.TryGetResource("Str.Menu.File", null, out var value).Should().BeTrue();
        value.Should().Be("_Ficheiro");
        sut.RegionsFor("gl").Select(region => region.Id).Should().Contain("gl-ES");
    }

    [AvaloniaFact]
    public void SerbianLatin_IsOfferedAsLeftToRight()
    {
        CreateSut().AvailableLanguages.Should().Contain(
            language => language.Id == "sr-Latn" && language.Direction == "ltr");
    }

    [AvaloniaFact]
    public void Apply_SerbianLatin_TranslatesWithMnemonics()
    {
        var sut = CreateSut();
        sut.Apply("sr-Latn");

        sut.GetString("Str.Menu.File").Should().Be("_Datoteka");
        sut.GetString("Str.StatusBar.Ready").Should().Be("Spremno");
        sut.GetString("Str.Dialog.MessageBox.Button.Cancel").Should().Be("Otkaži");
        sut.FlowDirection.Should().Be(FlowDirection.LeftToRight);
    }

    [AvaloniaTheory]
    [InlineData("th")]
    [InlineData("th-TH")]
    public void Apply_Thai_TranslatesWithoutMnemonics_AndUsesLeftToRight(string id)
    {
        var sut = CreateSut();
        sut.Apply(id);
        sut.GetString("Str.Menu.File").Should().Be("ไฟล์");
        sut.GetString("Str.StatusBar.Ready").Should().Be("พร้อม");
        sut.FlowDirection.Should().Be(FlowDirection.LeftToRight);
    }

    [AvaloniaFact]
    public void AvailableLanguages_OffersThaiAndThailand()
    {
        var sut = CreateSut();
        sut.AvailableLanguages.Should().Contain(new LanguageInfo("th", "Thai", "ltr"));
        sut.RegionsFor("th").Should().Contain(new HexIDE.Localization.RegionInfo("th-TH", "Thailand"));
    }

    [AvaloniaFact]
    public void ThaiPack_IsComplete_PreservesPlaceholders_AndOmitsAccessKeys()
    {
        LanguagePackRecord Load(string id)
        {
            using var stream = AssetLoader.Open(new Uri($"avares://HexIDE/Localization/Packs/{id}.json"));
            return System.Text.Json.JsonSerializer.Deserialize(stream,
                LocalizationJsonContext.Default.LanguagePackRecord)!;
        }

        var canonical = Load("en").Strings!;
        var thai = Load("th");
        thai.Name.Should().Be("Thai");
        thai.Direction.Should().Be("ltr");
        thai.Strings!.Keys.Should().BeEquivalentTo(canonical.Keys);
        var placeholders = new System.Text.RegularExpressions.Regex(@"\{[0-9]+[^{}]*\}");
        foreach (var (key, value) in thai.Strings)
        {
            placeholders.Matches(value).Select(m => m.Value).Should().BeEquivalentTo(
                placeholders.Matches(canonical[key]).Select(m => m.Value), key);
            value.Replace("VB_Name", "", StringComparison.Ordinal)
                .Replace("VB6_EXE", "", StringComparison.Ordinal)
                .Should().NotContain("_", key);
        }
    }

    [AvaloniaTheory]
    [InlineData("fr-CA", "fr", "Str.PropDesc.ToolTipText", "L'infobulle affichée lorsque le pointeur s'arrête sur le contrôle.")]
    [InlineData("es-MX", "es", "Str.PropDesc.MouseIcon", "Una imagen personalizada usada para el puntero del mouse.")]
    [InlineData("pt-BR", "pt", "Str.Menu.File", "_Arquivo")]
    public void Apply_TierOneRegion_OverridesTerminology_AndInheritsNeutral(
        string region, string neutral, string key, string expected)
    {
        var sut = CreateSut();
        sut.Apply(region);

        sut.ActiveLanguage.Should().Be(region);
        sut.GetString(key).Should().Be(expected);
        sut.GetString("Str.Menu.Edit").Should().Be(sut.GetStringFrom(neutral, "Str.Menu.Edit"));
        sut.GetStringFrom(neutral, key).Should().NotBe(expected);

        using var stream = Avalonia.Platform.AssetLoader.Open(
            new Uri($"avares://HexIDE/Localization/Packs/{region}.json"));
        using var pack = System.Text.Json.JsonDocument.Parse(stream);
        foreach (var entry in pack.RootElement.GetProperty("strings").EnumerateObject())
        {
            var value = entry.Value.GetString()!;
            var parentValue = sut.GetStringFrom(neutral, entry.Name);
            value.Should().NotBe(parentValue, $"{region} only carries genuine overrides");
            sut.GetString(entry.Name).Should().Be(value);
            System.Text.RegularExpressions.Regex.Matches(value, @"\{\d+\}").Select(m => m.Value)
                .Should().BeEquivalentTo(System.Text.RegularExpressions.Regex.Matches(parentValue, @"\{\d+\}")
                    .Select(m => m.Value), $"{region}/{entry.Name} preserves placeholders");
            if (!NavigationAccessKeyTests.CollisionPolicyKeys.Contains(entry.Name))
                value.Count(c => c == '_').Should().Be(parentValue.Count(c => c == '_'),
                    $"{region}/{entry.Name} preserves mnemonics");
        }
    }
}
