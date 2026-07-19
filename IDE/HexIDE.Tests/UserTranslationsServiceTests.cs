using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HexIDE.Localization;

namespace HexIDE.Tests;

/// <summary>
/// Exercises <see cref="UserTranslationsService"/> against a real temp directory via its internal
/// test/dir constructor. Each test owns a uniquely-named subfolder (derived from the test name, so the
/// directory is stable and isolated per case — no random/time seed). The service loads the real
/// <c>en.json</c> canonical pack via the Avalonia <c>AssetLoader</c>, so headless Avalonia must be up.
/// </summary>
public class UserTranslationsServiceTests
{
    // A canonical en key that carries a single {0} placeholder — used for the placeholder-preservation cases.
    private const string PlaceholderKey = "Str.RevertGate.Countdown";

    // A canonical en key with no placeholders — used for the plain write/clear/reset round-trips.
    private const string PlainKey = "Str.Menu.File";

    public UserTranslationsServiceTests() => AvaloniaTestSetup.EnsureInitialized();

    /// <summary>A fresh, empty temp directory keyed off the calling test's name (deterministic, isolated).</summary>
    private static string FreshDir([CallerMemberName] string testName = "")
    {
        var dir = Path.Combine(Path.GetTempPath(), "HexIDE.Tests.UserTranslations", testName);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string FilePath(string dir, string id) => Path.Combine(dir, $"{id}.json");

    private static Dictionary<string, string>? ReadStrings(string dir, string id)
    {
        var path = FilePath(dir, id);
        if (!File.Exists(path)) return null;
        var record = JsonSerializer.Deserialize(File.ReadAllText(path), LocalizationJsonContext.Default.LanguagePackRecord);
        return record?.Strings is null ? null : new Dictionary<string, string>(record.Strings);
    }

    // ── SetOverride / GetOverrides round-trip ──────────────────────────────

    [Fact]
    public void SetOverride_WritesFileAndRoundTripsViaGetOverrides()
    {
        var dir = FreshDir();
        var sut = new UserTranslationsService(dir);

        sut.SetOverride("de", PlainKey, "DeiFile").Should().BeTrue();

        sut.GetOverrides("de").Should().Contain(new KeyValuePair<string, string>(PlainKey, "DeiFile"));

        // A {de}.json was written and round-trips on a fresh load.
        File.Exists(FilePath(dir, "de")).Should().BeTrue();
        ReadStrings(dir, "de").Should().Contain(new KeyValuePair<string, string>(PlainKey, "DeiFile"));

        var reloaded = new UserTranslationsService(dir);
        reloaded.GetOverrides("de").Should().Contain(new KeyValuePair<string, string>(PlainKey, "DeiFile"));
    }

    [Fact]
    public void SetOverride_RaisesOverridesChanged_WithLocaleId()
    {
        var dir = FreshDir();
        var sut = new UserTranslationsService(dir);
        string? raisedFor = null;
        sut.OverridesChanged += id => raisedFor = id;

        sut.SetOverride("fr", PlainKey, "Fichier").Should().BeTrue();

        raisedFor.Should().Be("fr");
    }

    [Fact]
    public void GetOverrides_UnknownLocale_ReturnsEmpty()
    {
        var sut = new UserTranslationsService(FreshDir());

        sut.GetOverrides("xx-YY").Should().BeEmpty();
    }

    // ── null/whitespace value clears the key ───────────────────────────────

    [Fact]
    public void SetOverride_NullValue_ClearsTheKey_AndReturnsTrue()
    {
        var dir = FreshDir();
        var sut = new UserTranslationsService(dir);
        sut.SetOverride("de", PlainKey, "DeiFile").Should().BeTrue();

        sut.SetOverride("de", PlainKey, null).Should().BeTrue();

        sut.GetOverrides("de").Should().NotContainKey(PlainKey);
    }

    [Fact]
    public void SetOverride_WhitespaceValue_ClearsTheKey_AndReturnsTrue()
    {
        var dir = FreshDir();
        var sut = new UserTranslationsService(dir);
        sut.SetOverride("de", PlainKey, "DeiFile").Should().BeTrue();

        sut.SetOverride("de", PlainKey, "   ").Should().BeTrue();

        sut.GetOverrides("de").Should().NotContainKey(PlainKey);
    }

    // ── ClearOverride removes the key, deletes the file when empty ──────────

    [Fact]
    public void ClearOverride_RemovesKey_AndDeletesFileWhenEmpty()
    {
        var dir = FreshDir();
        var sut = new UserTranslationsService(dir);
        sut.SetOverride("de", PlainKey, "DeiFile").Should().BeTrue();
        File.Exists(FilePath(dir, "de")).Should().BeTrue();

        sut.ClearOverride("de", PlainKey);

        sut.GetOverrides("de").Should().NotContainKey(PlainKey);
        File.Exists(FilePath(dir, "de")).Should().BeFalse("the file must be deleted once the last key is cleared");
    }

    [Fact]
    public void ClearOverride_KeepsFile_WhenOtherKeysRemain()
    {
        var dir = FreshDir();
        var sut = new UserTranslationsService(dir);
        sut.SetOverride("de", PlainKey, "DeiFile").Should().BeTrue();
        sut.SetOverride("de", PlaceholderKey, "Zurück in {0}s").Should().BeTrue();

        sut.ClearOverride("de", PlainKey);

        sut.GetOverrides("de").Should().NotContainKey(PlainKey);
        sut.GetOverrides("de").Should().ContainKey(PlaceholderKey);
        File.Exists(FilePath(dir, "de")).Should().BeTrue("the file survives while another override remains");
    }

    [Fact]
    public void ClearOverride_RaisesOverridesChanged()
    {
        var dir = FreshDir();
        var sut = new UserTranslationsService(dir);
        sut.SetOverride("de", PlainKey, "DeiFile").Should().BeTrue();
        string? raisedFor = null;
        sut.OverridesChanged += id => raisedFor = id;

        sut.ClearOverride("de", PlainKey);

        raisedFor.Should().Be("de");
    }

    // ── ResetAll deletes the file and forgets the locale ───────────────────

    [Fact]
    public void ResetAll_RemovesAllOverrides_AndDeletesFile()
    {
        var dir = FreshDir();
        var sut = new UserTranslationsService(dir);
        sut.SetOverride("de", PlainKey, "DeiFile").Should().BeTrue();
        sut.SetOverride("de", PlaceholderKey, "Zurück in {0}s").Should().BeTrue();
        File.Exists(FilePath(dir, "de")).Should().BeTrue();

        sut.ResetAll("de");

        sut.GetOverrides("de").Should().BeEmpty();
        sut.OverriddenIds.Should().NotContain("de");
        File.Exists(FilePath(dir, "de")).Should().BeFalse();
    }

    [Fact]
    public void OverriddenIds_ReflectsLocalesWithOverrides()
    {
        var dir = FreshDir();
        var sut = new UserTranslationsService(dir);
        sut.SetOverride("de", PlainKey, "DeiFile").Should().BeTrue();
        sut.SetOverride("fr", PlainKey, "Fichier").Should().BeTrue();

        sut.OverriddenIds.Should().Contain(["de", "fr"]);
    }

    // ── PreservesPlaceholders ──────────────────────────────────────────────

    [Fact]
    public void PreservesPlaceholders_True_ForSamePlaceholderEdit()
    {
        var sut = new UserTranslationsService(FreshDir());

        // Canonical "Reverting in {0}s…" → a translation that keeps the single {0}.
        sut.PreservesPlaceholders(PlaceholderKey, "Zurück in {0} Sekunden").Should().BeTrue();
    }

    [Fact]
    public void PreservesPlaceholders_True_ForMnemonicAndPunctuationChange()
    {
        var sut = new UserTranslationsService(FreshDir());

        // The plain key has no placeholders, so an added mnemonic/punctuation change is fine.
        sut.PreservesPlaceholders(PlainKey, "_Fichier…").Should().BeTrue();
        // And the {0} key still preserves its index when the surrounding mnemonic/punctuation changes.
        sut.PreservesPlaceholders(PlaceholderKey, "_Reverting in {0}s!").Should().BeTrue();
    }

    [Fact]
    public void PreservesPlaceholders_False_WhenPlaceholderDropped()
    {
        var sut = new UserTranslationsService(FreshDir());

        // The canonical key requires {0}; a candidate that drops it must fail.
        sut.PreservesPlaceholders(PlaceholderKey, "Reverting now…").Should().BeFalse();
    }

    [Fact]
    public void PreservesPlaceholders_False_WhenExtraPlaceholderAdded()
    {
        var sut = new UserTranslationsService(FreshDir());

        // Canonical has only {0}; adding {1} breaks string.Format at runtime → must fail.
        sut.PreservesPlaceholders(PlaceholderKey, "Reverting {0} of {1}…").Should().BeFalse();
    }

    [Fact]
    public void PreservesPlaceholders_False_ForStrayUnescapedBrace_EvenWhenIndexSetMatches()
    {
        var sut = new UserTranslationsService(FreshDir());

        // Index set still {0}, but a trailing lone '}' is a malformed composite-format string that throws
        // FormatException at the call site — the gate must reject it before it can be persisted/applied.
        sut.PreservesPlaceholders(PlaceholderKey, "Zurück in {0}s }").Should().BeFalse();
        sut.PreservesPlaceholders(PlaceholderKey, "Zurück in {0}s {").Should().BeFalse();
    }

    [Fact]
    public void PreservesPlaceholders_False_ForStrayBrace_OnNoPlaceholderCanonicalKey()
    {
        var sut = new UserTranslationsService(FreshDir());

        // The plain key has no placeholders, but a lone '{' still breaks string.Format wherever the value
        // is formatted — it must be rejected, not waved through as "any non-{N} text ok".
        sut.PreservesPlaceholders(PlainKey, "100{ off").Should().BeFalse();
    }

    [Fact]
    public void PreservesPlaceholders_True_ForEscapedBraces()
    {
        var sut = new UserTranslationsService(FreshDir());

        // Doubled braces are valid escaped literals and must pass the well-formedness probe.
        sut.PreservesPlaceholders(PlainKey, "Use {{curly}} braces").Should().BeTrue();
    }

    [Fact]
    public void SetOverride_StrayBrace_ReturnsFalse_AndDoesNotPersist()
    {
        var dir = FreshDir();
        var sut = new UserTranslationsService(dir);

        sut.SetOverride("de", PlaceholderKey, "Zurück in {0}s }").Should().BeFalse();

        sut.GetOverrides("de").Should().NotContainKey(PlaceholderKey);
        File.Exists(FilePath(dir, "de")).Should().BeFalse();
    }

    [Fact]
    public void LoadAll_DropsStrayBraceValues()
    {
        var dir = FreshDir();
        WritePackFile(dir, "de", new()
        {
            [PlainKey] = "DeiFile",                  // kept
            [PlaceholderKey] = "Zurück in {0}s }",   // index set matches but malformed → dropped
        });

        var sut = new UserTranslationsService(dir);

        sut.GetOverrides("de").Should().ContainKey(PlainKey);
        sut.GetOverrides("de").Should().NotContainKey(PlaceholderKey);
    }

    [Fact]
    public void SetOverride_PlaceholderBreaker_ReturnsFalse_AndDoesNotPersist()
    {
        var dir = FreshDir();
        var sut = new UserTranslationsService(dir);

        sut.SetOverride("de", PlaceholderKey, "Reverting now…").Should().BeFalse();

        sut.GetOverrides("de").Should().NotContainKey(PlaceholderKey);
        File.Exists(FilePath(dir, "de")).Should().BeFalse();
    }

    // ── LoadAll drops bad entries / survives corruption ────────────────────

    [Fact]
    public void LoadAll_DropsOrphanKeysNotInCanonical()
    {
        var dir = FreshDir();
        WritePackFile(dir, "de", new()
        {
            [PlainKey] = "DeiFile",                       // valid canonical key — kept
            ["Str.Not.A.Real.Key"] = "ignored orphan",   // not in en → dropped
        });

        var sut = new UserTranslationsService(dir);

        sut.GetOverrides("de").Should().ContainKey(PlainKey);
        sut.GetOverrides("de").Should().NotContainKey("Str.Not.A.Real.Key");
    }

    [Fact]
    public void LoadAll_DropsEmptyAndWhitespaceValues()
    {
        var dir = FreshDir();
        WritePackFile(dir, "de", new()
        {
            [PlainKey] = "DeiFile",          // kept
            [PlaceholderKey] = "   ",        // whitespace value → dropped
        });

        var sut = new UserTranslationsService(dir);

        sut.GetOverrides("de").Should().ContainKey(PlainKey);
        sut.GetOverrides("de").Should().NotContainKey(PlaceholderKey);
    }

    [Fact]
    public void LoadAll_DropsPlaceholderBreakers()
    {
        var dir = FreshDir();
        WritePackFile(dir, "de", new()
        {
            [PlainKey] = "DeiFile",                  // kept
            [PlaceholderKey] = "Reverting now…",     // {0} dropped → placeholder-breaker → dropped
        });

        var sut = new UserTranslationsService(dir);

        sut.GetOverrides("de").Should().ContainKey(PlainKey);
        sut.GetOverrides("de").Should().NotContainKey(PlaceholderKey);
    }

    [Fact]
    public void LoadAll_SurvivesCorruptJsonFile_WithoutThrowing()
    {
        var dir = FreshDir();
        // A garbage file for one locale and a valid file for another.
        File.WriteAllText(FilePath(dir, "de"), "{ this is not valid json ]]] ");
        WritePackFile(dir, "fr", new() { [PlainKey] = "Fichier" });

        var act = () => new UserTranslationsService(dir);

        act.Should().NotThrow();
        var sut = new UserTranslationsService(dir);
        sut.GetOverrides("de").Should().BeEmpty();                                       // corrupt → skipped
        sut.GetOverrides("fr").Should().Contain(new KeyValuePair<string, string>(PlainKey, "Fichier"));
    }

    /// <summary>Writes a raw <see cref="LanguagePackRecord"/> file, bypassing the service's write-time validation
    /// so that LoadAll-time filtering can be exercised on deliberately-bad content.</summary>
    private static void WritePackFile(string dir, string id, Dictionary<string, string> strings)
    {
        var record = new LanguagePackRecord(id, null, strings);
        var json = JsonSerializer.Serialize(record, LocalizationJsonContext.Default.LanguagePackRecord);
        File.WriteAllText(FilePath(dir, id), json);
    }
}
