using System.Linq;
using HexIDE.Localization;

namespace HexIDE.Tests;

/// <summary>
/// <see cref="CultureCatalog"/> is the all-CultureInfo cascade behind the translation editor's From/To
/// pickers. CultureInfo content varies by OS/runtime (ICU vs NLS), so these assert membership and
/// <c>Parent</c>/direction invariants — never exact list sizes.
/// </summary>
public class CultureCatalogTests
{
    [Fact]
    public void Languages_IsNonEmpty_AndExcludesInvariantCulture()
    {
        CultureCatalog.Languages.Should().NotBeEmpty();
        // The invariant culture has an empty Name/Id — it must never appear as a selectable language.
        CultureCatalog.Languages.Should().NotContain(l => string.IsNullOrEmpty(l.Id));
    }

    [Fact]
    public void Languages_IncludeCommonNeutrals_AndChineseScriptNeutrals()
    {
        var ids = CultureCatalog.Languages.Select(l => l.Id).ToList();
        ids.Should().Contain(["en", "de", "ar", "he", "zh-Hans", "zh-Hant"]);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("he")]
    public void Languages_RtlNeutrals_DeclareRtlDirection(string id)
    {
        var info = CultureCatalog.Languages.Single(l => l.Id == id);
        info.Direction.Should().Be("rtl");
    }

    [Fact]
    public void RegionsFor_German_ContainsAustriaAndGermany()
    {
        CultureCatalog.RegionsFor("de").Select(r => r.Id).Should().Contain(["de-AT", "de-DE"]);
    }

    [Theory]
    [InlineData("de-AT", "de")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("de", "de")]   // a neutral maps to itself
    [InlineData("en", "en")]
    public void NeutralOf_ResolvesToTheExpectedNeutral(string id, string expectedNeutral)
    {
        CultureCatalog.NeutralOf(id).Should().Be(expectedNeutral);
    }
}
