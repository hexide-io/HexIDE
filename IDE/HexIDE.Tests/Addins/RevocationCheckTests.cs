using HexIDE.Addins;

namespace HexIDE.Tests.Addins;

/// <summary>
/// The revocation decision, with its first-party exemption: a BUILD (hash) revocation hits everyone
/// (so HexIDE can pull a bad first-party release); PUBLISHER / INTERMEDIATE revocation exempt key-pinned
/// first-party add-ins (so a marketplace-key compromise can't disable the bundled add-ins).
/// </summary>
public class RevocationCheckTests
{
    private static RevocationList List(string[]? hashes = null, string[]? pubs = null, string[]? inters = null,
        string[]? keys = null) =>
        new()
        {
            Revoked = new RevokedSets
            {
                ManifestHashes = hashes ?? [],
                PublisherIds = pubs ?? [],
                IntermediateIds = inters ?? [],
                KeyFingerprints = keys ?? [],
            },
        };

    // Two keys' fingerprints, grouped as PackageCrypto.KeyFingerprint gives them and as TrustLink carries them.
    private static readonly string PublisherKey = string.Join(" ", Enumerable.Repeat("A1B2", 16));
    private static readonly string IntermediateKey = string.Join(" ", Enumerable.Repeat("C3D4", 16));

    [Fact]
    public void Empty_list_revokes_nothing()
    {
        RevocationCheck.Reason(List(), "h", "p", "i", isFirstParty: false).Should().BeNull();
        RevocationCheck.Reason(List(), "h", "p", "i", isFirstParty: true).Should().BeNull();
    }

    [Fact]
    public void Build_revocation_hits_everyone_including_first_party()
    {
        RevocationCheck.Reason(List(hashes: ["h"]), "h", "p", "i", isFirstParty: false).Should().Be("build revoked");
        RevocationCheck.Reason(List(hashes: ["h"]), "h", "p", "i", isFirstParty: true).Should().Be("build revoked");
    }

    [Fact]
    public void Publisher_revocation_hits_third_party_but_exempts_first_party()
    {
        RevocationCheck.Reason(List(pubs: ["p"]), "h", "p", "i", isFirstParty: false).Should().Be("publisher revoked");
        RevocationCheck.Reason(List(pubs: ["p"]), "h", "p", "i", isFirstParty: true).Should().BeNull();
    }

    [Fact]
    public void Intermediate_revocation_hits_third_party_but_exempts_first_party()
    {
        RevocationCheck.Reason(List(inters: ["i"]), "h", "p", "i", isFirstParty: false).Should().Be("intermediate revoked");
        RevocationCheck.Reason(List(inters: ["i"]), "h", "p", "i", isFirstParty: true).Should().BeNull();
    }

    [Fact]
    public void Null_or_empty_identity_fields_never_match()
    {
        RevocationCheck.Reason(List(pubs: [""], inters: [""]), null, null, null, isFirstParty: false).Should().BeNull();
    }

    // ── Keys (#5315) ─────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_revoked_publisher_key_reaches_every_package_it_signed_first_party_included(bool firstParty)
    {
        RevocationCheck.Reason(List(keys: [PublisherKey]), "h", "p", "i", firstParty, PublisherKey, IntermediateKey)
            .Should().Be("publisher key revoked", "revoking the first-party key is a deliberate act about that key");
    }

    [Fact]
    public void A_revoked_intermediate_key_reaches_third_party_but_exempts_first_party()
    {
        RevocationCheck.Reason(List(keys: [IntermediateKey]), "h", "p", "i", false, PublisherKey, IntermediateKey)
            .Should().Be("intermediate key revoked");
        RevocationCheck.Reason(List(keys: [IntermediateKey]), "h", "p", "i", true, PublisherKey, IntermediateKey)
            .Should().BeNull("a stolen marketplace key must not switch off the bundled add-ins, as with its id");
    }

    [Theory]
    [InlineData("a1b2a1b2a1b2a1b2a1b2a1b2a1b2a1b2a1b2a1b2a1b2a1b2a1b2a1b2a1b2a1b2")]
    [InlineData("A1B2 A1B2 A1B2 A1B2 A1B2 A1B2 A1B2 A1B2 A1B2 A1B2 A1B2 A1B2 A1B2 A1B2 A1B2 A1B2")]
    [InlineData("A1B2A1B2 a1b2a1b2 A1B2A1B2 a1b2a1b2 A1B2A1B2 a1b2a1b2 A1B2A1B2 a1b2a1b2")]
    public void A_listed_fingerprint_matches_whatever_its_case_and_spacing(string listed)
    {
        RevocationCheck.Reason(List(keys: [listed]), "h", "p", "i", false, PublisherKey, null).Should().Be("publisher key revoked");
    }

    [Theory]
    [InlineData("A1B2")]
    [InlineData("A1B2A1B2A1B2A1B2A1B2A1B2A1B2A1B2A1B2A1B2A1B2A1B2A1B2A1B2A1B2A1B")]
    [InlineData("G1B2A1B2A1B2A1B2A1B2A1B2A1B2A1B2A1B2A1B2A1B2A1B2A1B2A1B2A1B2A1B2")]
    [InlineData("")]
    public void An_entry_that_isn_t_a_fingerprint_matches_nothing(string listed)
    {
        RevocationCheck.Reason(List(keys: [listed]), "h", "p", "i", false, PublisherKey, IntermediateKey).Should().BeNull();
        RevocationCheck.IsKeyRevoked(List(keys: [listed]), listed).Should().BeFalse();
    }

    [Fact]
    public void A_package_whose_keys_aren_t_known_matches_no_key()
    {
        RevocationCheck.Reason(List(keys: [PublisherKey, IntermediateKey]), "h", "p", "i", false, null, null).Should().BeNull();
        RevocationCheck.Reason(List(keys: [PublisherKey]), "h", "p", "i", isFirstParty: false)
            .Should().BeNull("the overload without keys, shipped before #5315, checks none");
    }

    [Fact]
    public void Id_and_build_revocation_are_unchanged_beside_keys()
    {
        RevocationCheck.Reason(List(hashes: ["h"], keys: [PublisherKey]), "h", "p", "i", false, PublisherKey, null)
            .Should().Be("build revoked");
        RevocationCheck.Reason(List(pubs: ["p"]), "h", "p", "i", false, PublisherKey, IntermediateKey).Should().Be("publisher revoked");
        RevocationCheck.Reason(List(pubs: ["p"]), "h", "p", "i", true, PublisherKey, IntermediateKey).Should().BeNull();
    }
}
