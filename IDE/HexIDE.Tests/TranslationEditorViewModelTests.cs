using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Tools.TranslationEditor;

namespace HexIDE.Tests;

/// <summary>
/// Exercises <see cref="TranslationEditorViewModel"/> against a <b>real</b> <see cref="LocalizationService"/>
/// (so the From-reference values and the red en-fallback rule come from the shipped packs) and a small
/// in-memory fake of <see cref="IUserTranslationsService"/> (so override write/clear round-trips and the
/// placeholder-breaker rejection are observable without touching disk). Headless Avalonia must be up because
/// the VM loads the canonical <c>en.json</c> key list via the <c>AssetLoader</c>.
/// </summary>
public class TranslationEditorViewModelTests
{
    public TranslationEditorViewModelTests() => AvaloniaTestSetup.EnsureInitialized();

    // ── Test doubles ───────────────────────────────────────────────────────

    /// <summary>
    /// A minimal, stateful stand-in for <see cref="IUserTranslationsService"/>. It mirrors the real
    /// service's contract that matters to the VM: <see cref="SetOverride"/> rejects (returns false, no
    /// write) a candidate that breaks the canonical key's placeholder set, a null/whitespace value clears,
    /// and <see cref="GetOverrides"/> reflects the live store so the VM's <c>IsOverridden</c> tracks it.
    /// It also records its calls so the tests can assert the VM routed through the right method.
    /// </summary>
    private sealed class FakeUserTranslationsService : IUserTranslationsService
    {
        // Canonical (en) map, loaded the same way the real service validates placeholder preservation.
        private readonly IReadOnlyDictionary<string, string> _canonical;
        private readonly Dictionary<string, Dictionary<string, string>> _byLocale = new();

        public List<(string Id, string Key, string? Value)> SetOverrideCalls { get; } = new();
        public List<(string Id, string Key)> ClearOverrideCalls { get; } = new();

        public FakeUserTranslationsService(IReadOnlyDictionary<string, string> canonical) => _canonical = canonical;

        public void LoadAll() { }

        public IReadOnlyDictionary<string, string> GetOverrides(string id) =>
            _byLocale.TryGetValue(id, out var map)
                ? new Dictionary<string, string>(map)
                : new Dictionary<string, string>();

        public bool SetOverride(string id, string key, string? value)
        {
            SetOverrideCalls.Add((id, key, value));

            if (string.IsNullOrWhiteSpace(value))
            {
                ClearOverride(id, key);
                return true;
            }

            if (!PreservesPlaceholders(key, value))
                return false;   // placeholder-breaker — reject without persisting

            if (!_byLocale.TryGetValue(id, out var map))
                _byLocale[id] = map = new Dictionary<string, string>();
            map[key] = value;
            OverridesChanged?.Invoke(id);
            return true;
        }

        public void ClearOverride(string id, string key)
        {
            ClearOverrideCalls.Add((id, key));
            if (_byLocale.TryGetValue(id, out var map) && map.Remove(key))
            {
                if (map.Count == 0) _byLocale.Remove(id);
                OverridesChanged?.Invoke(id);
            }
        }

        public void ResetAll(string id)
        {
            if (_byLocale.Remove(id))
                OverridesChanged?.Invoke(id);
        }

        public IReadOnlyCollection<string> OverriddenIds => _byLocale.Keys.ToList();

        public bool PreservesPlaceholders(string canonicalKey, string candidate)
        {
            // Mirror the real service: a key absent from en imposes no placeholder requirement.
            var canonical = _canonical.TryGetValue(canonicalKey, out var c) ? c : candidate;
            return IndexSet(canonical).SetEquals(IndexSet(candidate));
        }

        private static HashSet<string> IndexSet(string s)
        {
            var stripped = s.Replace("{{", string.Empty).Replace("}}", string.Empty);
            return Regex.Matches(stripped, @"\{(\d+)(?:[,:][^}]*)?\}")
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);
        }

