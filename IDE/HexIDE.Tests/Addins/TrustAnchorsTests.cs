using HexIDE.Addins;

namespace HexIDE.Tests.Addins;

/// <summary>
/// The embedded trust-anchor fingerprints must equal the canonical values published in <c>TRUST.md</c>
/// (and echoed in the About dialog). If the committed dev keys are rotated, this fails — a deliberate
/// reminder to update the published values so the trust inspector stays externally verifiable.
/// </summary>
public class TrustAnchorsTests
{
    private const string PublishedRootFingerprint =
        "DF36 B067 BF3A C047 CC28 B9E4 D5D4 B323 A491 84D4 C28E B0ED F9E8 9928 89D3 F491";
    private const string PublishedFirstPartyFingerprint =
        "7929 689A 38A1 DF58 9869 3FD3 2B67 1249 909C 99D5 C7F6 6092 22D4 FB35 7312 B4C8";

    [Fact]
    public void Embedded_anchor_fingerprints_match_the_published_values()
    {
        TrustAnchors.RootFingerprint.Should().Be(PublishedRootFingerprint);
        TrustAnchors.FirstPartyFingerprint.Should().Be(PublishedFirstPartyFingerprint);
    }
}
