using System.IO;
using System.Text.Json;
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

    /// <summary>
    /// Packs that exist to be exercised by tests and are not translations of anything.
    /// </summary>
    /// <remarks>
    /// Named rather than pattern-matched, so a real pack cannot fall through the exemption by accident.
    /// That these ship at all is a separate defect
    /// ([#423](https://github.com/hexide-io/HexIDE/issues/423)); it is not this test's business, and
    /// excluding them here does not bless it.
    /// </remarks>
    private static readonly string[] Fixtures = ["zz", "zz-ZZ"];

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
            .Where(id => !Fixtures.Contains(id, StringComparer.Ordinal))
            .Where(IsFullTranslation)
            .Where(id => !declared.Contains(id))
            .Order(StringComparer.Ordinal)
            .ToList();

        undeclared.Should().BeEmpty(
            "a full translation on disk is either shipped, and belongs in LanguageManifest.Packs, or is "
            + "not, and should not be carried and kept current forever. Deciding is the point");
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
