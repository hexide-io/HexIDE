using HexIDE.Addins;

namespace HexIDE.Tests.Addins;

/// <summary>
/// The revocation decision, with its first-party exemption: a BUILD (hash) revocation hits everyone
/// (so HexIDE can pull a bad first-party release); PUBLISHER / INTERMEDIATE revocation exempt key-pinned
/// first-party add-ins (so a marketplace-key compromise can't disable the bundled add-ins).
/// </summary>
public class RevocationCheckTests
{
    private static RevocationList List(string[]? hashes = null, string[]? pubs = null, string[]? inters = null) =>
        new()
        {
            Revoked = new RevokedSets
            {
                ManifestHashes = hashes ?? [],
                PublisherIds = pubs ?? [],
                IntermediateIds = inters ?? [],
            },
        };

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
}
