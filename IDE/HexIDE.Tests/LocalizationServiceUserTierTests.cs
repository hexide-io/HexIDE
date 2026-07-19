using System.Collections.Generic;
using System.Linq;
using HexIDE.Localization;

namespace HexIDE.Tests;

/// <summary>
/// Exercises the per-user override tier that <see cref="LocalizationService"/> merges LAST in resolution
/// (<c>en ← shipped-neutral ← shipped-region ← user(neutral) ← user(region)</c>). The fake
/// <see cref="IUserTranslationsService"/> supplies overrides directly so these tests never touch the disk.
/// Assertions stay robust to which exact strings ship: they compare against the as-shipped value rather
/// than hardcoding translated text (except canonical <c>en</c>, which is fixed).
/// </summary>
public class LocalizationServiceUserTierTests
{
    public LocalizationServiceUserTierTests() => AvaloniaTestSetup.EnsureInitialized();

    /// <summary>Builds a stub <see cref="IUserTranslationsService"/> backed by an in-memory per-id map.
    /// Only <see cref="IUserTranslationsService.GetOverrides"/> is consulted by the resolution path.</summary>
    private static IUserTranslationsService FakeUserTranslations(
        Dictionary<string, Dictionary<string, string>> overridesById)
    {
        var fake = Substitute.For<IUserTranslationsService>();
        fake.GetOverrides(Arg.Any<string>()).Returns(ci =>
        {
            var id = ci.ArgAt<string>(0);
            return overridesById.TryGetValue(id, out var map)
                ? (IReadOnlyDictionary<string, string>)map
                : new Dictionary<string, string>();
        });
        return fake;
    }

    private static LocalizationService CreateSut(Dictionary<string, Dictionary<string, string>> overridesById) =>
        new(FakeUserTranslations(overridesById));

    [Fact]
    public void UserOverrideOnNeutral_ShowsThrough_WhenNeutralApplied()
    {
        var sut = CreateSut(new()
        {
            ["de"] = new() { ["Str.Menu.File"] = "MeineDatei" },
        });

        sut.Apply("de");

        sut.GetString("Str.Menu.File").Should().Be("MeineDatei");
    }

    [Fact]
    public void UserOverrideOnNeutral_ShowsThrough_WhenRegionApplied()
    {
        // An override on neutral `de` must bleed through to a region under it (de-AT) — it beats the
        // shipped neutral/region tiers and is inherited by the region exactly like a shipped neutral string.
        var sut = CreateSut(new()
        {
            ["de"] = new() { ["Str.Menu.File"] = "MeineDatei" },
        });

        sut.Apply("de-AT");

        sut.ActiveLanguage.Should().Be("de-AT");
        sut.GetString("Str.Menu.File").Should().Be("MeineDatei");
    }

    [Fact]
    public void UserOverrideOnRegion_AppliesToThatRegion()
    {
        var sut = CreateSut(new()
        {
            ["de-AT"] = new() { ["Str.Menu.File"] = "ÖsterreichDatei" },
        });

        sut.Apply("de-AT");

        sut.GetString("Str.Menu.File").Should().Be("ÖsterreichDatei");
    }

    [Fact]
    public void UserOverrideOnRegion_DoesNotApplyToBareNeutral()
    {
        // A region-keyed override (de-AT) must NOT leak up into the bare neutral (de): applying `de`
        // resolves to the as-shipped German value, not the Austrian override.
        var overrides = new Dictionary<string, Dictionary<string, string>>
        {
            ["de-AT"] = new() { ["Str.Menu.File"] = "ÖsterreichDatei" },
        };

        // The as-shipped neutral value, captured with no user overrides present.
        var shippedDe = new LocalizationService(FakeUserTranslations(new()));
        shippedDe.Apply("de");
        var shippedFile = shippedDe.GetString("Str.Menu.File");

        var sut = CreateSut(overrides);
        sut.Apply("de");

        sut.GetString("Str.Menu.File").Should().Be(shippedFile);
        sut.GetString("Str.Menu.File").Should().NotBe("ÖsterreichDatei");
    }

