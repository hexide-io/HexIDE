using System.Text.RegularExpressions;
using HexIDE.Addins;

namespace HexIDE.Integration.Tests.Addins;

/// <summary>
/// The grants HexIDE gives, read against what the trust and consent dialogs read (#5396). A grant that could reach one of
/// those keys would let its owner change what a person reads before trusting an add-in.
/// </summary>
public partial class RegistrationGrantsTests
{
    // Every view a person reads before trusting an add-in or a workspace's artifacts.
    private static readonly string[] TrustViews =
    [
        "AddinConsentDialog.axaml",
        "TrustChainView.axaml",
        "WorkspaceArtifactConsentView.axaml",
    ];

    // The assemblies those views are built of. A grant to key a type in one of them would restyle a control they show.
    private static readonly string[] ViewAssemblies = ["HexIDE", "HexIDE.Core", "Classic.Avalonia.Theme", "Classic.CommonControls.Avalonia"];

    [GeneratedRegex(@"\{(?:Dynamic|Static)Resource\s+(?:ResourceKey=)?([A-Za-z_][\w.]*)\s*\}")]
    private static partial Regex ResourceReference();

    private static IReadOnlyList<string> KeysTheTrustViewsRead()
    {
        var views = Path.Combine(RepoTree.Root(), "IDE", "HexIDE", "Forms", "Views");
        return TrustViews.SelectMany(view => ResourceReference().Matches(File.ReadAllText(Path.Combine(views, view)))
            .Select(match => match.Groups[1].Value)).Distinct().ToList();
    }

    [Fact]
    public void The_trust_views_are_read()
    {
        // A pattern that matched nothing would pass every test below.
        var keys = KeysTheTrustViewsRead();
        keys.Should().Contain("Str.Dialog.AddinConsent.Allow");
        keys.Should().Contain("WarningBannerBackgroundBrush");
    }

    [Fact]
    public void No_grant_covers_a_key_the_trust_and_consent_views_read()
    {
        var keys = KeysTheTrustViewsRead();
        foreach (var grant in RegistrationGrants.All)
            keys.Where(grant.CoversResourceKey).Should().BeEmpty($"{grant.OwnerId}'s grant must not reach the trust views' keys");
    }

    [Fact]
    public void No_grant_covers_the_IDEs_text_or_everything()
    {
        foreach (var grant in RegistrationGrants.All)
            grant.ResourceKeys.Should().NotContain(family => family.StartsWith("Str", StringComparison.Ordinal) || family == "*"
                || family.Length < 3, $"{grant.OwnerId}'s grant names a family wide enough to reach the IDE's text");
    }

    [Fact]
    public void No_grant_covers_a_type_the_trust_views_are_built_of()
    {
        foreach (var grant in RegistrationGrants.All)
        {
            grant.ResourceTypeAssemblies.Should().NotContain(assembly => ViewAssemblies.Contains(assembly)
                || assembly.StartsWith("Avalonia", StringComparison.Ordinal) || assembly.StartsWith("Dock.", StringComparison.Ordinal),
                $"{grant.OwnerId}'s grant could restyle a control the trust views show");
        }
    }

    [Fact]
    public void Only_the_composed_provider_holds_its_grant()
    {
        var composed = new AddinOrigin(RegistrationGrants.BasicProvider, "HexIDE.Basic") { IsComposed = true };
        RegistrationGrants.For(composed).Should().NotBeNull();

        // A package that names itself after the provider, signed or not, is not the provider.
        RegistrationGrants.For(new AddinOrigin(RegistrationGrants.BasicProvider, "HexIDE.Basic")).Should().BeNull();
        RegistrationGrants.For(new AddinOrigin(RegistrationGrants.BasicProvider, "HexIDE.Basic", IsFirstParty: true)).Should().BeNull();
        RegistrationGrants.For(composed with { IsDeveloperUnsigned = true }).Should().BeNull();
        RegistrationGrants.For(new AddinOrigin("someone.else", "Someone") { IsComposed = true }).Should().BeNull();
    }
}