        public event Action<string>? OverridesChanged;
    }

    // ── Fixture helpers ────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, string> LoadCanonical()
    {
        var uri = new Uri("avares://HexIDE/Localization/Packs/en.json");
        using var stream = Avalonia.Platform.AssetLoader.Open(uri);
        var record = System.Text.Json.JsonSerializer.Deserialize(
            stream, LocalizationJsonContext.Default.LanguagePackRecord);
        return record?.Strings ?? new Dictionary<string, string>();
    }

    private static (TranslationEditorViewModel Vm, FakeUserTranslationsService User, LocalizationService Loc)
        CreateSut()
    {
        var user = new FakeUserTranslationsService(LoadCanonical());
        var loc = new LocalizationService(user);          // real service, fake user tier
        var windows = Substitute.For<IWindowManager>();
        var vm = new TranslationEditorViewModel(loc, user, windows);
        return (vm, user, loc);
    }

    private static LanguageInfo Lang(TranslationEditorViewModel vm, string id) =>
        vm.ToLanguages.Single(l => l.Id == id);

    /// <summary>Commits an edit exactly as the grid does: assign the row's ToValue (raises PropertyChanged,
    /// which the VM listens to and routes into its commit path).</summary>
    private static void CommitTo(TranslationRowViewModel row, string value) => row.ToValue = value;

    // ── From == en ⇒ no row is red ─────────────────────────────────────────

    [Fact]
    public void FromEnglish_NoRowIsRedFallback()
    {
        var (vm, _, _) = CreateSut();

        // From defaults to en; assert explicitly and confirm no red rows.
        vm.FromLanguage.Id.Should().Be("en");
        vm.Rows.Should().NotBeEmpty();
        vm.Rows.Should().NotContain(r => r.IsRedFallback);
    }

    // ── From == en-GB ⇒ red rows == exactly the keys not in GB's own chain ──

    [Fact]
    public void FromEnGb_RedRows_AreExactlyTheKeysNotInItsOwnShippedChain()
    {
        var (vm, _, loc) = CreateSut();

        // Drive the From cascade to en-GB: language en, region en-GB.
        vm.FromLanguage = Lang(vm, "en");
        vm.FromRegion = vm.FromRegions.Single(r => r.Id == "en-GB");
        vm.FromId.Should().Be("en-GB");

        var ownChain = loc.ShippedOwnChainKeys("en-GB");
        ownChain.Should().NotBeEmpty("en-GB ships a real pack with a handful of British-spelling overrides");

        // Red ⇔ the key is NOT defined in en-GB's own shipped chain (its "translation" is secretly English).
        var redKeys = vm.Rows.Where(r => r.IsRedFallback).Select(r => r.Key).ToHashSet();
        var expectedRed = vm.Rows.Select(r => r.Key).Where(k => !ownChain.Contains(k)).ToHashSet();

        redKeys.Should().BeEquivalentTo(expectedRed);
        // The set complements: every own-chain key is non-red, every other key is red.
        vm.Rows.Where(r => ownChain.Contains(r.Key)).Should().NotContain(r => r.IsRedFallback);
        // GB only overrides ~a dozen keys, so the overwhelming majority of rows are red.
        redKeys.Count.Should().BeGreaterThan(ownChain.Count);
    }

    // ── Commit a valid edit ⇒ IsOverridden + SetOverride called ─────────────

    [Fact]
    public void CommittingValidEdit_SetsOverridden_AndCallsSetOverride()
    {
        var (vm, user, _) = CreateSut();

        // Pick a concrete To language that is a real CultureInfo neutral.
        vm.ToLanguage = Lang(vm, "de");
        vm.ToId.Should().Be("de");

        // A plain (no-placeholder) key so any non-empty text is a valid override.
        var row = vm.Rows.First(r => r.Key == "Str.Menu.File");
        row.IsOverridden.Should().BeFalse();

        CommitTo(row, "Datei");

        row.IsOverridden.Should().BeTrue();
        row.HasValidationError.Should().BeFalse();
        user.SetOverrideCalls.Should().ContainSingle(c => c.Id == "de" && c.Key == "Str.Menu.File" && c.Value == "Datei");
        user.GetOverrides("de").Should().Contain(new KeyValuePair<string, string>("Str.Menu.File", "Datei"));
    }

    // ── Clearing a To cell ⇒ revert (not overridden) + ClearOverride ────────

    [Fact]
    public void ClearingToValue_RevertsToInherited_AndCallsClearOverride()
    {
        var (vm, user, loc) = CreateSut();

        vm.ToLanguage = Lang(vm, "de");
        var row = vm.Rows.First(r => r.Key == "Str.Menu.File");

        // Capture the as-shipped (inherited) value BEFORE any override exists — GetStringFrom merges the user
        // tier, so once an override is set it would report the override, not the value we revert to.
        var inherited = loc.GetStringFrom("de", "Str.Menu.File");

        // First set an override...
        CommitTo(row, "Datei");
        row.IsOverridden.Should().BeTrue();

        // ...then empty the cell — this reverts to the as-shipped (inherited) value.
        CommitTo(row, string.Empty);

        row.IsOverridden.Should().BeFalse();
        row.HasValidationError.Should().BeFalse();
        row.ToValue.Should().Be(inherited);
        user.ClearOverrideCalls.Should().Contain(c => c.Id == "de" && c.Key == "Str.Menu.File");
        user.GetOverrides("de").Should().NotContainKey("Str.Menu.File");
    }

    // ── Placeholder-breaking edit ⇒ validation error, NOT overridden ────────

    [Fact]
    public void PlaceholderBreakingEdit_SetsValidationError_AndDoesNotOverride()
    {
        var (vm, user, _) = CreateSut();

        vm.ToLanguage = Lang(vm, "de");

        // Find a canonical key that actually carries a {0} placeholder so the breaker path is real.
        var canonical = LoadCanonical();
        var placeholderKey = canonical.First(kv => kv.Value.Contains("{0}")).Key;
        var row = vm.Rows.First(r => r.Key == placeholderKey);
        row.IsOverridden.Should().BeFalse();

        // Drop the {0} → placeholder set no longer matches → SetOverride returns false.
        CommitTo(row, "no placeholder here");

        row.HasValidationError.Should().BeTrue();
        row.IsOverridden.Should().BeFalse("a rejected edit must not flip the override marker");
        user.SetOverrideCalls.Should().Contain(c => c.Id == "de" && c.Key == placeholderKey);
        user.GetOverrides("de").Should().NotContainKey(placeholderKey);
    }

    // ── Pickers come from CultureInfo, never from pack ids ──────────────────

    [Fact]
    public void LanguageLists_DoNotContainPseudoPackIds()
    {
        var (vm, _, _) = CreateSut();

        var fromIds = vm.FromLanguages.Select(l => l.Id).ToList();
        var toIds = vm.ToLanguages.Select(l => l.Id).ToList();

        fromIds.Should().NotContain(["pseudo", "pseudo-rtl"]);
        toIds.Should().NotContain(["pseudo", "pseudo-rtl"]);
        // ...and the synthetic "system" sentinel is a pack id too, never a CultureInfo neutral.
        fromIds.Should().NotContain("system");
        toIds.Should().NotContain("system");
        // Sanity: real CultureInfo neutrals are present.
        fromIds.Should().Contain(["en", "de", "fr"]);
    }
}
