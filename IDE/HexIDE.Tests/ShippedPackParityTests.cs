using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using HexIDE.Localization;

namespace HexIDE.Tests;

/// <summary>
/// The promise that makes the localisation system worth its cost: every shipped pack is complete.
/// </summary>
/// <remarks>
/// <b>It was stated as "enforced at build" and nothing enforced it</b>
/// ([#421](https://github.com/hexide-io/HexIDE/issues/421)). <see cref="LocalizationCoverageTests"/>
/// checks three things and all three point at the canonical pack — that AXAML keys exist in it, that every
/// VB6 property has a description in it, that region packs add nothing absent from it. None of them asks
/// whether the thirty translations of it are current. The only thing that did was
/// <c>tools/TranslationCoverage</c>, which is a manual <c>dotnet run</c> that always exits zero, and which
/// no workflow invokes.
///
/// <para>
/// So a missing key inherited English in silence, which is correct for a <em>user</em> and is exactly
/// what must not reach a release. This is the fail-open shape this repository has paid for twice already:
/// the <c>HEXIDE_REQUIRE_FOREIGN_LSP</c> guard that threw at discovery and turned a red suite green with
/// fourteen tests simply absent, and the coverage table that drifted into fiction until it was generated
/// and checked. A guarantee nothing verifies reads green either way.
/// </para>
///
/// <para>
/// <b>The bar is high because the cost is permanent.</b> Adding two keys costs fifty-eight translations,
/// and that arithmetic is the stated reason for refusing further languages. It only holds while the packs
/// are actually complete, so this is the test that makes the refusal honest.
/// </para>
///
/// <para>
/// <b><see cref="LanguageManifest.Packs"/> is the authority here, not the directory listing.</b> A file is
/// a fact about the disk; the manifest is the decision about what ships, and the two drifted before — which
/// is how Latin and Esperanto came to be translated in every pass before anybody had decided they were
/// shipped. Both directions are checked below, which also keeps the reporting tool honest: it infers the
/// shipped set from filenames, and that inference is only right while the two agree.
/// </para>
///
/// <para>
/// Reading from disk is faithful to what ships. <c>HexIDE.csproj</c> takes
/// <c>Localization\Packs\*.json</c> as a glob, so the files here are the resources in the assembly.
/// </para>
///
/// <para>
/// Plain <c>[Fact]</c> rather than <c>[AvaloniaFact]</c>, deliberately. Nothing here needs a dispatcher,
/// and the harness's thread-affinity fragility ([#286](https://github.com/hexide-io/HexIDE/issues/286))
/// is a good reason not to take that dependency for a test that only reads JSON.
/// </para>
/// </remarks>
public class ShippedPackParityTests
{
    /// <summary>The canonical pack every other one is measured against.</summary>
    private const string Canonical = "en";