    [Fact]
    public void UserOverride_BeatsShippedPackValue_ForAKeyTheShippedPackDefines()
    {
        // `fr` ships a real translation of Str.Menu.File (≠ "_File"). A user override on `fr` must win
        // over that shipped value, proving the user tier merges after the shipped neutral pack.
        var shippedFr = new LocalizationService(FakeUserTranslations(new()));
        shippedFr.Apply("fr");
        var shippedFrFile = shippedFr.GetString("Str.Menu.File");
        shippedFrFile.Should().NotBe("_File");   // guard: the shipped fr pack really translates this key

        var sut = CreateSut(new()
        {
            ["fr"] = new() { ["Str.Menu.File"] = "MonFichierPerso" },
        });
        sut.Apply("fr");

        sut.GetString("Str.Menu.File").Should().Be("MonFichierPerso");
        sut.GetString("Str.Menu.File").Should().NotBe(shippedFrFile);
    }

    [Fact]
    public void UserOverrideOnEn_ShowsThrough_ViaTheEnglishFastPath()
    {
        // `en` is the canonical fast-path; a user override on `en` (authoring the canonical) is honored.
        var sut = CreateSut(new()
        {
            ["en"] = new() { ["Str.Menu.File"] = "CustomFile" },
        });

        sut.Apply("en");

        sut.GetString("Str.Menu.File").Should().Be("CustomFile");
    }

    [Fact]
    public void GetStringFrom_UnshippedCultureWithOnlyUserOverrides_ReturnsTheOverride()
    {
        // ca-ES (Catalan) ships no pack. With only a user override present, the found path must resolve it
        // to the override rather than failing back to canonical English.
        var sut = CreateSut(new()
        {
            ["ca"] = new() { ["Str.Menu.File"] = "Fitxer" },
        });

        sut.GetStringFrom("ca-ES", "Str.Menu.File").Should().Be("Fitxer");
    }

    [Fact]
    public void Apply_UnshippedCultureWithOnlyUserOverrides_ResolvesToTheOverride_NotEnglish()
    {
        var sut = CreateSut(new()
        {
            ["ca"] = new() { ["Str.Menu.File"] = "Fitxer" },
        });

        sut.Apply("ca-ES");

        // The override marks the otherwise-unshipped culture as resolved, so the active id sticks
        // (it does not fall back to "en") and the override is the effective value.
        sut.ActiveLanguage.Should().Be("ca-ES");
        sut.GetString("Str.Menu.File").Should().Be("Fitxer");
    }

    [Fact]
    public void ShippedOwnChainKeys_ForEn_IsEmpty()
    {
        // `en` is the canonical itself — nothing is an "en-only" fallback above it.
        var sut = CreateSut(new());

        sut.ShippedOwnChainKeys("en").Should().BeEmpty();
    }

    [Fact]
    public void ShippedOwnChainKeys_ForEnGb_IsNonEmpty_AndASubsetOfCanonicalEn()
    {
        // en-GB ships a real pack (~18 overrides like "Customise..."). Its own-chain keys must be a subset
        // of the canonical en key set (en-GB only overrides keys that already exist in en).
        var sut = CreateSut(new());

        var ownChain = sut.ShippedOwnChainKeys("en-GB");
        ownChain.Should().NotBeEmpty();

        // Every key en-GB overrides resolves in en (proving membership without hardcoding the count).
        foreach (var key in ownChain)
            sut.GetStringFrom("en", key).Should().NotBe(key, $"'{key}' should be a real canonical en key");

        // Spot-check a couple of the known GB overrides are surfaced as own-chain keys.
        ownChain.Should().Contain("Str.Toolbar.Customize");
        ownChain.Should().Contain("Str.PropCategory.Behavior");
    }
}