    [Theory]
    [InlineData("Str.CodeEditor.Msg.RenameChangesReadOnlyText", "Str.CodeEditor.Msg.RenameTouchesReadOnly")]
    [InlineData("Str.CodeEditor.Msg.NoMarkOnReadOnlyText", "Str.CodeEditor.Msg.NoMarkOnReadOnlyLine")]
    [InlineData("Str.FindReplace.Msg.ReadOnlyTextOnly", "Str.FindReplace.Msg.ProtectedOnly")]
    [InlineData("Str.FindReplace.Msg.ReplacementsSkippingReadOnlyText", "Str.FindReplace.Msg.ReplacementsWithSkipped")]
    public void PeopleSeeTheIdesOwnWordsForReadOnlyText(string key, string retiredKey)
    {
        var canonical = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(PacksDirectory(), "en.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!;
        canonical.Should().ContainKey(key);
        foreach (var word in new[] { "Attribute", "header", "designer", "VB6" })
            canonical[key].Should().NotContainEquivalentOf(word);
        foreach (var file in Directory.EnumerateFiles(PacksDirectory(), "*.json"))
        {
            var pack = JsonSerializer.Deserialize(File.ReadAllText(file),
                LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!;
            pack.Should().NotContainKey(retiredKey, file);
            if (IsFullTranslation(Path.GetFileNameWithoutExtension(file)))
            {
                pack.Should().ContainKey(key, file);
                Regex.Matches(pack[key], @"\{\d+\}").Select(m => m.Value).Should().BeEquivalentTo(
                    Regex.Matches(canonical[key], @"\{\d+\}").Select(m => m.Value), file);
            }
        }
    }

    [Fact]
    public void NoPackHasKeysAbsentFromCanonicalEnglish()
    {
        var packs = PacksDirectory();
        var canonical = KeysOf(Path.Combine(packs, $"{Canonical}.json"));
        canonical.Should().NotBeEmpty("the canonical pack defines the keys that may ship");

        var unexpected = new List<string>();
        foreach (var file in Directory.EnumerateFiles(packs, "*.json"))
        {
            unexpected.AddRange(KeysOf(file).Except(canonical).Order(StringComparer.Ordinal)
                .Select(key => $"{Path.GetFileName(file)}: {key}"));
        }

        unexpected.Should().BeEmpty("all packs, including regional overrides, use only canonical English keys");
    }

    [Fact]
    public void PackAmpersandsAreLiteralRatherThanAccessKeyMarkers()
    {
        string[] literalKeys =
        [
            "Str.PropDesc.UseMnemonic",
            "Str.Tool.LanguageServers.Title",
            "Str.Tool.ProtocolInspector.NoBody.Unarmed",
        ];
        var unexpected = new List<string>();
        foreach (var file in Directory.EnumerateFiles(PacksDirectory(), "*.json"))
        {
            var pack = JsonSerializer.Deserialize(File.ReadAllText(file),
                LocalizationJsonContext.Default.LanguagePackRecord)!;
            unexpected.AddRange(pack.Strings!
                .Where(pair => pair.Value.Contains('&') && !literalKeys.Contains(pair.Key))
                .Select(pair => $"{Path.GetFileName(file)}: {pair.Key}"));
        }

        unexpected.Should().BeEmpty("Avalonia access keys are underscores, never ampersands (#347)");
    }

    [Fact]
    public void TheBrandRewriteNeverReachesAUrlOrAPathInAPack()
    {
        // BrandText takes the literal product name in a pack as the place a distribution's own name goes (#2042). A link
        // or a folder spelled with that name would come out broken in every branded build, and nothing else would say so.
        const string brand = "BRANDNAME";
        var broken = new List<string>();
        foreach (var file in Directory.EnumerateFiles(PacksDirectory(), "*.json"))
        {
            var strings = JsonSerializer.Deserialize(File.ReadAllText(file),
                LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!;
            BrandText.Apply(strings, brand);
            broken.AddRange(strings
                .Where(pair => PutsNameInAUrlOrAPath(pair.Value, brand))
                .Select(pair => $"{Path.GetFileName(file)}: {pair.Key}"));
        }

        broken.Should().BeEmpty("a URL or a path must not spell the product name where BrandText would rewrite it");
    }

    [Theory]
    [InlineData("See https://github.com/hexide-io/BRANDNAME for more.", true)]
    [InlineData(@"Look in logs\BRANDNAME\today.", true)]
    [InlineData("Look in ~/.config/BRANDNAME.", true)]
    [InlineData("About BRANDNAME...", false)]
    [InlineData("BRANDNAME's safety", false)]
    [InlineData("Either/or, in BRANDNAME", false)]
    public void TheUrlOrPathGuardCatchesTheNameInsideOneOnly(string text, bool caught) =>
        PutsNameInAUrlOrAPath(text, "BRANDNAME").Should().Be(caught);

    private static bool PutsNameInAUrlOrAPath(string text, string brand) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Any(word => word.Contains(brand, StringComparison.Ordinal) && (word.Contains('/') || word.Contains('\\')));

    [Fact]
    public void EveryShippedPackNameMatchesItsEnglishManifestName()
    {
        foreach (var language in LanguageManifest.Packs)
        {
            var pack = JsonSerializer.Deserialize(
                File.ReadAllText(Path.Combine(PacksDirectory(), $"{language.Id}.json")),
                LocalizationJsonContext.Default.LanguagePackRecord)!;
            pack.Name.Should().Be(language.DisplayName, $"pack {language.Id} uses its English name");
        }
    }

    [Fact]
    public void EveryShippedProgressFormatIncludesServerTaskAndPercentage()
    {
        foreach (var id in LanguageManifest.Packs.Select(p => p.Id))
        {
            var pack = JsonSerializer.Deserialize(
                File.ReadAllText(Path.Combine(PacksDirectory(), $"{id}.json")),
                LocalizationJsonContext.Default.LanguagePackRecord)!;
            if (!IsFullTranslation(id)) continue;
            var format = pack.Strings!["Str.StatusBar.LanguageServerProgress"];
            string.Format(format, "SERVER", "TASK", "PERCENTAGE").Should()
                .Contain("SERVER").And.Contain("TASK").And.Contain("PERCENTAGE", $"pack {id}");
        }
    }

    [Fact]
    public void EveryShippedTranslationHasEveryCanonicalKey()
    {
        var packs = PacksDirectory();
        var canonical = KeysOf(Path.Combine(packs, $"{Canonical}.json"));

        canonical.Should().NotBeEmpty("the canonical pack is what everything else is measured against");

        var stale = new List<string>();

        foreach (var id in ShippedTranslations())
        {
            var file = Path.Combine(packs, $"{id}.json");
            if (!File.Exists(file)) continue;   // reported by its own test, with its own explanation

            var missing = canonical.Except(KeysOf(file)).Order(StringComparer.Ordinal).ToList();
            if (missing.Count == 0) continue;

            // Capped. Thirty packs each missing a hundred keys is not a failure message anybody reads,
            // and the count plus the first few is enough to say which change left them behind.
            var shown = string.Join(", ", missing.Take(5));
            var rest = missing.Count > 5 ? $", and {missing.Count - 5} more" : "";
            stale.Add($"{id} is missing {missing.Count} of {canonical.Count}: {shown}{rest}");
        }

        stale.Should().BeEmpty(
            "every shipped pack is 100% complete, and a key added to en is not finished until all of them "
            + "have it. Run `cd tools/TranslationCoverage && dotnet run` for the full list, and see "
            + "CLAUDE.md under Localization for the one-agent-per-pack workflow");
    }

    [Fact]
    public void EveryShippedTranslationHasAPack()
    {
        // A manifest entry with no file offers the language in the combo and then resolves every string
        // through to English. Nothing errors, nothing logs, and the pack looks untranslated rather than
        // absent — so the language appears shipped and does nothing.
        var packs = PacksDirectory();

        var absent = ShippedTranslations()
            .Where(id => !File.Exists(Path.Combine(packs, $"{id}.json")))
            .ToList();

        absent.Should().BeEmpty(
            "a language offered in the manifest with no pack behind it silently renders as English");
    }

    [Fact]
    public void EveryTranslationOnDiskIsDeclaredAsShipped()
    {
        // The inverse, and the one with history. A full-translation pack nobody declared still gets
        // translated in every pass, because a pass works from the directory — which is how Latin and
        // Esperanto came to be complete before anyone had decided they were shipped. It also keeps
        // tools/TranslationCoverage correct, since that infers the shipped set from these filenames.
        // The whole manifest, canonical included. `en` ships and is declared; it is only excluded from
        // ShippedTranslations because it is the thing the others are measured against.
        var declared = LanguageManifest.Packs.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);

        var undeclared = Directory
            .EnumerateFiles(PacksDirectory(), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(id => id is not null)
            .Select(id => id!)
            .Where(IsFullTranslation)
            .Where(id => !declared.Contains(id))
            .Order(StringComparer.Ordinal)
            .ToList();

        undeclared.Should().BeEmpty(
            "a full translation on disk is either shipped, and belongs in LanguageManifest.Packs, or is "
            + "not, and should not be carried and kept current forever. Deciding is the point");
    }

    [Fact]
    public void RomanianPack_IsCompleteAndPreservesPlaceholdersAndAccessKeys()
    {
        var canonical = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(PacksDirectory(), "en.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;
        var romanian = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(PacksDirectory(), "ro.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;

        romanian.Name.Should().Be("Romanian");
        romanian.Direction.Should().Be("ltr");
        romanian.Strings!.Keys.Should().BeEquivalentTo(canonical.Strings!.Keys);
        foreach (var (key, english) in canonical.Strings)
        {
            var translated = romanian.Strings[key];
            translated.Should().NotBeNullOrWhiteSpace(key);
            Regex.Matches(translated, @"\{\d+\}").Select(m => m.Value).Should()
                .BeEquivalentTo(Regex.Matches(english, @"\{\d+\}").Select(m => m.Value), key);
            if (!NavigationAccessKeyTests.CollisionPolicyKeys.Contains(key))
                translated.Count(c => c == '_').Should().Be(english.Count(c => c == '_'), key);
        }
    }

    [Fact]
    public void Hungarian_PreservesCanonicalKeysPlaceholdersAndAccessKeys()
    {
        var canonical = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(PacksDirectory(), "en.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!;
        var pack = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(PacksDirectory(), "hu.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;

        pack.Name.Should().Be("Hungarian");
        pack.Direction.Should().Be("ltr");
        pack.Strings!.Keys.Should().BeEquivalentTo(canonical.Keys);
        foreach (var (key, original) in canonical)
        {
            var translated = pack.Strings[key];
            translated.Should().NotBeNullOrWhiteSpace(key);
            System.Text.RegularExpressions.Regex.Matches(translated, @"\{[0-9]+[^}]*\}")
                .Select(m => m.Value).Should().BeEquivalentTo(
                    System.Text.RegularExpressions.Regex.Matches(original, @"\{[0-9]+[^}]*\}")
                        .Select(m => m.Value), key);
            if (!NavigationAccessKeyTests.CollisionPolicyKeys.Contains(key))
                translated.Count(c => c == '_').Should().Be(original.Count(c => c == '_'), key);
        }
    }

    [Fact]
    public void BulgarianPackPreservesCanonicalPlaceholdersWithoutAccessKeyMarkers()
    {
        LanguageManifest.Packs.Should().Contain(p => p.Id == "bg" && p.Direction == "ltr");
        LanguageManifest.Regions["bg"].Should().Contain(r => r.Id == "bg-BG");
        var packs = PacksDirectory();
        var canonical = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(packs, "en.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;
        var bulgarian = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(packs, "bg.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;

        bulgarian.Name.Should().Be("Bulgarian");
        bulgarian.Direction.Should().Be("ltr");
        bulgarian.Strings!.Keys.Should().BeEquivalentTo(canonical.Strings!.Keys);
        foreach (var (key, value) in canonical.Strings)
        {
            var translated = bulgarian.Strings[key];
            translated.Should().NotBeNullOrWhiteSpace(key);
            translated.Replace("VB6_EXE", "").Replace("VB_Name", "").Should().NotContain("_", key);
            System.Text.RegularExpressions.Regex.Matches(translated, @"\{\d+[^}]*\}")
                .Select(m => m.Value).Should().BeEquivalentTo(
                    System.Text.RegularExpressions.Regex.Matches(value, @"\{\d+[^}]*\}")
                        .Select(m => m.Value), key);
        }
    }

    [Fact]
    public void SlovakPack_PreservesCanonicalKeysPlaceholdersAndAccessKeys()
    {
        var packs = PacksDirectory();
        var canonical = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(packs, "en.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;
        var slovak = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(packs, "sk.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;

        slovak.Name.Should().Be("Slovak");
        slovak.Direction.Should().Be("ltr");
        slovak.Strings!.Keys.Should().BeEquivalentTo(canonical.Strings!.Keys);
        foreach (var (key, value) in canonical.Strings)
        {
            var translated = slovak.Strings[key];
            translated.Should().NotBeNullOrWhiteSpace(key);
            Regex.Matches(translated, @"\{[0-9]+[^}]*\}").Select(m => m.Value).Should()
                .BeEquivalentTo(Regex.Matches(value, @"\{[0-9]+[^}]*\}").Select(m => m.Value), key);
            if (!NavigationAccessKeyTests.CollisionPolicyKeys.Contains(key))
                translated.Count(c => c == '_').Should().Be(value.Count(c => c == '_'), key);
        }
    }

    [Fact]
    public void Slovak_IsDeclaredWithSlovakiaAsAFilelessRegion()
    {
        LanguageManifest.Packs.Should().Contain(new LanguageInfo("sk", "Slovak", "ltr"));
        LanguageManifest.Regions.Should().ContainKey("sk");
        LanguageManifest.Regions["sk"].Should().Contain(new RegionInfo("sk-SK", "Slovakia"));
        File.Exists(Path.Combine(PacksDirectory(), "sk-SK.json")).Should().BeFalse();
    }

    [Fact]
    public void Lithuanian_PreservesCanonicalPlaceholdersAndAccessKeys()
    {
        var packs = PacksDirectory();
        var canonical = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(packs, "en.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;
        var lithuanian = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(packs, "lt.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;

        lithuanian.Strings!.Keys.Should().BeEquivalentTo(canonical.Strings!.Keys);
        foreach (var (key, original) in canonical.Strings)
        {
            var translated = lithuanian.Strings[key];
            translated.Should().NotBeNullOrWhiteSpace($"Lithuanian key {key}");
            System.Text.RegularExpressions.Regex.Matches(translated, @"\{\d+[^}]*\}")
                .Select(m => m.Value).Should().BeEquivalentTo(
                    System.Text.RegularExpressions.Regex.Matches(original, @"\{\d+[^}]*\}")
                        .Select(m => m.Value), $"Lithuanian key {key}");
            if (!NavigationAccessKeyTests.CollisionPolicyKeys.Contains(key))
                translated.Count(c => c == '_').Should().Be(original.Count(c => c == '_'),
                    $"Lithuanian key {key} keeps the Latin-script access-key convention");
        }
    }

    [Fact]
    public void Latvian_PreservesEveryPlaceholderAndAccessKey()
    {
        var packs = PacksDirectory();
        var canonical = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(packs, "en.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;
        var latvian = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(packs, "lv.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;

        latvian.Strings!.Keys.Should().BeEquivalentTo(canonical.Strings!.Keys);
        foreach (var (key, original) in canonical.Strings)
        {
            var translated = latvian.Strings[key];
            translated.Should().NotBeNullOrWhiteSpace(key);
            var placeholders = System.Text.RegularExpressions.Regex.Matches(original, @"\{\d+\}")
                .Select(m => m.Value);
            System.Text.RegularExpressions.Regex.Matches(translated, @"\{\d+\}")
                .Select(m => m.Value).Should().BeEquivalentTo(placeholders, key);
            if (!NavigationAccessKeyTests.CollisionPolicyKeys.Contains(key))
                translated.Count(c => c == '_').Should().Be(original.Count(c => c == '_'), key);
        }
    }

    [Fact]
    public void EstonianPack_PreservesCanonicalKeysPlaceholdersAndAccessKeys()
    {
        var packs = PacksDirectory();
        var canonical = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(packs, "en.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;
        var estonian = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(packs, "et.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;

        estonian.Name.Should().Be("Estonian");
        estonian.Direction.Should().Be("ltr");
        estonian.Strings!.Keys.Should().BeEquivalentTo(canonical.Strings!.Keys);
        foreach (var (key, original) in canonical.Strings)
        {
            var translated = estonian.Strings[key];
            translated.Should().NotBeNullOrWhiteSpace(key);
            Regex.Matches(translated, @"\{\d+[^{}]*\}").Select(m => m.Value).Should()
                .BeEquivalentTo(Regex.Matches(original, @"\{\d+[^{}]*\}").Select(m => m.Value), key);
            if (original.Contains('_') && !NavigationAccessKeyTests.CollisionPolicyKeys.Contains(key))
                translated.Should().Contain("_", key);
        }
    }

    [Fact]
    public void Basque_PreservesPlaceholdersAndLatinAccessKeys()
    {
        var packs = PacksDirectory();
        var canonical = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(packs, "en.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;
        var basque = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(packs, "eu.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;

        basque.Name.Should().Be("Basque");
        basque.Direction.Should().Be("ltr");
        basque.Strings!.Keys.Should().BeEquivalentTo(canonical.Strings!.Keys);
        foreach (var (key, value) in canonical.Strings)
        {
            var translated = basque.Strings[key];
            translated.Should().NotBeNullOrWhiteSpace(key);
            System.Text.RegularExpressions.Regex.Matches(translated, @"\{\d+\}")
                .Select(m => m.Value).Should().BeEquivalentTo(
                    System.Text.RegularExpressions.Regex.Matches(value, @"\{\d+\}")
                        .Select(m => m.Value), key);
            if (value.Contains('_') && !NavigationAccessKeyTests.CollisionPolicyKeys.Contains(key))
                translated.Count(c => c == '_').Should().Be(value.Count(c => c == '_'), key);
        }
    }

    [Fact]
    public void Catalan_PreservesCanonicalPlaceholdersAndAccessKeys()
    {
        var packs = PacksDirectory();
        var canonical = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(packs, "en.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;
        var catalan = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(packs, "ca.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;

        catalan.Name.Should().Be("Catalan");
        catalan.Direction.Should().Be("ltr");
        foreach (var (key, value) in canonical.Strings!)
        {
            catalan.Strings.Should().ContainKey(key);
            var translated = catalan.Strings![key];
            translated.Should().NotBeNullOrWhiteSpace($"Catalan key {key}");
            Regex.Matches(translated, @"\{\d+\}")
                .Select(m => m.Value).Should().BeEquivalentTo(
                    Regex.Matches(value, @"\{\d+\}")
                        .Select(m => m.Value), $"Catalan key {key}");
            if (!NavigationAccessKeyTests.CollisionPolicyKeys.Contains(key))
                translated.Count(c => c == '_').Should().Be(value.Count(c => c == '_'),
                    $"Catalan key {key} must preserve access keys");
        }
    }

    [Fact]
    public void Galician_UsesEnglishOnlyForSharedTermsAndFormats()
    {
        var packs = PacksDirectory();
        var canonical = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(packs, "en.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;
        var galician = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(packs, "gl.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;

        galician.Name.Should().Be("Galician");
        // Shared words, VB syntax, abbreviations and formats intentionally remain identical (#2774).
        galician.Strings!.Where(pair => pair.Value == canonical.Strings![pair.Key])
            .Select(pair => pair.Key).Should().BeEquivalentTo(new[]
            {
                "Str.Tool.FindReferences.Error",
                "Str.CodeEditor.ViewAs.Vb6", "Str.Tool.Locals.Ellipsis",
                "Str.Dialog.AddProcedure.Type.Sub", "Str.Dialog.MenuEditor.Visible",
                "Str.Tool.ProtocolInspector.DetailHeader", "Str.StatusBar.Insert",
                "Str.StatusBar.Automation.Text", "Str.StatusBar.Automation.TextSession",
                "Str.StatusBar.LanguageServerProgress", "Str.Options.Node.Editor",
                "Str.Document.UserControlSuffix", "Str.Document.PropertyPageSuffix",
                "Str.Dialog.Color.Rgb",
                "Str.Tool.Outline.Kind.Interface", "Str.Tool.Outline.Kind.Variable",
                "Str.Dock.DocumentTabStripItemLayoutModeMdi",
                "Str.Tool.LanguageServers.Vm.Age.Seconds",
                "Str.Tool.LanguageServers.Vm.Elapsed",
            });
    }

    [Fact]
    public void Galician_PreservesCanonicalPlaceholdersAndAccessKeys()
    {
        var packs = PacksDirectory();
        var canonical = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(packs, "en.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;
        var galician = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(packs, "gl.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;

        galician.Strings!.Keys.Should().BeEquivalentTo(canonical.Strings!.Keys);
        foreach (var (key, value) in canonical.Strings)
        {
            var translated = galician.Strings[key];
            translated.Should().NotBeNullOrWhiteSpace(key);
            System.Text.RegularExpressions.Regex.Matches(translated, @"\{\d+(?:[^{}]*)\}")
                .Select(match => match.Value).Should().BeEquivalentTo(
                    System.Text.RegularExpressions.Regex.Matches(value, @"\{\d+(?:[^{}]*)\}")
                        .Select(match => match.Value), key);
            if (!NavigationAccessKeyTests.CollisionPolicyKeys.Contains(key))
                translated.Count(character => character == '_').Should()
                    .Be(value.Count(character => character == '_'), key);
        }
    }

    [Fact]
    public void SerbianLatin_PreservesEveryPlaceholderAndAccessKey()
    {
        var packs = PacksDirectory();
        var canonical = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(packs, "en.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;
        var serbian = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(packs, "sr-Latn.json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!;

        foreach (var (key, value) in canonical.Strings!)
        {
            serbian.Strings.Should().ContainKey(key);
            var translated = serbian.Strings![key];
            Regex.Matches(translated, @"\{[0-9]+\}")
                .Select(match => match.Value).Should().BeEquivalentTo(
                    Regex.Matches(value, @"\{[0-9]+\}")
                        .Select(match => match.Value), key);
            if (!NavigationAccessKeyTests.CollisionPolicyKeys.Contains(key))
                translated.Count(c => c == '_').Should().Be(value.Count(c => c == '_'), key);
        }
    }

    /// <summary>
    /// The translations that ship, canonical excluded: what each of them is measured against.
    /// </summary>
    private static IEnumerable<string> ShippedTranslations() =>
        LanguageManifest.Packs
            .Select(p => p.Id)
            .Where(id => !string.Equals(id, Canonical, StringComparison.Ordinal));

    /// <summary>
    /// Whether a pack id names a full translation rather than a regional variant.
    /// </summary>
    /// <remarks>
    /// A region pack overrides only what differs from its neutral — <c>en-GB</c> carries eighteen keys of
    /// seven hundred by design — so measuring one against the canonical pack would report a deliberate
    /// choice as a defect. Chinese is the exception in both directions: it ships two <em>script</em>
    /// neutrals rather than a bare <c>zh</c>, and both are full translations despite the subtag.
    /// </remarks>
    private static bool IsFullTranslation(string id) =>
        !id.Contains('-') || id is "zh-Hans" or "zh-Hant";

    private static HashSet<string> KeysOf(string packPath)
    {
        var pack = JsonSerializer.Deserialize(
            File.ReadAllText(packPath), LocalizationJsonContext.Default.LanguagePackRecord);

        return pack?.Strings is { } strings ? [.. strings.Keys] : [];
    }

    private static string PacksDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var packs = Path.Combine(dir.FullName, "IDE", "HexIDE", "Localization", "Packs");
            if (Directory.Exists(packs)) return packs;
        }

        throw new DirectoryNotFoundException(
            "Could not locate IDE/HexIDE/Localization/Packs from " + AppContext.BaseDirectory);
    }
}
